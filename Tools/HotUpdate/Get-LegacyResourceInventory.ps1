[CmdletBinding()]
param([string]$SshTarget='root@47.93.78.106',[Parameter(Mandatory=$true)][string]$OutputPath)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
if($SshTarget -notmatch '^[A-Za-z0-9_.-]+@[A-Za-z0-9.-]+$'){throw 'Invalid SSH login.'}
# Read only: does not initialize the new channel, repair pointers or delete data.
$source=@'
import os,json,shutil,pathlib
roots=['/srv/hundun-upload','/srv/hundun-releases','/srv/hundun-resources']
rows=[]
for name in roots:
 p=pathlib.Path(name);items=[]
 if p.is_dir():
  for child in p.iterdir():
   if child.is_symlink():items.append({'name':child.name,'symlinkTarget':str(child.resolve())});continue
   size=0;files=0
   if child.is_dir():
    for folder,dirs,names in os.walk(child,followlinks=False):
     dirs[:]=[d for d in dirs if not pathlib.Path(folder,d).is_symlink()]
     for file in names:
      q=pathlib.Path(folder,file)
      if not q.is_symlink() and q.is_file():size+=q.stat().st_size;files+=1
   elif child.is_file():size=child.stat().st_size;files=1
   items.append({'name':child.name,'logicalBytes':size,'files':files})
 rows.append({'root':name,'items':items})
active=pathlib.Path('/srv/hundun-resources/StandaloneWindows64')
print(json.dumps({'readOnly':True,'diskFreeBytes':shutil.disk_usage('/srv').free,'legacyActiveDirectory':str(active.resolve()),'directories':rows,'note':'logical bytes can double-count hard links; this report does not authorize deletion'}))
'@
$encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($source))
$command="python3 -c 'import base64;exec(base64.b64decode(`"$encoded`"))'"
$result=& ssh.exe -o ConnectTimeout=20 -o ServerAliveInterval=15 $SshTarget $command
if($LASTEXITCODE -ne 0){throw 'Read-only legacy inventory failed.'}
$json=($result -join "`n")|ConvertFrom-Json
Write-Utf8Json $OutputPath $json
$json
