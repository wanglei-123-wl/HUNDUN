"""Local immutable baseline, source verification and cache-key delta workflow."""
from __future__ import annotations
import argparse,datetime as dt,json,pathlib,sys
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'HotUpdate'))
from release_common import *
from source_integrity import sha,direct_sha
PROTOCOL=1
def inputs(root):
    return {p.relative_to(root).as_posix():p for folder in ('Assets','Packages','ProjectSettings') for p in tree_files(root/folder)}
def resource_input(name):
    return '/Editor/' not in name and pathlib.Path(name).suffix.lower() not in ('.cs','.asmdef','.asmref','.dll','.pdb')
def fingerprint(files):
    return digest(json_bytes({name:{'sha256':item['sha256'],'bytes':item['bytes']} for name,item in files.items() if resource_input(name)}))
def snapshot(source,out):
    source=plain(source);out=plain(out);require(not out.exists(),'Source verification report is immutable.')
    require(not any(out.is_relative_to(source/folder) for folder in ('Assets','Packages','ProjectSettings')),'Do not write reports into source inputs.')
    files={};failures=[]
    for name,path in inputs(source).items():
        before=path.stat();hashes=[sha(path),sha(path)]
        if before.st_size>=262144:hashes.append(direct_sha(path))
        after=path.stat()
        if len(set(hashes))!=1 or (before.st_size,before.st_mtime_ns)!=(after.st_size,after.st_mtime_ns):
            failures.append({'path':name,'hashes':hashes,'reason':'Inconsistent source read or concurrent modification.'})
        files[name]={'sha256':hashes[0],'bytes':before.st_size}
    report={'kind':'QiDaoResourceInputVerification','schemaVersion':PROTOCOL,'status':'succeeded' if not failures else 'failed',
        'sourceRoot':str(source),'verifiedAtUtc':timestamp(),'files':files,'resourceFingerprint':fingerprint(files),'failures':failures,
        'expectedHashesChanged':False,'filesModified':False}
    write_json(out,report);require(not failures,'Source reads were inconsistent; resource building stopped. See '+str(out))
    return {'status':'succeeded','files':len(files),'resourceFingerprint':report['resourceFingerprint'],'report':str(out)}
def verify_snapshot(source,path,require_fresh=True):
    source=plain(source);report=read_json(path)
    require(report.get('kind')=='QiDaoResourceInputVerification' and report.get('schemaVersion')==PROTOCOL
        and report.get('status')=='succeeded' and not report.get('failures') and plain(report['sourceRoot'])==source,'Invalid source verification report.')
    if require_fresh:
        age=utcnow()-parse_time(report['verifiedAtUtc']);require(dt.timedelta(0)<=age<=dt.timedelta(hours=1),'Source verification expired; verify inputs again.')
    actual=inputs(source);require(set(actual)==set(report['files']),'Source input inventory changed.')
    for name,path in actual.items():
        item=report['files'][name];require(path.stat().st_size==item['bytes'] and sha(path)==item['sha256'],'Source input changed or could not be verified: '+name)
    require(fingerprint(report['files'])==report['resourceFingerprint'],'Resource fingerprint differs.')
    return report
def verify_report(report_path):
    report_path=plain(report_path);r=read_json(report_path)
    require(r.get('status')=='succeeded' and r.get('allBundlesNativeCrcVerified') and r.get('assetDependencyOrderVerified'),'Native resource proof missing.')
    root=plain(pathlib.Path(r['catalogPath']).parent);manifest=read_json(root/'resource-manifest.json')
    require(file_hash(root/'resource-manifest.json')==r['resourceManifestSha256'] and file_hash(root/'catalog_0.1.json')==r['resourceCatalogSha256'],'Resource controls differ.')
    require(manifest['resourceReleaseId']==r['resourceReleaseId'] and manifest['scriptTypeHash']==r['scriptTypeHash'],'Resource binding differs.')
    for b in manifest['bundles']:
        p=inside(root,b['relativePath']);require(p.stat().st_size==b['bytes'] and file_hash(p)==b['sha256'],'Resource bundle differs: '+str(p))
    return r,manifest
