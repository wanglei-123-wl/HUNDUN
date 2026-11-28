import base64
import copy
import datetime as dt
import functools
import hashlib
import http.server
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import threading
import unittest
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Tools/HotUpdate'))
from release_common import *
from release_package import build_package, verify_package, extract_archive
from release_transport import describe, transfer_archive
from release_store import Store
from release_retention import plan_retention

PWSH = r'C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'

class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *_): pass

class ReleaseToolsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix='QiDaoReleaseTests-')
        cls.base = Path(cls.temporary.name)
        private = cls.base / 'test-private.xml'
        command = '$k=[Security.Cryptography.RSA]::Create(2048);[IO.File]::WriteAllText($args[0],$k.ToXmlString($true));$k.Dispose()'
        script = cls.base / 'key.ps1'; script.write_text(command, encoding='utf-8')
        subprocess.run([PWSH, '-NoProfile', '-File', str(script), str(private)], check=True, capture_output=True)
        key = ET.fromstring(private.read_text()); cls.n = int.from_bytes(base64.b64decode(key.findtext('Modulus')), 'big')
        cls.d = int.from_bytes(base64.b64decode(key.findtext('D')), 'big')
        public = '<RSAKeyValue><Modulus>' + key.findtext('Modulus') + '</Modulus><Exponent>' + key.findtext('Exponent') + '</Exponent></RSAKeyValue>'
        cls.config = {'publicKeyXml': public, 'unityVersion': '2022.3.62f1', 'bootstrapApiVersion': 1, 'maxCodeBytes': 67108864}
        cls.policy = read_json(ROOT / 'Tools/HotUpdate/RetentionPolicy.json')

    @classmethod
    def tearDownClass(cls): cls.temporary.cleanup()

    @classmethod
    def sign(cls, body):
        raw = json_bytes(body); length = (cls.n.bit_length() + 7) // 8
        tail = bytes.fromhex('3031300d060960864801650304020105000420') + hashlib.sha256(raw).digest()
        encoded = b'\0\1' + b'\xff' * (length-len(tail)-3) + b'\0' + tail
        signature = pow(int.from_bytes(encoded, 'big'), cls.d, cls.n).to_bytes(length, 'big')
        return {'payload': base64.b64encode(raw).decode(), 'signature': base64.b64encode(signature).decode()}

    def setUp(self):
        self.path = self.base / uuid.uuid4().hex; self.path.mkdir()
        self.private = self.path / 'private'; self.public = self.path / 'public' / CHANNEL.lstrip('/')
        self.store = Store.initialize(self.private, self.public, self.config, self.policy, True)
        self.http = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(QuietHandler, directory=str(self.path/'public')))
        self.thread = threading.Thread(target=self.http.serve_forever, daemon=True); self.thread.start()
        self.url = 'http://127.0.0.1:' + str(self.http.server_port) + CHANNEL

    def tearDown(self): self.http.shutdown(); self.http.server_close()

    def fixture(self, sequence=1, resource_id=None, code=b'game-code', proof=True):
        rid = '%032x' % sequence; resource_id = resource_id or '%032x' % (1000+sequence)
        source = self.path / ('source-'+rid); source.mkdir()
        resources = source/'resources'; resources.mkdir(); (resources/'bundles').mkdir()
        bundle = b'identical-resource-content'
        (resources/'bundles/content.bundle').write_bytes(bundle)
        catalog = b'{"catalog":"fixture"}'; (resources/'catalog_0.1.json').write_bytes(catalog)
        (resources/'catalog_0.1.hash').write_text('c'*32)
        release = dict(schemaVersion=1,releaseId=rid,sequence=sequence,target=TARGET,unityVersion=self.config['unityVersion'],bootstrapApiVersion=1,
            assemblyName='QiDao.GameLogic',entryType='QiDao.GameLogic.GameEntry',codePath='releases/'+rid+'/'+DLL,codeBytes=len(code),codeSha256=digest(code),
            resourceReleaseId=resource_id,resourceCatalogHash='c'*32,resourceCatalogSha256=digest(catalog),resourceCatalogBytes=len(catalog),
            scriptTypeHash='d'*32,resourceBuildRoot='http://47.93.78.106:18761'+CHANNEL+'/resources/'+resource_id)
        manifest = dict(schemaVersion=1,resourceReleaseId=resource_id,target=TARGET,unityVersion=release['unityVersion'],gameAssemblyName=release['assemblyName'],
            scriptTypeHash=release['scriptTypeHash'],catalogSha256=release['resourceCatalogSha256'],catalogHash=release['resourceCatalogHash'],catalogBytes=len(catalog),buildRoot=release['resourceBuildRoot'],
            bundles=[dict(relativePath='bundles/content.bundle',sha256=digest(bundle),cacheHash='e'*32,bundleName='content',bytes=len(bundle),crc=1)])
        write_json(resources/'resource-manifest.json',manifest)
        release.update(resourceManifestSha256=file_hash(resources/'resource-manifest.json'),resourceManifestBytes=(resources/'resource-manifest.json').stat().st_size)
        code_dir=source/'code'; (code_dir/'releases'/rid).mkdir(parents=True); (code_dir/release['codePath']).write_bytes(code)
        write_json(code_dir/'current.json',self.sign(release))
        write_json(code_dir/'code-build-report.json',dict(status='succeeded',resourceCompatibilityVerified=True,codeSha256=release['codeSha256'],probeVariant='A'))
        report=source/'resource-report.json';write_json(report,dict(status='succeeded',allBundlesNativeCrcVerified=True,assetDependencyOrderVerified=True,
            resourceReleaseId=resource_id,scriptTypeHash=release['scriptTypeHash'],catalogPath=str(resources/'catalog_0.1.json')))
        proof_path=None
        if proof:
            proof_path=source/'proof.json';write_json(proof_path,self.sign(dict(kind='QiDaoBaseClientProof',schemaVersion=1,status='verified',target=TARGET,
                unityVersion=release['unityVersion'],bootstrapApiVersion=1,publisherKeySha256=digest(self.config['publicKeyXml'].encode()),baseBuildGuid='f'*32,
                baseFilesSha256='1'*64,businessDllExcluded=True,preloadExecutionVerified=True,resourcePairVerified=True,acceptanceOnly=True)))
        package=source/'package';build_package(code_dir,report,package,self.config,proof_path,True)
        return package,release

    def stage(self, package):
        descriptor=describe(package,self.config,True);offer=self.store.offer(descriptor)
        archive=package.parent/'transfer.tar';meta=transfer_archive(package,descriptor,offer,archive)
        with archive.open('rb') as stream: result=self.store.receive(offer['releaseId'],stream,meta['bytes'],meta['sha256'])
        return offer,result

    def test_signature_tamper_and_package_unknown_files_rejected(self):
        package,r=self.fixture();verify_package(package,self.config,True)
        (package/'private.xml').write_text('must-not-upload')
        with self.assertRaises(ValueError):verify_package(package,self.config,True)
        envelope=self.sign(r);envelope['payload']=base64.b64encode(b'{}').decode()
        with self.assertRaises(ValueError):verify_envelope(envelope,self.config)

    def test_stage_publish_dedup_and_no_staging_copy_growth(self):
        package,r=self.fixture();self.stage(package)
        self.assertFalse((self.public/'current.json').exists())
        self.store.publish(r['releaseId'],self.url)
        package2,r2=self.fixture(2,r['resourceReleaseId'],b'new-code');offer,_=self.stage(package2)
        self.assertNotIn('resources/'+r['resourceReleaseId']+'/bundles/content.bundle',offer['missingFiles'])
        self.store.publish(r2['releaseId'],self.url)
        bundle=self.public/'resources'/r['resourceReleaseId']/'bundles/content.bundle'
        self.assertEqual(bundle.stat().st_ino,(self.private/'blobs'/digest(bundle.read_bytes())).stat().st_ino)
        self.assertEqual({p.name for p in (self.private/'staging'/r2['releaseId']).iterdir()},{'published-envelope.json'})
        self.assertEqual(self.store.state()['currentId'],r2['releaseId'])

    def test_client_proof_gate_and_failed_publish_preserve_current(self):
        package,r=self.fixture(proof=False);self.stage(package)
        with self.assertRaises(ValueError):self.store.publish(r['releaseId'],self.url)
        self.assertFalse((self.public/'current.json').exists())

    def test_public_verification_failure_rolls_back_pointer_and_registry(self):
        p,r=self.fixture();self.stage(p);self.store.publish(r['releaseId'],self.url)
        p2,r2=self.fixture(2);self.stage(p2)
        before=(self.public/'current.json').read_bytes();old=self.store.state()
        with self.assertRaises(Exception):self.store.publish(r2['releaseId'],self.url+'/wrong')
        self.assertEqual((self.public/'current.json').read_bytes(),before)
        self.assertEqual(self.store.state(),old)
        self.assertFalse((self.private/'pending-publication.json').exists())

    def test_read_only_state_reports_pending_publish_then_writer_recovers_it(self):
        p,r=self.fixture();self.stage(p);self.store.publish(r['releaseId'],self.url)
        p2,r2=self.fixture(2);self.stage(p2);state=self.store.state()
        old=(self.public/'current.json').read_bytes();new=(p2/'current.json').read_bytes()
        write_json(self.private/'pending-publication.json',{'transactionId':uuid.uuid4().hex,'releaseId':r2['releaseId'],
            'oldState':state,'oldHash':digest(old),'newHash':digest(new),'oldEnvelope':base64.b64encode(old).decode()})
        atomic(self.public/'current.json',new)
        names=['release_common.py','release_package.py','release_retention.py','release_store.py']
        tool_hash=digest(''.join(n+' '+file_hash(ROOT/'Tools/HotUpdate'/n)+'\n' for n in names).encode())
        request=base64.b64encode(json_bytes({'action':'state','toolchainSha256':tool_hash})).decode()
        result=subprocess.run([sys.executable,str(ROOT/'Tools/HotUpdate/release_store.py'),'--root',str(self.private),'--request-b64',request],capture_output=True,check=True)
        self.assertTrue(json.loads(result.stdout)['pendingRecovery'])
        self.assertEqual((self.public/'current.json').read_bytes(),new)
        self.store.recover_transaction();self.assertEqual((self.public/'current.json').read_bytes(),old)

    def test_monotonic_sequence_and_immutable_publication(self):
        p,r=self.fixture();self.stage(p);self.store.publish(r['releaseId'],self.url)
        with self.assertRaises(ValueError):self.store.publish(r['releaseId'],self.url)
        descriptor=describe(p,self.config,True);changed=copy.deepcopy(r);changed['codeSha256']='a'*64
        descriptor['envelope']=self.sign(changed)
        with self.assertRaises(ValueError):self.store.offer(descriptor)

    def test_interrupted_upload_becomes_failed_and_keeps_current(self):
        p,r=self.fixture();offer=self.store.offer(describe(p,self.config,True))
        with self.assertRaises(ValueError):self.store.receive(r['releaseId'],io.BytesIO(b'partial'),100,'a'*64)
        self.assertEqual(self.store.state()['uploads'][r['releaseId']]['status'],'failed')
        self.assertIsNone(self.store.state()['currentId'])

    def test_archive_traversal_and_links_rejected(self):
        for name,kind in [('../outside',tarfile.REGTYPE),('symlink',tarfile.SYMTYPE)]:
            archive=self.path/(uuid.uuid4().hex+'.tar')
            with tarfile.open(archive,'w') as f:
                entry=tarfile.TarInfo(name);entry.type=kind;entry.linkname='../outside';f.addfile(entry)
            with self.assertRaises(ValueError):extract_archive(archive,self.path/uuid.uuid4().hex)
        self.assertFalse((self.path/'outside').exists())

    def test_archive_copy_corruption_is_retried_without_changing_expected_hash(self):
        from unittest import mock
        archive=self.path/'content.tar';raw=b'authenticated-content';record={'data.bin':{'sha256':digest(raw),'bytes':len(raw)}}
        with tarfile.open(archive,'w') as target:
            member=tarfile.TarInfo('data.bin');member.size=len(raw);target.addfile(member,io.BytesIO(raw))
        original=shutil.copyfileobj;attempts=[]
        def damaged_once(source,target,*args):
            attempts.append(1)
            if len(attempts)==1:target.write(b'x'*len(raw))
            else:original(source,target,*args)
        with mock.patch('release_package.shutil.copyfileobj',side_effect=damaged_once):extract_archive(archive,self.path/'extracted',record)
        self.assertEqual((self.path/'extracted/data.bin').read_bytes(),raw);self.assertEqual(len(attempts),2)

    def test_future_cleanup_preview_and_altered_candidates_are_rejected(self):
        p,r=self.fixture();self.stage(p);self.store.publish(r['releaseId'],self.url)
        plan=self.store.cleanup_plan();plan['createdAtUtc']=timestamp(utcnow()+dt.timedelta(days=1))
        with self.assertRaises(ValueError):self.store.cleanup(plan,self.url)
        plan=self.store.cleanup_plan();plan['removePublications']=[r['releaseId']]
        with self.assertRaises(ValueError):self.store.cleanup(plan,self.url)

    def test_toolchain_mismatch_is_rejected_before_mutation(self):
        request=base64.b64encode(json_bytes({'action':'state','toolchainSha256':'0'*64})).decode()
        before=(self.private/'state.json').read_bytes()
        result=subprocess.run([sys.executable,str(ROOT/'Tools/HotUpdate/release_store.py'),'--root',str(self.private),'--request-b64',request],capture_output=True)
        self.assertNotEqual(result.returncode,0);self.assertEqual((self.private/'state.json').read_bytes(),before)

    def test_unknown_owned_tree_disables_cleanup(self):
        p,r=self.fixture();self.stage(p);self.store.publish(r['releaseId'],self.url)
        (self.public/'resources'/'unknown').mkdir()
        plan=self.store.cleanup_plan();self.assertFalse(plan['canApply'])
        with self.assertRaises(ValueError):self.store.cleanup(plan,self.url)

    def test_stale_cleanup_snapshot_rejected(self):
        p,r=self.fixture();self.stage(p);self.store.publish(r['releaseId'],self.url)
        plan=self.store.cleanup_plan();state=self.store.state();state['publications'][r['releaseId']]['pinned']=True;self.store.save(state)
        with self.assertRaises(ValueError):self.store.cleanup(plan,self.url)

    def test_cleanup_removes_only_retired_versions_and_keeps_shared_resource(self):
        resource='%032x'%777
        for i in range(1,6):
            package,r=self.fixture(i,resource,b'code-'+str(i).encode());self.stage(package);self.store.publish(r['releaseId'],self.url)
        state=self.store.state();now=utcnow()
        for key,value in state['publications'].items():
            sequence=value['release']['sequence']
            value['publishedAtUtc']=timestamp(now-dt.timedelta(days=30-sequence))
            if key!=state['currentId']:value['retiredAtUtc']=timestamp(now-dt.timedelta(days=10))
        self.store.save(state)
        plan=self.store.cleanup_plan();self.assertEqual(plan['removePublications'],['%032x'%1,'%032x'%2])
        self.store.cleanup(plan,self.url)
        self.assertEqual(set(self.store.state()['publications']),{'%032x'%i for i in [3,4,5]})
        self.assertTrue((self.public/'resources'/resource/'bundles/content.bundle').exists())
        self.assertEqual(self.store.cleanup(self.store.cleanup_plan(),self.url)['removedPublications'],[])

    def test_cleanup_crash_journal_resumes_before_next_mutation(self):
        p,r=self.fixture();self.stage(p);self.store.publish(r['releaseId'],self.url)
        original=self.store.remove_owned
        def stop(path):raise OSError('injected cleanup interruption')
        # Create an old failed incoming file, owned by its registry entry.
        state=self.store.state();other='%032x'%99
        state['uploads'][other]={'release':r,'files':{},'status':'failed','updatedAtUtc':timestamp(utcnow()-dt.timedelta(days=2))}
        self.store.save(state);(self.private/'incoming'/(other+'.tar')).write_bytes(b'incomplete')
        self.store.remove_owned=stop
        with self.assertRaises(OSError):self.store.cleanup(self.store.cleanup_plan(),self.url)
        self.assertTrue((self.private/'pending-cleanup.json').exists())
        self.store.remove_owned=original;self.store.recover_transaction()
        self.assertFalse((self.private/'pending-cleanup.json').exists())
        self.assertNotIn(other,self.store.state()['uploads'])

    def test_disk_space_failure_does_not_delete_protected_versions(self):
        from unittest import mock
        p,r=self.fixture()
        with mock.patch('release_store.shutil.disk_usage',return_value=shutil._ntuple_diskusage(10,10,0)):
            with self.assertRaisesRegex(ValueError,'Insufficient disk'):self.store.offer(describe(p,self.config,True))
        self.assertEqual(self.store.state()['publications'],{})

    def test_receive_space_counts_extraction_and_new_blob_copy(self):
        from unittest import mock
        p,r=self.fixture();descriptor=describe(p,self.config,True);offer=self.store.offer(descriptor)
        archive=p.parent/'space.tar';meta=transfer_archive(p,descriptor,offer,archive)
        missing=offer['missingBytes']
        available=meta['bytes']+missing+1048576
        with mock.patch('release_store.shutil.disk_usage',return_value=shutil._ntuple_diskusage(available,0,available)):
            with archive.open('rb') as stream:
                with self.assertRaisesRegex(ValueError,'Insufficient space'):
                    self.store.receive(r['releaseId'],stream,meta['bytes'],meta['sha256'])
        self.assertEqual(self.store.state()['uploads'][r['releaseId']]['status'],'offered')
        self.assertEqual(list((self.private/'incoming').iterdir()),[])

    def test_failed_archive_generation_removes_partial_transport(self):
        p,r=self.fixture();descriptor=describe(p,self.config,True);offer=self.store.offer(descriptor)
        (p/r['codePath']).write_bytes(b'changed after verification')
        archive=p.parent/'failed-transfer.tar'
        with self.assertRaisesRegex(ValueError,'Local transfer file changed'):
            transfer_archive(p,descriptor,offer,archive)
        self.assertFalse(archive.exists())

    def test_cross_process_lock_excludes_another_operator(self):
        with exclusive_lock(self.private/'.publish.lock'):
            with self.assertRaises(OSError):
                with exclusive_lock(self.private/'.publish.lock'):pass

    def powershell(self, name, *arguments):
        result=subprocess.run([PWSH,'-NoProfile','-File',str(ROOT/'Tools/HotUpdate'/name),*map(str,arguments)],capture_output=True,text=True,encoding='utf-8')
        self.assertEqual(result.returncode,0,result.stdout+'\n'+result.stderr)
        return result.stdout

    def test_powershell_upload_publish_and_signed_forward_rollback(self):
        config=self.path/'trusted.json';write_json(config,self.config)
        first,r1=self.fixture();second,r2=self.fixture(2,resource_id=r1['resourceReleaseId'],code=b'next')
        for package,r in [(first,r1),(second,r2)]:
            self.powershell('Upload-HotUpdateRelease.ps1','-ReleaseDirectory',package,'-LocalStoreDirectory',self.private,
                '-Acceptance','-TrustedConfigPath',config)
            self.powershell('Publish-HotUpdateRelease.ps1','-ReleaseId',r['releaseId'],'-LocalStoreDirectory',self.private,'-PublicBaseUrl',self.url)
            self.powershell('Upload-HotUpdateRelease.ps1','-ReleaseDirectory',package,'-LocalStoreDirectory',self.private,
                '-Acceptance','-TrustedConfigPath',config)
        before=self.store.state()['highestSequence']
        self.powershell('Rollback-HotUpdateRelease.ps1','-TargetReleaseId',r1['releaseId'],'-OutputDirectory',self.path/'rollback',
            '-SigningKeyPath',self.base/'test-private.xml','-LocalStoreDirectory',self.private,'-PublicBaseUrl',self.url,'-Publish')
        state=self.store.state();current=state['publications'][state['currentId']]['release']
        self.assertGreater(current['sequence'],before)
        self.assertEqual(current['codeSha256'],r1['codeSha256'])
        self.assertEqual(current['resourceReleaseId'],r1['resourceReleaseId'])

    def test_local_execution_cleanup_keeps_protected_and_unknown_directories(self):
        _,r=self.fixture();config=self.path/'trusted.json';write_json(config,self.config)
        cache=self.path/'code-cache';(cache/'releases'/r['releaseId']).mkdir(parents=True)
        write_json(cache/'releases'/r['releaseId']/'manifest.json',self.sign(r))
        write_json(cache/'state.json',{'activeRelease':r['releaseId'],'readyRelease':r['releaseId']})
        execution=self.path/'executions';execution.mkdir()
        def host(code):
            identity=str(cache.resolve()).upper()+'|'+'f'*32+'|'+code
            path=execution/digest(identity.encode())[:24];path.mkdir();(path/'.host-identity').write_text(identity);(path/'.usage-v1').touch()
            old=(utcnow()-dt.timedelta(days=10)).timestamp();os.utime(path/'.host-identity',(old,old));return path
        kept=host(r['codeSha256']);removed=host('a'*64)
        unknown=execution/'unknown';unknown.mkdir();(unknown/'private.txt').write_text('preserve')
        self.powershell('Clear-LocalHotUpdateExecutions.ps1','-CodeCacheRoot',cache,'-ExecutionRoot',execution,
            '-ReportPath',self.path/'local-cleanup.json','-TrustedConfigPath',config,'-Apply')
        self.assertTrue(kept.exists());self.assertFalse(removed.exists());self.assertTrue((unknown/'private.txt').exists())

    def test_local_execution_live_lease_prevents_deletion(self):
        import ctypes
        _,r=self.fixture();config=self.path/'trusted.json';write_json(config,self.config)
        cache=self.path/'code-cache';(cache/'releases'/r['releaseId']).mkdir(parents=True)
        write_json(cache/'releases'/r['releaseId']/'manifest.json',self.sign(r));write_json(cache/'state.json',{'activeRelease':r['releaseId']})
        execution=self.path/'executions';execution.mkdir();identity=str(cache.resolve()).upper()+'|'+'f'*32+'|'+'a'*64
        host=execution/digest(identity.encode())[:24];host.mkdir();(host/'.host-identity').write_text(identity);lease=host/'.usage-v1';lease.touch()
        old=(utcnow()-dt.timedelta(days=10)).timestamp();os.utime(host/'.host-identity',(old,old))
        create=ctypes.windll.kernel32.CreateFileW;create.argtypes=[ctypes.c_wchar_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p];create.restype=ctypes.c_void_p
        handle=create(str(lease),0x80000000,1,None,3,0,None);self.assertNotEqual(handle,ctypes.c_void_p(-1).value)
        try:self.powershell('Clear-LocalHotUpdateExecutions.ps1','-CodeCacheRoot',cache,'-ExecutionRoot',execution,
            '-ReportPath',self.path/'local-cleanup.json','-TrustedConfigPath',config,'-Apply')
        finally:ctypes.windll.kernel32.CloseHandle(ctypes.c_void_p(handle))
        self.assertTrue(host.exists())

    def test_retirement_grace_references_and_channel_isolation(self):
        now=utcnow();state={'currentId':'5','publications':{},'resources':{},'blobs':{},'uploads':{}}
        for i in range(1,6):
            state['publications'][str(i)]={'status':'published','release':{'target':TARGET,'unityVersion':'v1','bootstrapApiVersion':1,'sequence':i,'resourceReleaseId':'shared','codeSha256':'code'+str(i)},
                'publishedAtUtc':timestamp(now-dt.timedelta(days=100-i)),'retiredAtUtc':timestamp(now-dt.timedelta(days=10)),'pinned':False}
        state['publications']['1']['retiredAtUtc']=timestamp(now-dt.timedelta(hours=1))
        state['publications']['2']['pinned']=True
        state['resources']['shared']={'files':{'bundle':{'sha256':'shared-blob','bytes':9}}};state['blobs']['shared-blob']={'bytes':9}
        state['publications']['old-channel']=copy.deepcopy(state['publications']['1']);state['publications']['old-channel']['release']['unityVersion']='v0'
        plan=plan_retention(state,self.policy,now)
        self.assertEqual(plan['removePublications'],[])
        self.assertIn('shared-blob',plan['keepBlobs'])
        self.assertIn('old-channel',plan['keepPublications'])
        state['publications']['1']['retiredAtUtc']=timestamp(now-dt.timedelta(days=8));state['publications']['2']['pinned']=False
        plan=plan_retention(state,self.policy,now);self.assertEqual(plan['removePublications'],['1','2'])

    def test_abandoned_and_active_upload_protection(self):
        now=utcnow();r={'target':TARGET,'unityVersion':'v','bootstrapApiVersion':1,'resourceReleaseId':'r','codeSha256':'c','sequence':1}
        state={'currentId':None,'publications':{},'resources':{},'blobs':{},'uploads':{},'activeOperationIds':['active']}
        for key,status,age in [('old','failed',48),('new','failed',1),('active','receiving',100)]:
            state['uploads'][key]={'release':r,'files':{},'status':status,'updatedAtUtc':timestamp(now-dt.timedelta(hours=age))}
        plan=plan_retention(state,self.policy,now)
        self.assertEqual(plan['removeUploads'],['old']);self.assertIn('active',plan['keepPublications'])

if __name__=='__main__':
    unittest.main(verbosity=2)
