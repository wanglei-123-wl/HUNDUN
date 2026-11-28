[CmdletBinding()]
param([Parameter(Mandatory=$true)][long]$Sequence,
      [Parameter(Mandatory=$true)][string]$BuildProjectDirectory,
      [string]$ReleaseId=([Guid]::NewGuid().ToString('N')),
      [string]$SourceProjectDirectory,
      [string]$OutputDirectory,
      [string]$ResourceBuildReport,[switch]$RebuildResources,
      [ValidateSet('Auto','Baseline','Update','Reuse')][string]$ResourceMode='Auto',
      [string]$PreviousResourceReport,[string]$SourceVerificationReport,
      [string]$BaseClientProof,
      [string]$SigningKeyPath='C:\QiDaoBuild\Keys\QiDaoHotUpdate-RSA.xml',
      [string]$UnityEditor='C:\Program Files\Unity 2022.3.62f1\Editor\Unity.exe')
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
if (!$SourceProjectDirectory) { $SourceProjectDirectory = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent }
if ($Sequence -le 0 -or $ReleaseId -notmatch '^[a-f0-9]{32}$') { throw 'A positive publication sequence and new hexadecimal ID are required.' }
$packingStatePath = Join-Path $SourceProjectDirectory 'Tools/ResourcePacking/packing-state.json'
if(!$PreviousResourceReport){$PreviousResourceReport=$ResourceBuildReport}
if($RebuildResources){$ResourceMode='Baseline'}
$workflowHelper=Join-Path (Split-Path $PSScriptRoot -Parent) 'ResourcePacking/resource_workflow.py'
if (Test-Path -LiteralPath $packingStatePath) {
    $packingState = Read-Utf8Json $packingStatePath
    $hasWorkflow=$false
    if($PreviousResourceReport -and (Test-Path -LiteralPath $PreviousResourceReport)){
        $hasWorkflow=(Read-Utf8Json $PreviousResourceReport).workflowProtocol -eq 1
    }
    if ($packingState.requiresNewResourceBaseline -and $ResourceMode -ne 'Baseline' -and !$hasWorkflow) {
        throw 'Resource packing changed; the old resource report cannot be reused. A fresh paired resource baseline is required.'
    }
    if ($packingState.requiresNewResourceBaseline -and !$SourceVerificationReport) {
        throw 'Resource source read integrity is not verified. Release building is held; see Tools/ResourcePacking/packing-state.json.'
    }
}
if(!$SourceVerificationReport){throw 'Supply a fresh source verification report from Verify-ResourceInputs.ps1.'}
$SourceVerificationReport=[IO.Path]::GetFullPath($SourceVerificationReport)
$verifiedInputs=Invoke-PythonJson @($workflowHelper,'verify-snapshot','--source',$SourceProjectDirectory,'--snapshot',$SourceVerificationReport)
if($ResourceMode -ne 'Baseline'){
    if(!$PreviousResourceReport){throw 'Supply the previous workflow resource report, or choose Baseline.'}
    $PreviousResourceReport=[IO.Path]::GetFullPath($PreviousResourceReport)
    $choice=Invoke-PythonJson @($workflowHelper,'choose','--mode',$ResourceMode.ToLowerInvariant(),'--snapshot',$SourceVerificationReport,'--previous',$PreviousResourceReport)
    $selectedMode=[string]$choice.mode
}else{$selectedMode='baseline'}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $SourceProjectDirectory ('Build/HotUpdateReleases/'+$ReleaseId) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Release output is immutable; choose a new ID/directory.' }
$buildNeedle = [IO.Path]::GetFullPath($BuildProjectDirectory).Replace('\','/').ToUpperInvariant()
if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Replace('\','/').ToUpperInvariant().Contains($buildNeedle) }) {
    throw 'The isolated build project is already open in Unity.'
}
$sync = Invoke-PythonJson -Arguments @((Join-Path $PSScriptRoot 'release_build.py'),'--source',$SourceProjectDirectory,'--target',$BuildProjectDirectory,'--snapshot',$SourceVerificationReport)
[IO.Directory]::CreateDirectory($output) | Out-Null
Write-Utf8Json (Join-Path $output 'source-sync.json') $sync
function Invoke-UnityMethod([string]$Method,[string]$RequestPath,[string]$LogPath) {
    $arguments = @('-batchmode','-nographics','-quit','-projectPath',$BuildProjectDirectory,'-executeMethod',$Method,'-hotupdate-request',$RequestPath,'-logFile',$LogPath)
    $quoted = ($arguments | ForEach-Object { Quote-NativeArgument $_ }) -join ' '
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $quoted -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw ('Unity build failed; see '+$LogPath) }
}
if ($selectedMode -ne 'reuse') {
    $resourceOutput = Join-Path $output 'ResourceBuild'
    $resourceRequest = Join-Path $output 'resource-request.json'
    $resourceArguments=@($workflowHelper,'request','--source',$SourceProjectDirectory,'--snapshot',$SourceVerificationReport,
        '--mode',$selectedMode,'--output',$resourceOutput,'--resource-id',([Guid]::NewGuid().ToString('N')))
    if($PreviousResourceReport){$resourceArguments+=@('--previous',$PreviousResourceReport)}
    $resourceBuildRequest=Invoke-PythonJson $resourceArguments
    Write-Utf8Json $resourceRequest $resourceBuildRequest
    Invoke-UnityMethod 'HotUpdateResourceBuild.BuildFromRequest' $resourceRequest (Join-Path $output 'resource-build.log')
    $ResourceBuildReport = Join-Path $resourceOutput 'resource-build-report.json'
    $finalizeArgs=@($workflowHelper,'finalize','--report',$ResourceBuildReport)
    if($PreviousResourceReport){$finalizeArgs+=@('--previous',$PreviousResourceReport)}
    $resourceWorkflow=Invoke-PythonJson $finalizeArgs
}else{
    $ResourceBuildReport=$PreviousResourceReport
}
$code = Join-Path $output 'CodeBuild'; $request = Join-Path $output 'code-request.json'
Write-Utf8Json $request @{outputDirectory=$code;releaseId=$ReleaseId;sequence=$Sequence;signingKeyPath=$SigningKeyPath;resourceBuildReport=[IO.Path]::GetFullPath($ResourceBuildReport)}
Invoke-UnityMethod 'HotUpdateCodeBuild.BuildCodeRelease' $request (Join-Path $output 'code-build.log')
$postBuildInputs=Invoke-PythonJson @($workflowHelper,'verify-snapshot','--source',$SourceProjectDirectory,'--snapshot',$SourceVerificationReport)
$arguments = @((Join-Path $PSScriptRoot 'release_package.py'),'build','--config',$script:TrustedConfig,'--package',(Join-Path $output 'Package'),
    '--code-directory',$code,'--resource-report',[IO.Path]::GetFullPath($ResourceBuildReport))
if ($BaseClientProof) { $arguments += @('--client-proof',[IO.Path]::GetFullPath($BaseClientProof)) }
$package = Invoke-PythonJson -Arguments $arguments
Write-Utf8Json (Join-Path $output 'release-summary.json') @{status='built';releaseId=$ReleaseId;sequence=$Sequence;resourceMode=$selectedMode;resourceReport=[IO.Path]::GetFullPath($ResourceBuildReport);sourceVerification=$SourceVerificationReport;formalBaseClientProofIncluded=[bool]$BaseClientProof;published=$false}
Write-Host ('HOTUPDATE_RELEASE_BUILT '+$output)
[pscustomobject]@{status='built';releaseId=$ReleaseId;sequence=$Sequence;releaseDirectory=$output;
    packageDirectory=(Join-Path $output 'Package');formalBaseClientProofIncluded=[bool]$BaseClientProof;published=$false}
