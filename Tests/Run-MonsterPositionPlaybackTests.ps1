param([Parameter(Mandatory)][string]$UnityEditorData)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$engine = Join-Path $UnityEditorData 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
if (-not (Test-Path -LiteralPath $engine)) { throw "UnityEngine.CoreModule.dll was not found under $UnityEditorData" }

$dir = [IO.Directory]::CreateTempSubdirectory('monster-position-tests-').FullName
$project = Join-Path $dir 'MonsterPositionPlayback.Tests.csproj'
$enginePath = [Security.SecurityElement]::Escape($engine)
$runtimeSource = [Security.SecurityElement]::Escape((Join-Path $root 'Assets/Scripts/Monster/Movement/MonsterPositionPlayback.cs'))
$testSource = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'MonsterPositionPlayback.Tests.cs'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="UnityEngine.CoreModule" HintPath="$enginePath" />
    <Compile Include="$runtimeSource" />
    <Compile Include="$testSource" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding UTF8
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw "Monster playback test failed under $dir" }
