$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'
$OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$script:HotUpdateRoot = $PSScriptRoot
$script:TrustedConfig = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Assets/Resources/Startup/HotUpdateBootstrap.json'

function Write-Utf8Json([string]$Path, $Value) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))) | Out-Null
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 80), [Text.UTF8Encoding]::new($false))
}
function Read-Utf8Json([string]$Path) { Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json }
function Invoke-PythonJson([string[]]$Arguments) {
    $output = & python @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Hot-update helper failed; operation stopped.' }
    ($output -join "`n") | ConvertFrom-Json
}
function Quote-NativeArgument([string]$Value) {
    $builder = [Text.StringBuilder]::new('"'); $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $slashes++; continue }
        $count = $slashes
        if ($character -eq '"') { $count = $slashes * 2 + 1 }
        [void]$builder.Append('\', $count); [void]$builder.Append($character); $slashes = 0
    }
    [void]$builder.Append('\', $slashes * 2); [void]$builder.Append('"'); $builder.ToString()
}
function Assert-ServerPath([string]$Path) {
    if ($Path -notmatch '^/[A-Za-z0-9_./-]+$' -or $Path.Split('/') -contains '..' -or $Path.TrimEnd('/') -in @('/srv','/','/srv/hundun-resources')) {
        throw 'Invalid owned server directory.'
    }
}
function Get-HotUpdateToolchainHash {
    $lines = foreach($name in @('release_common.py','release_package.py','release_retention.py','release_store.py')) {
        $name+' '+(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $script:HotUpdateRoot $name)).Hash.ToLowerInvariant()+"`n"
    }
    $hash=[Security.Cryptography.SHA256]::Create()
    try{[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join '')))).Replace('-','').ToLowerInvariant()}finally{$hash.Dispose()}
}
function Invoke-HotUpdateStore {
    param([Parameter(Mandatory=$true)]$Request,
          [string]$SshTarget='root@47.93.78.106',
          [string]$ServerRoot='/srv/hundun-hotupdate/windows-mono-v1',
          [string]$ServerTools='/srv/hundun-hotupdate-tools/windows-mono-v1',
          [string]$LocalStoreDirectory,
          [string]$InputJsonPath,[string]$InputArchivePath)
    $toolHash = Get-HotUpdateToolchainHash
    $Request['toolchainSha256'] = $toolHash
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($Request | ConvertTo-Json -Compress -Depth 80)))
    $start = [Diagnostics.ProcessStartInfo]::new()
    if ($LocalStoreDirectory) {
        $start.FileName = (Get-Command python -ErrorAction Stop).Source
        $nativeArguments = @((Join-Path $script:HotUpdateRoot 'release_store.py'),'--root',[IO.Path]::GetFullPath($LocalStoreDirectory),'--request-b64',$encoded)
    } else {
        if ($SshTarget -notmatch '^[A-Za-z0-9_.-]+@[A-Za-z0-9.-]+$') { throw 'Invalid SSH login.' }
        Assert-ServerPath $ServerRoot; Assert-ServerPath $ServerTools
        $start.FileName = (Get-Command ssh.exe -ErrorAction Stop).Source
        $remote = "python3 '$ServerTools/$toolHash/release_store.py' --root '$ServerRoot' --request-b64 '$encoded'"
        $nativeArguments = @('-o','ConnectTimeout=20','-o','ServerAliveInterval=15','-o','ServerAliveCountMax=3',$SshTarget,$remote)
    }
    $start.Arguments = ($nativeArguments | ForEach-Object { Quote-NativeArgument $_ }) -join ' '
    $start.UseShellExecute = $false; $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $start.EnvironmentVariables['PYTHONUTF8'] = '1'
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if ($InputJsonPath) {
            $bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($InputJsonPath))
            $process.StandardInput.BaseStream.Write($bytes,0,$bytes.Length)
        } elseif ($InputArchivePath) {
            $inputStream = [IO.File]::OpenRead([IO.Path]::GetFullPath($InputArchivePath))
            try { $inputStream.CopyTo($process.StandardInput.BaseStream) } finally { $inputStream.Dispose() }
        }
        $process.StandardInput.Close(); $process.WaitForExit()
        $text = $stdout.GetAwaiter().GetResult(); $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw ('Server operation failed; live version was not assumed successful. ' + $errorText) }
        if ($errorText) { Write-Warning $errorText }
        $text | ConvertFrom-Json
    } finally { $process.Dispose() }
}
function Sign-HotUpdateEnvelope($Payload, [string]$SigningKeyPath) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Payload | ConvertTo-Json -Compress -Depth 80))
    $key = [Security.Cryptography.RSA]::Create()
    try {
        $key.FromXmlString([IO.File]::ReadAllText([IO.Path]::GetFullPath($SigningKeyPath)))
        $signature = $key.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    } finally { $key.Dispose() }
    @{payload=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)}
}
