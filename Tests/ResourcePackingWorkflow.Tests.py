import importlib.util,json,pathlib,sys,tempfile,unittest
ROOT=pathlib.Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'Tools/ResourcePacking'))
import resource_workflow as w
class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.temp.name)
        self.source=self.root/'source'
        for folder in ['Assets','Packages','ProjectSettings']:(self.source/folder).mkdir(parents=True)
        (self.source/'Assets/data.asset').write_text('data',encoding='utf-8');(self.source/'Assets/game.cs').write_text('code',encoding='utf-8')
        self.snapshot=self.root/'verified.json';w.snapshot(self.source,self.snapshot)
    def tearDown(self):self.temp.cleanup()
    def fake_build(self,rid,previous=None,bundle=b'asset-v1',mode='baseline'):
        out=self.root/rid;payload=out/'Payload/resources'/rid;(payload/'bundles').mkdir(parents=True)
        file=payload/'bundles/data.bundle';file.write_bytes(bundle);catalog=payload/'catalog_0.1.json';catalog.write_text('{}')
        state=out/'ContentState/state.bin';state.parent.mkdir();state.write_bytes(b'original state')
        contract=out/'contract.json';w.write_json(contract,{'kind':'fixture-contract'})
        ss=out/'inputs.json';ss.write_bytes(self.snapshot.read_bytes())
        manifest={'resourceReleaseId':rid,'scriptTypeHash':'f'*32,'bundles':[{'relativePath':'bundles/data.bundle','bundleName':'bundle','cacheHash':w.digest(bundle)[:32],'sha256':w.digest(bundle),'bytes':len(bundle),'crc':1}]}
        w.write_json(payload/'resource-manifest.json',manifest)
        r={'status':'succeeded','allBundlesNativeCrcVerified':True,'assetDependencyOrderVerified':True,'resourceReleaseId':rid,'scriptTypeHash':'f'*32,
            'catalogPath':str(catalog),'resourceCatalogSha256':w.file_hash(catalog),'resourceManifestSha256':w.file_hash(payload/'resource-manifest.json'),
            'workflowProtocol':1,'resourceBuildMode':mode,'nativeLoadRoot':'http://127.0.0.1/native-stable','sourceSnapshotPath':str(ss),'sourceSnapshotSha256':w.file_hash(ss),
            'layoutContractPath':str(contract),'contentStatePath':str(state)}
        if previous:
            _,_,pw=w.workflow(previous);r.update(baselineResourceReleaseId=pw['baselineResourceReleaseId'],baselineResourceReport=pw['baselineResourceReport'])
        report=out/'resource-build-report.json';w.write_json(report,r);w.finalize(report,previous);return report
    def test_method_code_change_reuses_resources_but_asset_change_does_not(self):
        base=self.fake_build('a'*32)
        (self.source/'Assets/game.cs').write_text('new method body')
        next=self.root/'code-only.json';w.snapshot(self.source,next)
        self.assertEqual(w.choose('auto',next,base)['mode'],'reuse')
        (self.source/'Assets/data.asset').write_text('new asset')
        changed=self.root/'asset-change.json';w.snapshot(self.source,changed)
        self.assertEqual(w.choose('auto',changed,base)['mode'],'update')
        with self.assertRaisesRegex(ValueError,'inputs changed'):w.choose('reuse',changed,base)
    def test_snapshot_rejects_changed_added_and_expired_inputs(self):
        (self.source/'Assets/data.asset').write_text('changed')
        with self.assertRaisesRegex(ValueError,'changed'):w.verify_snapshot(self.source,self.snapshot)
        (self.source/'Assets/data.asset').write_text('data');(self.source/'Assets/new.asset').write_text('new')
        with self.assertRaisesRegex(ValueError,'inventory'):w.verify_snapshot(self.source,self.snapshot)
        (self.source/'Assets/new.asset').unlink()
        d=w.read_json(self.snapshot);d['verifiedAtUtc']=(w.utcnow()-w.dt.timedelta(hours=2)).isoformat();w.write_json(self.snapshot,d)
        with self.assertRaisesRegex(ValueError,'expired'):w.verify_snapshot(self.source,self.snapshot)
    def test_failed_or_forged_snapshot_not_accepted(self):
        d=w.read_json(self.snapshot);d['status']='failed';w.write_json(self.snapshot,d)
        with self.assertRaisesRegex(ValueError,'Invalid source'):w.verify_snapshot(self.source,self.snapshot)
    def test_no_change_and_one_bundle_delta(self):
        base=self.fake_build('a'*32);same=self.fake_build('b'*32,base,mode='update');new=self.fake_build('c'*32,same,b'asset-v2',mode='update')
        self.assertEqual(w.read_json(same.parent/'resource-download-delta.json')['estimatedDownloadBytesWithCompletePreviousCache'],0)
        self.assertEqual(w.read_json(new.parent/'resource-download-delta.json')['newOrChangedBundleCount'],1)
    def test_update_chain_keeps_original_full_state(self):
        base=self.fake_build('a'*32);first=self.fake_build('b'*32,base,b'v2','update');second=self.fake_build('c'*32,first,b'v3','update')
        _,_,anchor=w.workflow(base);_,_,latest=w.workflow(second)
        self.assertEqual(latest['contentState'],anchor['contentState']);self.assertEqual(latest['baselineResourceReport'],str(base))
    def test_tampered_state_and_workflow_are_rejected(self):
        base=self.fake_build('a'*32);_,_,descriptor=w.workflow(base)
        pathlib.Path(descriptor['contentState']['path']).write_bytes(b'changed state')
        with self.assertRaisesRegex(ValueError,'state changed'):w.workflow(base)
    def test_cache_identity_rebind_is_an_error(self):
        a={'bundleName':'x','cacheHash':'a'*32,'sha256':'b'*64,'bytes':1,'relativePath':'x'}
        b=dict(a,sha256='c'*64)
        with self.assertRaisesRegex(ValueError,'rebound'):w.delta({'bundles':[a]},{'bundles':[b]})
    def test_identical_bytes_with_new_cache_key_are_counted(self):
        a={'bundleName':'x','cacheHash':'a'*32,'sha256':'b'*64,'bytes':7,'relativePath':'x'};b=dict(a,cacheHash='c'*32)
        d=w.delta({'bundles':[a]},{'bundles':[b]});self.assertEqual(d['estimatedDownloadBytesWithCompletePreviousCache'],7);self.assertEqual(d['sameBytesButNewCacheIdentity'],['x'])
    def test_legacy_report_cannot_be_incremental_baseline(self):
        base=self.fake_build('a'*32);r=w.read_json(base);r.pop('workflowProtocol');w.write_json(base,r)
        with self.assertRaisesRegex(ValueError,'provenance'):w.workflow(base)
    def test_archived_baseline_is_not_overwritten_by_update(self):
        base=self.fake_build('a'*32);_,_,anchor=w.workflow(base);state=pathlib.Path(anchor['contentState']['path']);old=state.read_bytes()
        self.fake_build('b'*32,base,b'changed','update');self.assertEqual(state.read_bytes(),old)
    def test_reuse_rejection_creates_no_partial_output(self):
        base=self.fake_build('a'*32);out=self.root/'not-created'
        with self.assertRaisesRegex(ValueError,'No resource build'):
            w.request(self.source,self.snapshot,'auto',out,'b'*32,base)
        self.assertFalse(out.exists())
    def test_snapshot_cannot_write_into_project_inputs(self):
        for folder in ('Assets','Packages','ProjectSettings'):
            with self.assertRaisesRegex(ValueError,'source inputs'):
                w.snapshot(self.source,self.source/folder/'verification.json')
            self.assertFalse((self.source/folder/'verification.json').exists())
if __name__=='__main__':unittest.main(verbosity=2)
