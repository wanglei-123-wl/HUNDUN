$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    (Join-Path $root 'Assets/Bootstrap/Contracts/IGameStartupContent.cs'),
    (Join-Path $root 'Assets/Bootstrap/Contracts/GameStartupServices.cs')
)
$bridge = [QiDao.Bootstrap.Contracts.GameStartupServices]
$bridge::Reset()
$rejected = $false
try { $bridge::Create() | Out-Null } catch { $rejected = $true }
if (!$rejected) { throw 'Startup must fail closed before the business entry registers services.' }
$rejected = $false
try { $bridge::Register($null) } catch { $rejected = $true }
if (!$rejected) { throw 'A missing business factory must not be accepted.' }
$bridge::Register([Func[QiDao.Bootstrap.Contracts.IGameStartupContent]]{ $null })
$rejected = $false
try { $bridge::Create() | Out-Null } catch { $rejected = $true }
if (!$rejected) { throw 'A factory returning no business services must block startup.' }
$bridge::Reset()
$rejected = $false
try { $bridge::Create() | Out-Null } catch { $rejected = $true }
if (!$rejected) { throw 'Reset must clear services from a previous Play session.' }
Write-Output 'Startup service boundary fails closed before registration, rejects invalid factories, and resets old session state.'
