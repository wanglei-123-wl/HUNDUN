"""Build and exercise tiny native content updates, without game source or cloud writes."""
import argparse, base64, hashlib, http.server, json, pathlib, re, subprocess, sys, threading, urllib.parse, uuid
HERE=pathlib.Path(__file__).resolve().parent
sys.path[:0]=[str(HERE),str(HERE.parent/'HotUpdate')]
import resource_workflow as workflow
from release_build import synchronize
from release_common import copy_verified, read_json, write_json, require, file_hash
from release_package import build_package

def hidden(command,timeout):
    startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
    process=subprocess.Popen([str(x) for x in command],startupinfo=startup)
    try:code=process.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        subprocess.run(['taskkill.exe','/PID',str(process.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,check=False)
        process.wait();raise
    require(code==0,'Owned fixture process failed: '+str(command))

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=pathlib.Path,required=True)
    p.add_argument('--project',type=pathlib.Path,default=pathlib.Path('C:/QiDaoBuild/HotUpdateMigration-Stage2'))
    p.add_argument('--base',type=pathlib.Path,required=True)
    p.add_argument('--unity',type=pathlib.Path,default=pathlib.Path('C:/Program Files/Unity 2022.3.62f1/Editor/Unity.exe'))
    p.add_argument('--key',type=pathlib.Path,default=pathlib.Path('C:/QiDaoBuild/Keys/QiDaoHotUpdate-RSA.xml'))
    a=p.parse_args();out=a.output.resolve();require(not out.exists(),'Fixture output must be new.');out.mkdir(parents=True)
    project=a.project.resolve();require(project==pathlib.Path('C:/QiDaoBuild/HotUpdateMigration-Stage2'),'Use only the dedicated fixture project.')
    run=uuid.uuid4().hex;author=pathlib.Path('C:/QiDaoBuild/RPOStage3Authoring')/run;author.mkdir(parents=True)
    for name,path in workflow.inputs(project).items():copy_verified(path,author/name,file_hash(path),path.stat().st_size)
    write_json(out/'ownership.json',{'authoringFixture':str(author),'sourceGameModified':False,'cloudContacted':False})
    data_asset=author/'Assets/HotUpdateAcceptance/HotCodeProbeData.asset'
    original=data_asset.read_text(encoding='utf-8');reports=[];code_dirs=[];builds=[]
    def unity(method,request,log):
        hidden([a.unity,'-batchmode','-nographics','-quit','-projectPath',project,'-executeMethod',method,
                '-hotupdate-request',request,'-logFile',log],180)
    for index,(name,marker) in enumerate([('baseline',101),('changed',102),('unchanged',102)]):
        data_asset.write_text(re.sub(r'(?m)^  marker: \d+$','  marker: '+str(marker),original),encoding='utf-8')
        snapshot=out/(name+'-inputs.json');workflow.snapshot(author,snapshot)
        write_json(out/(name+'-sync.json'),synchronize(author,project,snapshot))
        previous=reports[-1] if reports else None
        auto=workflow.choose('auto',snapshot,previous) if previous else {'mode':'baseline'}
        require(auto['mode']==('baseline' if index==0 else 'update' if index==1 else 'reuse'),'Wrong automatic resource decision.')
        directory=out/name;request=workflow.request(author,snapshot,'baseline' if index==0 else 'update',directory,uuid.uuid4().hex,previous,True)
        request_path=out/(name+'-request.json');write_json(request_path,request)
        unity('HotUpdateResourceBuild.BuildFromRequest',request_path,out/(name+'-build.log'))
        report=directory/'resource-build-report.json';result=workflow.finalize(report,previous);reports.append(report)
        builds.append({'name':name,'autoMode':auto['mode'],'report':str(report),'result':result})
        require(result['delta']['newOrChangedBundleCount']==(2 if index==0 else 1 if index==1 else 0),'Unexpected cache-key delta.')
        if index<2:
            code=out/(name+'-code');code_request=out/(name+'-code-request.json')
            write_json(code_request,{'outputDirectory':str(code),'releaseId':uuid.uuid4().hex,'sequence':index+1,
                'probeVariant':'A','signingKeyPath':str(a.key),'resourceBuildReport':str(report)})
            unity('HotUpdateCodeBuild.BuildCodeRelease',code_request,out/(name+'-code-build.log'));code_dirs.append(code)
        print('FIXTURE_NATIVE_BUILD_OK '+name,flush=True)
    baseline=workflow.workflow(reports[0])[2]
    for report in reports[1:]:
        _,_,w=workflow.workflow(report);require(w['contentState']==baseline['contentState'],'Original full content state was replaced.')
    config=read_json(author/'Assets/Resources/Startup/HotUpdateBootstrap.json')
    packages=[]
    for name,code,report in zip(('baseline','changed'),code_dirs,reports):
        package=out/(name+'-package');build_package(code,report,package,config,acceptance=True)
        require(not any(p.suffix=='.bin' or p.name in ('workflow.json','source-inputs.json','resource-layout-contract.json') for p in package.rglob('*')),'Local workflow data leaked into public package.')
        packages.append(str(package))
    roots={};state={'current':b'','requests':[]}
    for code in code_dirs:
        for release in (code/'releases').iterdir():roots['/releases/'+release.name+'/']=release
    for report in reports:
        r=read_json(report);roots['/resources/'+r['resourceReleaseId']+'/']=pathlib.Path(r['catalogPath']).parent
    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self,*_):pass
        def do_GET(self):
            path=urllib.parse.urlsplit(self.path).path;data=state['current'] if path=='/current.json' else None
            if data is None:
                for prefix,root in roots.items():
                    if path.startswith(prefix):
                        file=(root/path[len(prefix):]).resolve()
                        if file.is_relative_to(root.resolve()) and file.is_file():data=file.read_bytes()
                        break
            if data is None:self.send_error(404);return
            state['requests'].append({'path':path,'bytes':len(data)});self.send_response(200)
            self.send_header('Content-Length',str(len(data)));self.end_headers()
            try:self.wfile.write(data)
            except ConnectionError:pass
    server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler);threading.Thread(target=server.serve_forever,daemon=True).start()
    exe=a.base.resolve();base_hashes={p.relative_to(exe.parent).as_posix():file_hash(p) for p in exe.parent.rglob('*') if p.is_file()}
    cache=pathlib.Path('C:/QiDaoBuild/RPOStage3Caches')/run;cases=[]
    try:
        for index,(name,marker) in enumerate([('cold',101),('update',102),('warm',102)]):
            state['current']=(code_dirs[0 if index==0 else 1]/'current.json').read_bytes()
            start=len(state['requests']);report=out/('player-'+name+'.json')
            hidden([exe,'-batchmode','-nographics','-resource-probe-cache',cache,'-resource-probe-url',
                'http://127.0.0.1:'+str(server.server_port)+'/current.json','-resource-probe-report',report,'-logFile',out/('player-'+name+'.log')],60)
            result=read_json(report);requests=state['requests'][start:];bundles=[r for r in requests if r['path'].endswith('.bundle')]
            require(result['succeeded'] and result['preloadedVerifiedCode'] and result['pinned'] and result['cacheVerified'] and result['readyPairCommitted']
                and result['fixtureAssetsLoaded'] and result['nestedDataRestored'] and result['marker']==marker,'Native paired assets failed.')
            require(len(bundles)==(2 if index==0 else 1 if index==1 else 0),'Unexpected native bundle requests.')
            require(not any('StandaloneWindows64' in r['path'] for r in requests),'Legacy resource channel contacted.')
            if index:
                require(result['codeSha256']==cases[0]['result']['codeSha256'],'Resource-only update unexpectedly changed game code.')
                require(not any(r['path'].endswith('.dll.bytes') for r in requests),'Unchanged code was downloaded again.')
            if index==2:require(result['downloadBytes']==0,'Warm startup downloaded bundles.')
            cases.append({'name':name,'result':result,'requests':requests,'bundleRequests':len(bundles),
                'measuredHttpBundleBytes':sum(r['bytes'] for r in bundles)})
            print('FIXTURE_NATIVE_PLAYER_OK '+name,flush=True)
    finally:server.shutdown();server.server_close()
    require(base_hashes=={p.relative_to(exe.parent).as_posix():file_hash(p) for p in exe.parent.rglob('*') if p.is_file()},'Base client files changed.')
    write_json(out/'acceptance-summary.json',{'status':'succeeded','scope':'Tiny isolated native fixture, not production game download measurements.',
        'baseUnchanged':True,'baseHashes':base_hashes,'cache':str(cache),'builds':builds,'packages':packages,'cases':cases,
        'originalBaselinePreserved':True,'cloudContacted':False,'gameAssetsModified':False})
    print('INCREMENTAL_NATIVE_ACCEPTANCE_OK '+str(out),flush=True)

if __name__=='__main__':main()
