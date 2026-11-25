[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$InputFile,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(1, 1024)][int]$PartSizeGiB = 10
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$InputFile = (Resolve-Path -LiteralPath $InputFile).ProviderPath
if ((Split-Path -Leaf $InputFile) -ne 'data.bin') { throw 'InputFile must be named data.bin.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory | Out-Null }
[long]$partSize = [long]$PartSizeGiB * 1GB
$length = (Get-Item -LiteralPath $InputFile).Length
if ($length -le $partSize) { throw 'data.bin is not larger than the requested part size; distribute the single file instead.' }
$count = [int][Math]::Ceiling(($length - 65536) / [double]$partSize)
$header = Join-Path $OutputDirectory 'data.bin.header'
if (Test-Path -LiteralPath $header) { throw "Refusing to overwrite existing $header" }
for ($index = 1; $index -le $count; ++$index) {
    $part = Join-Path $OutputDirectory ('data.bin.' + $index.ToString('D3'))
    if (Test-Path -LiteralPath $part) { throw "Refusing to overwrite existing $part" }
}
$input = [IO.File]::OpenRead($InputFile)
try {
    $headerOutput = [IO.File]::Create($header)
    try {
        $headerBytes = New-Object byte[] 65536; $headerRead = 0
        while ($headerRead -lt $headerBytes.Length) {
            $read = $input.Read($headerBytes, $headerRead, $headerBytes.Length - $headerRead)
            if ($read -le 0) { throw 'data.bin does not contain a complete signed header.' }
            $headerRead += $read
        }
        $headerOutput.Write($headerBytes, 0, $headerBytes.Length)
    } finally { $headerOutput.Dispose() }
    $buffer = New-Object byte[] (4MB)
    for ($index = 1; $index -le $count; ++$index) {
        $part = Join-Path $OutputDirectory ('data.bin.' + $index.ToString('D3'))
        $output = [IO.File]::Create($part)
        try {
            [long]$remaining = [Math]::Min($partSize, $input.Length - $input.Position)
            while ($remaining -gt 0) {
                $read = $input.Read($buffer, 0, [int][Math]::Min([long]$buffer.Length, $remaining))
                if ($read -le 0) { throw 'Could not read data.bin while splitting.' }
                $output.Write($buffer, 0, $read); $remaining -= $read
            }
        } finally { $output.Dispose() }
        Write-Progress -Activity 'Splitting data.bin' -Status (Split-Path -Leaf $part) -PercentComplete ([int]($index * 100 / $count))
    }
} catch {
    if (Test-Path -LiteralPath $header) { Remove-Item -LiteralPath $header -Force }
    throw
} finally { $input.Dispose(); Write-Progress -Activity 'Splitting data.bin' -Completed }
Write-Host "Created data.bin.header and $count numbered payload parts in $OutputDirectory. The original data.bin was not changed."
