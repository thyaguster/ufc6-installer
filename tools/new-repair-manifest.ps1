param(
    [Parameter(Mandatory=$true)][string]$SplitDirectory,
    [Parameter(Mandatory=$true)][string]$ExpectedPayloadSha256,
    [Parameter(Mandatory=$true)][long]$ExpectedArchiveBytes,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($ExpectedPayloadSha256 -notmatch '\A[0-9a-fA-F]{64}\z') { throw 'ExpectedPayloadSha256 must be a SHA-256 digest from the signed package.' }
$directory = (Resolve-Path -LiteralPath $SplitDirectory).ProviderPath
$header = Join-Path $directory 'data.bin.header'
if (!(Test-Path -LiteralPath $header -PathType Leaf) -or (Get-Item -LiteralPath $header).Length -ne 65536) { throw 'Expected a 65,536-byte data.bin.header.' }
$partFiles = @(Get-ChildItem -LiteralPath $directory -File | Where-Object Name -Match '^data\.bin\.\d{3}$' | Sort-Object Name)
if (!$partFiles.Count) { throw 'No numbered data.bin parts were found.' }
$partSize = [long]$partFiles[0].Length
$payload = [Security.Cryptography.SHA256]::Create()
$buffer = New-Object byte[] (4MB)
$parts = @()
[long]$offset = 65536
try {
    for ($index = 0; $index -lt $partFiles.Count; $index++) {
        $file = $partFiles[$index]
        if ($file.Name -ne ('data.bin.' + ($index + 1).ToString('D3'))) { throw 'Numbered parts are missing or out of order.' }
        if ($file.Length -le 0 -or ($index -lt $partFiles.Count - 1 -and $file.Length -ne $partSize) -or $file.Length -gt $partSize) { throw "Unexpected part length: $($file.Name)" }
        $hash = [Security.Cryptography.SHA256]::Create()
        $input = [IO.File]::OpenRead($file.FullName)
        try {
            while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                [void]$hash.TransformBlock($buffer, 0, $read, $buffer, 0)
                [void]$payload.TransformBlock($buffer, 0, $read, $buffer, 0)
            }
            [void]$hash.TransformFinalBlock([byte[]]@(), 0, 0)
            $parts += [ordered]@{ name=$file.Name; offset=$offset; length=[long]$file.Length; sha256=[BitConverter]::ToString($hash.Hash).Replace('-','') }
            $offset += [long]$file.Length
            Write-Host "Hashed $($file.Name)"
        } finally { $input.Dispose(); $hash.Dispose() }
    }
    [void]$payload.TransformFinalBlock([byte[]]@(), 0, 0)
    $actual = [BitConverter]::ToString($payload.Hash).Replace('-','')
    if ($actual -ne $ExpectedPayloadSha256.ToUpperInvariant()) { throw 'The split files do not match the signed package SHA-256. Refusing to publish a repair manifest.' }
    if ($offset -ne $ExpectedArchiveBytes) { throw 'The split files do not match the signed package length.' }
    $manifest = [ordered]@{
        format = 1
        archiveBytes = $ExpectedArchiveBytes
        payloadSha256 = $actual
        headerBytes = 65536
        headerSha256 = (Get-FileHash -LiteralPath $header -Algorithm SHA256).Hash
        parts = $parts
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    Write-Host "Repair manifest: $OutputPath"
} finally { $payload.Dispose() }
