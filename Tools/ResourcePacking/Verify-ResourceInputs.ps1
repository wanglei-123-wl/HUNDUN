[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportPath,[string]$SourceProjectDirectory)
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'HotUpdate/HotUpdate.Common.ps1')
if(!$SourceProjectDirectory){$SourceProjectDirectory=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent}
Invoke-PythonJson @((Join-Path $PSScriptRoot 'resource_workflow.py'),'snapshot','--source',[IO.Path]::GetFullPath($SourceProjectDirectory),'--output',[IO.Path]::GetFullPath($ReportPath))
