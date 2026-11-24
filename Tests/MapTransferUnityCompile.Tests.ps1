$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$unity = 'C:\Program Files\Unity 2022.3.62f1\Editor\Data'
$dotnet = Join-Path $unity 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $unity 'DotNetSdkRoslyn/csc.dll'
$original = Join-Path $root 'Library/Bee/artifacts/1900b0aE.dag/Assembly-CSharp.rsp'
$transfer = 'Assets/Scripts/MapTransferCoordinator.cs'
$interaction = 'Assets/Scripts/MapTeleporterInteraction.cs'
if (-not (Test-Path -LiteralPath $dotnet) -or
    -not (Test-Path -LiteralPath $compiler) -or
    -not (Test-Path -LiteralPath $original) -or
    -not (Test-Path -LiteralPath (Join-Path $root $transfer)) -or
    -not (Test-Path -LiteralPath (Join-Path $root $interaction))) {
    throw 'Unity compiler, last response file, or map transfer source is missing.'
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::Combine(
    $temporaryParent, "map-transfer-compile-$([Guid]::NewGuid().ToString('N'))"))
if (-not $temporaryRoot.StartsWith(
    $temporaryParent.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Compiler output must stay inside the system temporary directory.'
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $lines = [Collections.Generic.List[string]]::new()
    $hasTransfer = $false
    $hasInteraction = $false
    foreach ($line in [IO.File]::ReadAllLines($original)) {
        if ($line -like '-out:*') {
            $lines.Add('-out:"' + (Join-Path $temporaryRoot 'Assembly-CSharp.dll') + '"')
        }
        elseif ($line -like '-refout:*') {
            $lines.Add('-refout:"' + (Join-Path $temporaryRoot 'Assembly-CSharp.ref.dll') + '"')
        }
        else {
            $lines.Add($line)
        }
        if ($line.Trim('"') -eq $transfer)
        {
            $hasTransfer = $true
        }
        if ($line.Trim('"') -eq $interaction)
        {
            $hasInteraction = $true
        }
    }
    if (-not $hasTransfer) {
        $lines.Add('"' + $transfer + '"')
    }
    if (-not $hasInteraction) {
        $lines.Add('"' + $interaction + '"')
    }
    foreach ($source in @('Assets/Scripts/MapTransferStatusView.cs',
        'Assets/Scripts/MapTransferResourcePreflight.cs',
        'Assets/Scripts/LoginScripts/LoadingBackgroundSlideshow.cs')) {
        if (-not ($lines | Where-Object { $_.Trim('"') -eq $source })) {
            $lines.Add('"' + $source + '"')
        }
    }
    $response = Join-Path $temporaryRoot 'Assembly-CSharp.rsp'
    [IO.File]::WriteAllLines($response, $lines,
        [Text.UTF8Encoding]::new($false))
    Push-Location -LiteralPath $root
    try {
        & $dotnet $compiler "@$response"
        if ($LASTEXITCODE -ne 0) {
            throw "Unity script compilation failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
    Write-Output 'PASS: current Assembly-CSharp sources compile with Unity Roslyn.'
}
finally {
    if ([IO.Directory]::Exists($temporaryRoot) -and
        $temporaryRoot.StartsWith(
            $temporaryParent.TrimEnd([IO.Path]::DirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
