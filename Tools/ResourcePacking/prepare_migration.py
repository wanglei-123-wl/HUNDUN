"""Generate a reviewable packing plan; does not modify Unity assets."""
import argparse,collections,csv,hashlib,json,pathlib,re
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):
    with p.open('rb') as s:return hashlib.file_digest(s,'sha256').hexdigest()
def write(p,d):p.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
def build(source,stage1,out,preflight,metadata_only=False):
    gate=read(preflight);build_hold=gate['status']!='succeeded'
    before=read(stage1/'source-inputs-before.json')['files'];graph=read(stage1/'native-dependency-graph.json')
    assets={a['path']:a for a in graph['assets']};allrows=list(csv.DictReader((stage1/'asset-ownership-and-migration.csv').open(encoding='utf-8-sig',newline='')))
    proof_path=preflight.parent/'failed-file-followup.json'
    if build_hold:
        assert metadata_only,'Source read preflight did not pass.'
        proof={r['path']:r for r in read(proof_path)['findings']}
        for failure in gate['failed']:
            path=failure['path'];r=proof[path];expected=failure['expected']
            assert assets[path]['assetType']=='UnityEngine.Texture2D',path
            assert failure['hashes'].get('direct')==expected and r['gitExpected']==expected and r['clone']==expected,path
            assert all(sample['direct']==expected for sample in r['followupReads']),path
            assert all(all(h==expected for h in sample.values()) for sample in r['followupReads'][-2:]),path
    records={r['path']:r for r in allrows};candidates=list(csv.DictReader((stage1/'migration-root-candidates.csv').open(encoding='utf-8-sig',newline='')))
    old=read(stage1/'groups-and-existing-addresses.json');oldentries={a['path']:a for a in old['entries']}
    modes={g['name']:g['packingMode'] for g in old['groups']};modes['RemoteGameContent']='PackSeparately'
    planned={};groups={};skipped=[]
    def ascii_group(prefix,key):return prefix+'_'+hashlib.sha256(key.encode()).hexdigest()[:12]
    def select(path,target,reason):
        assert path in assets and path in before and path not in planned,path
        assert re.fullmatch(r'[A-Za-z0-9_]+',target),target
        original=oldentries.get(path);guid=assets[path]['guid'];assert re.fullmatch('[a-f0-9]{32}',guid)
        planned[path]={'path':path,'guid':guid,'targetGroup':target,'reason':reason,
            'address':original['address'] if original else 'rpo/'+guid,'existingGroup':original['group'] if original else None,
            'sourceSha256':before[path]['sha256'],'metaSha256':before[path+'.meta']['sha256']}
        groups.setdefault(target,{'name':target,'mode':'PackTogether','description':reason});modes[target]='PackTogether'
    for row in candidates:
        path=row['path'];category=row['proposedCategory'];family=row['proposedGroupOrFamily'];suffix=pathlib.Path(path).suffix.lower()
        if category=='怪物轻量定义':select(path,'RPO_MonsterDefinitions','怪物目录及轻量定义')
        elif category=='怪物预制体':select(path,'RPO_MonsterPrefab_'+family.split('/')[-1],'按怪物独立的预制体')
        elif category=='物品轻量定义':select(path,'RPO_ItemDefinitions','物品表及隐式轻量物品定义，图片外置')
        elif category=='物品图标':select(path,ascii_group('RPO_ItemIcons',family),'按稳定资源分类的物品图标')
        elif category=='加载背景':select(path,'RPO_LoadingBackground_'+assets[path]['guid'][:12],'独立加载背景')
        elif category=='界面美术候选':select(path,ascii_group('RPO_UIArt',family),'按实际界面消费者/大图划分的界面美术')
        elif category=='怪物美术':
            if suffix in ['.fbx','.obj'] and '@' in pathlib.Path(path).stem:
                skipped.append({'path':path,'reason':'Animation FBX remains an implicit dependency of its animation controller; avoid promoting its unused imported objects.'});continue
            animation=suffix in ['.controller','.overridecontroller','.anim']
            prefix='RPO_MonsterAnimation_' if animation else 'RPO_MonsterArt_'
            select(path,prefix+family.split('/')[-1],'怪物动画控制/动画资源' if animation else '稳定怪物模型、材质及专用子素材')
    # A metadata-only loot prefab points back to the item table. Keep this
    # small cycle within the definitions bundle, without editing prefab fields.
    table='Assets/Data/Items/ItemDefinitionTable.asset'
    for path in oldentries:
        if path in planned or not path.endswith('.prefab'):continue
        deps=assets[path]['directDependencies']
        if table in deps and all(d==table or pathlib.Path(d).suffix=='.cs' for d in deps):
            select(path,'RPO_ItemDefinitions','与物品表互相引用的轻量掉落预制体，同包消除依赖回环')
    # Preserve every existing root and its address, including map/font/shared roots.
    for path,entry in oldentries.items():
        if path not in planned:
            planned[path]={'path':path,'guid':assets[path]['guid'],'targetGroup':entry['group'],'reason':'保留原有显式根',
                'address':entry['address'],'existingGroup':entry['group'],'sourceSha256':before[path]['sha256'],'metaSha256':before[path+'.meta']['sha256']}
    non_content={'.cs','.asmdef','.asmref','.dll','.pdb','.zip','.unitypackage'}
    def eligible(path):
        return path in assets and pathlib.Path(path).suffix.lower() not in non_content and '/Editor/' not in path and records[path]['proposedCategory'] not in ['固定客户端','代码或编辑器','尚未证明在用或未用']
    def bucket(path):
        root=planned[path];return root['targetGroup']+('/'+root['guid'] if modes[root['targetGroup']]=='PackSeparately' else '/together')
    def ownership():
        owners=collections.defaultdict(set)
        for path in planned:
            own=bucket(path);pending=[path];seen=set()
            while pending:
                node=pending.pop()
                if node in seen:continue
                seen.add(node)
                if node!=path and node in planned and bucket(node)!=own:continue
                if eligible(node):owners[node].add(own)
                pending.extend(assets.get(node,{}).get('directDependencies',[]))
        return owners
    promotions=[]
    for iteration in range(len(assets)+1):
        owners=ownership();shared={path for path,buckets in owners.items() if len(buckets)>1 and path not in planned}
        if not shared:break
        children={child for parent in shared for child in assets[parent]['directDependencies'] if child!=parent and child in shared}
        frontier=sorted(shared-children) or [min(shared)]
        for path in frontier:
            parent=pathlib.PurePosixPath(path).parent.as_posix();suffix=pathlib.Path(path).suffix.lower()
            family='RPO_SharedShaders' if suffix in ['.shader','.shadergraph','.shadersubgraph'] else 'RPO_SharedArt'
            group=ascii_group(family,parent);select(path,group,'多个实际打包边界共用的父资源，建立唯一归属')
            promotions.append({'path':path,'targetGroup':group,'previousPredictedOwners':sorted(owners[path])})
    else:raise ValueError('Shared ownership did not converge.')
    assert not any(len(x)>1 for x in ownership().values()),'Predicted duplicate ownership remains.'
    assert len({x['address'] for x in planned.values()})==len(planned),'Duplicate addresses.'
    source_files={name:value for name,value in before.items() if name.startswith('Assets/AddressableAssetsData/')}
    for name,value in source_files.items():assert sha(source/name)==value['sha256'],'Addressables input changed: '+name
    result={'schemaVersion':1,'status':'prepared-metadata-only-build-held' if build_hold else 'prepared','sourceRoot':str(source),'stage1Root':str(stage1),'preflightReport':str(preflight),
        'preflightSha256':sha(preflight),'existingAddresses':old['entries'],'sourceAddressablesFiles':source_files,
        'metadataOnlyMigration':True,'resourceBuildReadinessVerified':not build_hold,
        'readIntegrityFollowupSha256':sha(proof_path) if build_hold else None,
        'settingsFile':'Assets/AddressableAssetsData/AddressableAssetSettings.asset',
        'existingSchemaOverrides':[{'group':'RemoteGameContent','mode':'PackSeparately'}],
        'newGroups':list(groups.values()),'entries':sorted(planned.values(),key=lambda r:r['path']),
        'sharedPromotions':promotions,'skippedExplicitCandidates':skipped,
        'sourceAssetsToKeepIdentical':sorted({r['path'] for r in planned.values()}|{r['path']+'.meta' for r in planned.values()}),
        'predictedSharedOwnershipConflicts':0,'resourceBuildPerformed':False,'playerBuildPerformed':False,'published':False,
        'notes':['Group identities are created once and saved, never regenerated during builds.','All old addresses and labels must survive.',
            'Scene objects, serialized gameplay fields and image import settings are not edited.',
            'Implicit animation FBX assets remain behind controller boundaries.',
            'Native build size, runtime loading and incremental update propagation remain later-stage acceptance requirements.']}
    out.mkdir(parents=True,exist_ok=True);write(out/'migration-plan.json',result)
    with (out/'迁移对应表.csv').open('w',encoding='utf-8-sig',newline='') as stream:
        writer=csv.DictWriter(stream,fieldnames=['path','guid','existingGroup','targetGroup','address','reason']);writer.writeheader()
        for r in result['entries']:writer.writerow({key:r[key] for key in writer.fieldnames})
    print(json.dumps({'status':result['status'],'entries':len(planned),'newGroups':len(groups),'sharedPromotions':len(promotions),'implicitAnimationFbxKept':len(skipped)},ensure_ascii=True))
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--stage1',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--preflight',type=pathlib.Path,required=True);p.add_argument('--metadata-only-with-build-hold',action='store_true');a=p.parse_args()
    build(a.source.resolve(),a.stage1.resolve(),a.output.resolve(),a.preflight.resolve(),a.metadata_only_with_build_hold)
