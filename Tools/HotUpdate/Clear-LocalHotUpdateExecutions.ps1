[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$CodeCacheRoot,
      [Parameter(Mandatory=$true)][string]$ReportPath,
      [string]$ExecutionRoot=(Join-Path $env:LOCALAPPDATA 'QiDao/CodeExecution'),
      [string]$TrustedConfigPath,[switch]$Apply)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
if($TrustedConfigPath){$script:TrustedConfig=[IO.Path]::GetFullPath($TrustedConfigPath)}
$cache=[IO.Path]::GetFullPath($CodeCacheRoot).TrimEnd('\','/')
$root=[IO.Path]::GetFullPath($ExecutionRoot).TrimEnd('\','/')
if($root -eq [IO.Path]::GetPathRoot($root) -or $root -eq $cache -or $root.StartsWith($cache+'\',[StringComparison]::OrdinalIgnoreCase)){
    throw 'Execution cleanup root must be a dedicated directory outside the code cache.'
}
function Assert-PlainPath([string]$Path){
    $current=[IO.Path]::GetFullPath($Path)
    while($current){
        if(Test-Path -LiteralPath $current){if((Get-Item -Force -LiteralPath $current).Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Reparse point is not allowed: '+$current)}}
        $current=[IO.Path]::GetDirectoryName($current)
    }
}
Assert-PlainPath $root;Assert-PlainPath $cache
$state=Read-Utf8Json (Join-Path $cache 'state.json')
$config=Read-Utf8Json $script:TrustedConfig
$protected=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$rsa=[Security.Cryptography.RSA]::Create();$rsa.FromXmlString($config.publicKeyXml)
try{
    foreach($name in @('activeRelease','previousRelease','readyRelease','previousReadyRelease')){
        $id=[string]$state.$name;if(!$id){continue}
        if($id -notmatch '^[a-f0-9]{32}$'){throw 'Invalid protected cache release ID; cleanup stopped.'}
        $path=Join-Path $cache ('releases/'+$id+'/manifest.json');Assert-PlainPath $path
        $e=Read-Utf8Json $path;$bytes=[Convert]::FromBase64String($e.payload)
        if(!$rsa.VerifyData($bytes,[Convert]::FromBase64String($e.signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)){throw 'Protected cached manifest signature failed; cleanup stopped.'}
        $r=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
        if($r.releaseId -ne $id -or $r.codeSha256 -notmatch '^[a-f0-9]{64}$'){throw 'Protected cached manifest identity differs.'}
        [void]$protected.Add($r.codeSha256)
    }
}finally{$rsa.Dispose()}
if(!$protected.Count){throw 'No verified active/recovery code state was found; cleanup stopped.'}
$processes=@(Get-CimInstance Win32_Process -ErrorAction Stop)
$items=[Collections.Generic.List[object]]::new()
$grace=(Read-Utf8Json (Join-Path $PSScriptRoot 'RetentionPolicy.json')).published.retiredVersionGraceDays
if(Test-Path -LiteralPath $root){foreach($folder in Get-ChildItem -LiteralPath $root -Directory -Force){
    $reason=$null;$deleted=$false;$lease=$null;$mutex=$null;$acquired=$false
    try{
        $target=[IO.Path]::GetFullPath($folder.FullName)
        if(!$target.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or $folder.Name -notmatch '^[a-f0-9]{24}$'){throw 'Unknown execution directory; keep it.'}
        Assert-PlainPath $target
        $marker=Join-Path $target '.host-identity';$leasePath=Join-Path $target '.usage-v1'
        Assert-PlainPath $marker;Assert-PlainPath $leasePath
        if(!(Test-Path -LiteralPath $marker) -or !(Test-Path -LiteralPath $leasePath)){throw 'No supported identity/usage lease; keep it.'}
        $identity=[IO.File]::ReadAllText($marker);$parts=$identity.Split('|')
        if($parts.Count -ne 3 -or $parts[0] -ne $cache.ToUpperInvariant() -or $parts[1] -notmatch '^[a-f0-9]{32}$' -or $parts[2] -notmatch '^[a-f0-9]{64}$'){throw 'Another profile or invalid identity; keep it.'}
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$identityHash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity))).Replace('-','').ToLowerInvariant()
            $directoryHash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($target.ToUpperInvariant()))).Replace('-','').ToLowerInvariant()
        }finally{$sha.Dispose()}
        if($folder.Name -ne $identityHash.Substring(0,24)){throw 'Execution identity/path mismatch; keep it.'}
        if($protected.Contains($parts[2])){throw 'Active or recovery code; keep it.'}
        if([DateTime]::UtcNow - (Get-Item -LiteralPath $marker).LastWriteTimeUtc -lt [TimeSpan]::FromDays($grace)){throw 'Recent execution grace period; keep it.'}
        $names=@(Get-ChildItem -LiteralPath $target -File -Filter '*.exe'|ForEach-Object Name)
        if(@($processes|Where-Object {($_.ExecutablePath -and $_.ExecutablePath.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)) -or (!$_.ExecutablePath -and $names -contains $_.Name)}).Count){throw 'Running or unreadable matching process; keep it.'}
        $mutex=[Threading.Mutex]::new($false,('Local\QiDao.ExecutionHost.'+$directoryHash))
        try{$acquired=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$acquired=$true}
        if(!$acquired){throw 'Execution copy is being prepared; keep it.'}
        # Shared readers in bootstrap/game processes block this exclusive writer.
        $lease=[IO.FileStream]::new($leasePath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Delete)
        foreach($file in Get-ChildItem -LiteralPath $target -Recurse -Force){Assert-PlainPath $file.FullName}
        $reason='inactive-unreferenced-and-outside-grace'
        if($Apply){
            # Recheck the resolved absolute boundary immediately before deletion.
            $resolved=[IO.Path]::GetFullPath((Get-Item -LiteralPath $target).FullName)
            if($resolved -ne $target -or !$resolved.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe final cleanup target.'}
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop
            $deleted=$true
        }
    }catch{$reason=$_.Exception.Message}
    finally{if($lease){$lease.Dispose()};if($mutex){if($acquired){$mutex.ReleaseMutex()};$mutex.Dispose()}}
    $items.Add(@{path=$folder.FullName;reason=$reason;deleted=$deleted})
}}
$report=@{schemaVersion=1;applied=[bool]$Apply;codeCacheRoot=$cache;executionRoot=$root;items=@($items.ToArray());resourceCacheTouched=$false}
Write-Utf8Json $ReportPath $report
$report
