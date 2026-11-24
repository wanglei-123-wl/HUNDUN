$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Assets/Editor/ReleaseDuplicatePolicy.cs')
if (![ReleaseDuplicatePolicy]::IsSmallMetadata('0000000000000000f000000000000000', [long[]]@(10304), 268) -or
    ![ReleaseDuplicatePolicy]::IsSmallMetadata('424a8351358298f49830eb6d76d863c4', [long[]]@(4890085278179872738), 44)) {
    throw 'The measured tiny engine/bake placeholders should remain explicitly classified.'
}
foreach ($case in @(
    @{ Guid = '42df2bce77beb604ca0235f736032b10'; Ids = [long[]]@(10304); Bytes = 268 },
    @{ Guid = '0000000000000000f000000000000000'; Ids = [long[]]@(10304); Bytes = 2048 },
    @{ Guid = '0000000000000000f000000000000000'; Ids = [long[]]@(10304, 10202); Bytes = 268 },
    @{ Guid = '424a8351358298f49830eb6d76d863c4'; Ids = [long[]]@(4890085278179872738); Bytes = 256 }
)) {
    if ([ReleaseDuplicatePolicy]::IsSmallMetadata($case.Guid, $case.Ids, $case.Bytes)) {
        throw 'Business assets, larger payloads or additional objects must never be hidden as metadata.'
    }
}
Write-Output 'Measured metadata exceptions, byte limits and rejection of business or additional objects passed.'
