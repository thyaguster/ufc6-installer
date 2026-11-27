$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root ('repair-test-' + [Guid]::NewGuid().ToString('N'))
$parts = Join-Path $testRoot 'parts'
New-Item -ItemType Directory -Path $parts -Force | Out-Null
try {
    $header = New-Object byte[] 65536
    [byte[]]$payload = 1..39
    [IO.File]::WriteAllBytes((Join-Path $parts 'data.bin.header'), $header)
    [IO.File]::WriteAllBytes((Join-Path $parts 'data.bin.001'), $payload[0..15])
    [IO.File]::WriteAllBytes((Join-Path $parts 'data.bin.002'), $payload[16..31])
    [IO.File]::WriteAllBytes((Join-Path $parts 'data.bin.003'), $payload[32..38])
    $dataBin = Join-Path $testRoot 'data.bin'
    [IO.File]::WriteAllBytes($dataBin, ($header + $payload))
    $originalHash = (Get-FileHash -LiteralPath $dataBin -Algorithm SHA256).Hash
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $expected = [BitConverter]::ToString($hash.ComputeHash($payload)).Replace('-', '') }
    finally { $hash.Dispose() }
    $manifest = Join-Path $testRoot 'manifest.json'
    & (Join-Path $root 'tools\new-repair-manifest.ps1') -SplitDirectory $parts -ExpectedPayloadSha256 $expected -ExpectedArchiveBytes 65575 -OutputPath $manifest
    & (Join-Path $root 'tools\check-repair-data-bin.ps1') -DataBin $dataBin -Manifest $manifest

    $stream = [IO.File]::Open($dataBin, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { $stream.Position = 65536 + 20; $stream.WriteByte(255) }
    finally { $stream.Dispose() }
    try {
        & (Join-Path $root 'tools\check-repair-data-bin.ps1') -DataBin $dataBin -Manifest $manifest
        throw 'A corrupted range was not detected.'
    } catch {
        if ($_.Exception.Message -notlike 'Damaged range(s): data.bin.002*') { throw }
    }
    & (Join-Path $root 'tools\check-repair-data-bin.ps1') -DataBin $dataBin -Manifest $manifest -ReplacementPart (Join-Path $parts 'data.bin.002')
    & (Join-Path $root 'tools\check-repair-data-bin.ps1') -DataBin $dataBin -Manifest $manifest
    if ((Get-FileHash -LiteralPath $dataBin -Algorithm SHA256).Hash -ne $originalHash) { throw 'Repair changed the wrong bytes.' }
    Write-Host 'PASS: diagnose a damaged range, repair only that range, and verify the result.'
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $workspace = [IO.Path]::GetFullPath($root).TrimEnd('\')
    if (!$resolved.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to remove a path outside the workspace.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
