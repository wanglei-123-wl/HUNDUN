"""Synchronize a source project into a dedicated isolated Unity build project."""
import argparse
from release_common import *

def synchronize(source, target, snapshot=None):
    source, target = plain(source), plain(target)
    require(source != target and not target.is_relative_to(source), 'Build project cannot be the source project or its child.')
    require(str(target).replace('\\','/').lower().startswith('c:/qidaobuild/'), 'Use a dedicated C:/QiDaoBuild project.')
    require('keys' not in [part.lower() for part in target.parts], 'Signing key directories cannot be build projects.')
    require((source/'Assets').is_dir() and (source/'ProjectSettings').is_dir(), 'Invalid Unity source project.')
    verified = None
    if snapshot:
        sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'ResourcePacking'))
        from resource_workflow import verify_snapshot
        verified = verify_snapshot(source,snapshot)['files']
    desired=[]; extra_bytes=0
    for folder in ('Assets','Packages','ProjectSettings'):
        for file in (source/folder).rglob('*'):
            plain(file)
            if file.is_file():
                copy=target/file.relative_to(source)
                expected=verified[file.relative_to(source).as_posix()]['sha256'] if verified is not None else file_hash(file)
                if not copy.exists() or expected!=file_hash(copy):
                    desired.append((file,copy));extra_bytes+=file.stat().st_size
    target.mkdir(parents=True,exist_ok=True)
    require(shutil.disk_usage(target).free >= extra_bytes+268435456, 'Insufficient disk space for isolated source synchronization.')
    for file,destination in desired:
        expected=verified[file.relative_to(source).as_posix()]['sha256'] if verified is not None else file_hash(file)
        copy_verified(file,destination,expected,file.stat().st_size)
    # Do not compile stale scripts/asmdefs or acceptance fixture assets left over
    # from a previous isolated project. Disable extras; never touch source assets.
    source_paths={p.relative_to(source).as_posix() for folder in ('Assets','Packages','ProjectSettings') for p in (source/folder).rglob('*') if p.is_file()}
    disabled=[]
    for folder in ('Assets','Packages','ProjectSettings'):
        for file in list((target/folder).rglob('*')):
            if file.is_file() and file.relative_to(target).as_posix() not in source_paths and not file.name.endswith('.disabled'):
                plain(file);file.rename(file.with_name(file.name+'.disabled'));disabled.append(str(file.relative_to(target)))
    return {'status':'synchronized','copiedFiles':len(desired),'disabledExtras':disabled,'sourceProject':str(source),'buildProject':str(target)}

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',type=Path,required=True);p.add_argument('--target',type=Path,required=True);p.add_argument('--snapshot',type=Path)
    a=p.parse_args();print(json.dumps(synchronize(a.source,a.target,a.snapshot),ensure_ascii=False))