def core_report(report):return {k:v for k,v in report.items() if k not in ('workflowPath','workflowSha256')}
def workflow(report_path,seen=None):
    report_path=plain(report_path);seen=seen or set();require(str(report_path) not in seen,'Workflow lineage cycle.');seen.add(str(report_path))
    r,m=verify_report(report_path)
    require(r.get('workflowProtocol')==PROTOCOL and r.get('workflowPath') and r.get('workflowSha256'),'Resource report has no incremental workflow provenance; create a new baseline.')
    wp=plain(r['workflowPath']);require(file_hash(wp)==r['workflowSha256'],'Workflow descriptor changed.');w=read_json(wp)
    require(w['resourceReportCoreSha256']==digest(json_bytes(core_report(r))) and w['resourceReleaseId']==r['resourceReleaseId'],'Workflow/report identity differs.')
    for key in ('sourceSnapshot','layoutContract'):
        item=w[key];require(file_hash(item['path'])==item['sha256'],'Archived workflow control differs: '+key)
    ss=read_json(w['sourceSnapshot']['path']);require(ss['resourceFingerprint']==w['resourceFingerprint'] and ss['status']=='succeeded','Invalid archived source verification.')
    if w['mode']=='baseline':
        require(w['baselineResourceReleaseId']==r['resourceReleaseId'],'Baseline identity differs.')
        item=w['contentState'];require(file_hash(item['path'])==item['sha256'],'Original baseline content state changed.')
    else:
        require(w['mode']=='update','Invalid resource mode.')
        base,_,anchor=workflow(w['baselineResourceReport'],seen)
        require(anchor['mode']=='baseline' and base['resourceReleaseId']==w['baselineResourceReleaseId'],'Update must use the original full baseline.')
        require(w['contentState']==anchor['contentState'] and w['nativeLoadRoot']==anchor['nativeLoadRoot'],'Update changed its original baseline state or native load root.')
    return r,m,w
def delta(old_manifest,new_manifest):
    previous=old_manifest['bundles'] if old_manifest else []
    by_key={(b['bundleName'],b['cacheHash']):b for b in previous};new_keys=set();reused=[];download=[];identity=[]
    old_sha={b['sha256'] for b in previous}
    for b in new_manifest['bundles']:
        key=(b['bundleName'],b['cacheHash']);require(key not in new_keys,'Duplicate native cache identity.');new_keys.add(key)
        if key in by_key:
            prior=by_key[key];require(prior['sha256']==b['sha256'] and prior['bytes']==b['bytes'],'Same cache identity was rebound to different bytes.')
            reused.append(b['relativePath'])
        else:
            download.append(b)
            if b['sha256'] in old_sha:identity.append(b['relativePath'])
    return {'schemaVersion':1,'newOrChangedBundleCount':len(download),'estimatedDownloadBytesWithCompletePreviousCache':sum(b['bytes'] for b in download),
        'reusedBundleCount':len(reused),'reusedBundles':reused,'downloadBundles':download,'sameBytesButNewCacheIdentity':identity,
        'removedCacheIdentities':[{'bundleName':name,'cacheHash':h} for name,h in sorted(set(by_key)-new_keys)],
        'scope':'Bundle payload estimate with a complete valid previous cache; catalog/control requests and cache repairs are additional. Not measured player traffic.'}
def choose(mode,snapshot_path,previous):
    s=read_json(snapshot_path);require(s['status']=='succeeded','Source verification failed.')
    if mode=='baseline':return {'mode':'baseline'}
    require(previous,'Supply the previous verified resource report, or explicitly create a baseline.')
    _,_,w=workflow(previous)
    same=s['resourceFingerprint']==w['resourceFingerprint']
    if mode=='reuse':require(same,'Resource inputs changed; old resources cannot be reused.')
    return {'mode':('reuse' if same else 'update') if mode=='auto' else mode,'baselineResourceReport':str(previous) if w['mode']=='baseline' else w['baselineResourceReport']}
def request(source,source_snapshot,mode,output,rid,previous=None,acceptance=False):
    source=plain(source);ss=verify_snapshot(source,source_snapshot);require(hex_id(rid),'Invalid resource ID.')
    output=plain(output);require(not output.exists(),'Resource output is immutable.')
    chosen=choose(mode,source_snapshot,previous) if mode!='baseline' else {'mode':'baseline'}
    require(chosen['mode']!='reuse','No resource build is needed; reuse the previous verified report.')
    saved=output/'source-inputs.json'
    req={'outputDirectory':str(output),'resourceReleaseId':rid,'resourceBuildMode':chosen['mode'],'sourceSnapshotPath':str(saved),
        'sourceSnapshotSha256':file_hash(source_snapshot),'acceptanceOnly':acceptance,'workflowProtocol':PROTOCOL}
    if chosen['mode']=='update':
        base,_,anchor=workflow(chosen['baselineResourceReport']);pr,_,pw=workflow(previous)
        require(pw['baselineResourceReleaseId']==anchor['baselineResourceReleaseId'],'Previous release belongs to a different baseline.')
        req.update(baselineResourceReleaseId=base['resourceReleaseId'],baselineContentState=anchor['contentState']['path'],
            baselineContentStateSha256=anchor['contentState']['sha256'],baselineLayoutContract=anchor['layoutContract']['path'],
            baselineLayoutContractSha256=anchor['layoutContract']['sha256'],nativeLoadRoot=anchor['nativeLoadRoot'],
            baselineResourceReport=chosen['baselineResourceReport'],fallbackBundleDirectories=list(dict.fromkeys([str(pathlib.Path(pr['catalogPath']).parent),str(pathlib.Path(base['catalogPath']).parent)])))
    output.mkdir(parents=True)
    copy_verified(source_snapshot,saved,req['sourceSnapshotSha256'],pathlib.Path(source_snapshot).stat().st_size)
    return req
