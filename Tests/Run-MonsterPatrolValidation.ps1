$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & dotnet run --project Tests/MonsterPatrolProtocol.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Patrol protocol regression failed.' }
    & dotnet run --project Tests/StateFrameArrayParser.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'State frame parser regression failed.' }
    $cases = @('FlyingMonsterDeathPresentation', 'Ls003MonsterAnimatorDriver',
        'Ls005MonsterAnimatorDriver', 'Ls006MonsterAnimatorDriver', 'Ls007MonsterAnimatorDriver',
        'SL002MonsterStatePresentation', 'BS001MonsterAnimatorDriver', 'CavecrawlerMonsterAnimatorDriver',
        'HellcreeperMonsterAnimatorDriver', 'GorosaurusMonsterAnimatorDriver', 'MarhomorphMonsterAnimatorDriver',
        'TyranopodeMonsterAnimatorDriver', 'MonsterCatalogWiring', 'MonsterGenerationRegistry',
        'MonsterCombatGeneration', 'MonsterGenerationPresentation')
    foreach ($case in $cases) {
        $testPath = Join-Path $PSScriptRoot "$case.Tests.ps1"
        # Old YAML assertions use multiline $; normalize CRLF in memory, keeping assets unchanged.
        # A separate process also isolates Add-Type fixture names between tests.
        $bootstrap = @'
function Get-Content {
    $value = Microsoft.PowerShell.Management\Get-Content @args
    if ($value -is [string]) { $value.Replace([string][char]13, '') } else { $value }
}
'@
        $command = $bootstrap + "`n& '" + $testPath.Replace("'", "''") + "'"
        & pwsh -NoProfile -Command $command
        if ($LASTEXITCODE -ne 0) { throw "$case failed." }
    }
    'PASS: all monster patrol regression suites.'
} finally { Pop-Location }
