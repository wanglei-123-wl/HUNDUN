"""Commit only verified Addressables metadata from the isolated migration."""
import argparse,datetime,json,pathlib,sys
def main(source,clone,artifact):
    sys.path.insert(0,str(source/'Tools/HotUpdate'))
    from release_common import read_json,file_hash,copy_verified,plain,inside,write_json,exclusive_lock
    plan=read_json(artifact/'migration-plan.json');original=plan['sourceAddressablesFiles']
    assert read_json(artifact/'native-migration.json')['status']=='succeeded'
    assert read_json(artifact/'native-idempotence.json')['status']=='succeeded'
    first=read_json(artifact/'isolated-first-application.json');current={}
    for p in (clone/'Assets/AddressableAssetsData').rglob('*'):
        if p.is_file() and p.suffix in ['.asset','.meta']:current[p.relative_to(clone).as_posix()]=file_hash(p)
    assert first==current,'A repeated application changed metadata/identities.'
    write_json(artifact/'idempotence-verification.json',{'status':'succeeded','metadataFileCount':len(current),'changed':[]})
    def allowed(name):
        return name in ['Assets/AddressableAssetsData/AddressableAssetSettings.asset','Assets/AddressableAssetsData/AddressableAssetSettings.asset.meta'] or name.startswith('Assets/AddressableAssetsData/AssetGroups/') and pathlib.Path(name).suffix in ['.asset','.meta']
    candidates={name for name in original if allowed(name)}
    candidates.update(name for name in current if allowed(name) and pathlib.Path(name).name.startswith('RPO_'))
    changes=[]
    for name in sorted(candidates):
        path=inside(clone,name);assert path.is_file(),name
        new=file_hash(path);old=original.get(name)
        if old and new==old['sha256']:continue
        if not old:assert pathlib.Path(name).name.startswith('RPO_'),name
        changes.append({'path':name,'oldSha256':old['sha256'] if old else None,'newSha256':new,'bytes':path.stat().st_size})
    assert changes and all(allowed(c['path']) for c in changes)
    # Check all source Addressables preimages before any writes.
    for name,record in original.items():assert file_hash(inside(source,name))==record['sha256'],'Source configuration changed: '+name
    for change in changes:
        target=inside(source,change['path'])
        assert change['oldSha256'] is not None or not target.exists(),'New target already exists.'
        if change['oldSha256']:
            backup=inside(artifact,'SourceBackup/'+change['path']);copy_verified(target,backup,change['oldSha256'],target.stat().st_size)
    stats={p.relative_to(source).as_posix():(p.stat().st_size,p.stat().st_mtime_ns) for folder in ['Assets','Packages','ProjectSettings'] for p in (source/folder).rglob('*') if p.is_file()}
    write_json(artifact/'source-commit-plan.json',{'changes':changes,'onlyAddressablesMetadata':True,'resourceBuildReadinessVerified':False})
    committed=[]
    with exclusive_lock(artifact/'.source-commit.lock'):
        try:
            # Write referenced groups before the root settings registry.
            for change in sorted(changes,key=lambda c:c['path']=='Assets/AddressableAssetsData/AddressableAssetSettings.asset'):
                copy_verified(inside(clone,change['path']),inside(source,change['path']),change['newSha256'],change['bytes']);committed.append(change)
            for change in changes:assert file_hash(inside(source,change['path']))==change['newSha256']
        except BaseException:
            for change in reversed(committed):
                target=inside(source,change['path'])
                if change['oldSha256']:
                    backup=inside(artifact,'SourceBackup/'+change['path']);copy_verified(backup,target,change['oldSha256'],backup.stat().st_size)
                elif target.exists():target.unlink()
            raise
    after={p.relative_to(source).as_posix():(p.stat().st_size,p.stat().st_mtime_ns) for folder in ['Assets','Packages','ProjectSettings'] for p in (source/folder).rglob('*') if p.is_file()}
    unexpected=[name for name in stats if not allowed(name) and stats[name]!=after.get(name)]
    assert not unexpected,unexpected
    assert all(allowed(name) for name in after.keys()-stats.keys())
    result={'status':'committed','changedMetadataFiles':len(changes),'newMetadataFiles':sum(c['oldSha256'] is None for c in changes),
        'unchangedOtherSourceInputFiles':sum(not allowed(p) for p in stats),'unexpectedSourceStatChanges':unexpected,
        'originalImageModelSceneAndImporterFilesWritten':False,'sourceRuntimeCodeModified':False,'resourceBuildPerformed':False,'playerBuildPerformed':False,
        'cloudOperationsPerformed':False,'resourceBuildReadinessVerified':False,'readIntegrityRootCauseResolved':False}
    write_json(artifact/'source-commit-result.json',result);print(json.dumps(result,ensure_ascii=True))
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--clone',type=pathlib.Path,required=True);p.add_argument('--artifact',type=pathlib.Path,required=True);a=p.parse_args()
    main(a.source.resolve(),a.clone.resolve(),a.artifact.resolve())