def finalize(report_path,previous=None):
    report_path=plain(report_path);r,m=verify_report(report_path);require(r.get('workflowProtocol')==PROTOCOL,'Native workflow protocol missing.')
    require(not r.get('workflowPath'),'Workflow already finalized.');directory=report_path.parent/'Workflow';require(not directory.exists(),'Workflow output already exists.');directory.mkdir()
    require(file_hash(r['sourceSnapshotPath'])==r['sourceSnapshotSha256'],'Source snapshot differs.')
    ss=read_json(r['sourceSnapshotPath']);require(ss['status']=='succeeded','Source snapshot failed.')
    mode=r['resourceBuildMode'];anchor_path=report_path if mode=='baseline' else pathlib.Path(r['baselineResourceReport'])
    w={'schemaVersion':PROTOCOL,'mode':mode,'resourceReleaseId':r['resourceReleaseId'],'baselineResourceReleaseId':r['resourceReleaseId'] if mode=='baseline' else r['baselineResourceReleaseId'],
        'baselineResourceReport':str(anchor_path),'nativeLoadRoot':r['nativeLoadRoot'],'resourceFingerprint':ss['resourceFingerprint'],
        'resourceReportCoreSha256':digest(json_bytes(core_report(r))),
        'sourceSnapshot':{'path':r['sourceSnapshotPath'],'sha256':r['sourceSnapshotSha256']},
        'layoutContract':{'path':r['layoutContractPath'],'sha256':file_hash(r['layoutContractPath'])}}
    if mode=='baseline':
        p=plain(r['contentStatePath']);target=directory/'original-addressables-content-state.bin';copy_verified(p,target,file_hash(p),p.stat().st_size)
        w['contentState']={'path':str(target),'sha256':file_hash(target)}
    else:
        br,_,bw=workflow(anchor_path);require(bw['mode']=='baseline' and br['resourceReleaseId']==r['baselineResourceReleaseId'],'Wrong original baseline.')
        w['contentState']=bw['contentState']
    old=workflow(previous)[1] if previous else None
    result=delta(old,m);write_json(report_path.parent/'resource-download-delta.json',result)
    wp=directory/'workflow.json';write_json(wp,w);r.update(workflowPath=str(wp),workflowSha256=file_hash(wp));write_json(report_path,r)
    workflow(report_path)
    return {'status':'finalized','mode':mode,'baselineResourceReleaseId':w['baselineResourceReleaseId'],'delta':result}
def main():
    p=argparse.ArgumentParser();p.add_argument('action',choices=['snapshot','verify-snapshot','choose','request','finalize','verify-workflow','delta'])
    p.add_argument('--source',type=Path);p.add_argument('--snapshot',type=Path);p.add_argument('--output',type=Path);p.add_argument('--mode',choices=['auto','baseline','update','reuse']);p.add_argument('--previous',type=Path);p.add_argument('--report',type=Path);p.add_argument('--resource-id');p.add_argument('--acceptance',action='store_true');a=p.parse_args()
    if a.action=='snapshot':value=snapshot(a.source,a.output)
    elif a.action=='verify-snapshot':r=verify_snapshot(a.source,a.snapshot);value={'status':'succeeded','resourceFingerprint':r['resourceFingerprint']}
    elif a.action=='choose':value=choose(a.mode,a.snapshot,a.previous)
    elif a.action=='request':value=request(a.source,a.snapshot,a.mode,a.output,a.resource_id,a.previous,a.acceptance)
    elif a.action=='finalize':value=finalize(a.report,a.previous)
    elif a.action=='verify-workflow':r,m,w=workflow(a.report);value={'status':'succeeded','mode':w['mode'],'baselineResourceReleaseId':w['baselineResourceReleaseId']}
    else:value=delta(workflow(a.previous)[1],verify_report(a.report)[1])
    print(json.dumps(value,ensure_ascii=False))
if __name__=='__main__':main()
