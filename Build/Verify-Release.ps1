$ErrorActionPreference = 'Stop'
$release = Join-Path (Split-Path -Parent $PSScriptRoot) 'Release'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\InsightScreenRecorder\InsightScreenRecorder.csproj') -Raw
if ($project.SelectNodes('/Project/PropertyGroup/Content').Count -ne 0) { throw 'Invalid Content placement.' }
foreach ($name in @('InsightScreenRecorder.exe','InsightScreenRecorder.dll','coreclr.dll','hostfxr.dll','System.Windows.Forms.dll','Assets\app.ico','Assets\app.png','README.md','THIRD_PARTY_NOTICES.md','VERSION.txt','default-settings.json','BUILD-INFO.json','tools\ffmpeg.exe','tools\ffprobe.exe')) {
    $path = Join-Path $release $name
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Missing release file: $name" }
}
$buildInfo = Get-Content -LiteralPath (Join-Path $release 'BUILD-INFO.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $release 'tools\ffmpeg.exe') -Algorithm SHA256).Hash -ne $buildInfo.EncoderSHA256) { throw 'Bundled encoder differs from BUILD-INFO.json.' }
if ((Get-FileHash -LiteralPath (Join-Path $release 'InsightScreenRecorder.dll') -Algorithm SHA256).Hash -ne $buildInfo.ApplicationSHA256) { throw 'Application differs from BUILD-INFO.json.' }
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $release 'InsightScreenRecorder.exe'))
if ($null -eq $icon) { throw 'Executable icon unavailable.' }
$icon.Dispose()
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $release 'InsightScreenRecorder_Win_x64.zip'))
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '(^|/)(obj|bin|src)/|\.pdb$|\.cs$') { throw "Unexpected distribution entry: $($entry.FullName)" }
        $source = Join-Path $release $entry.FullName
        if (-not (Test-Path -LiteralPath $source)) { throw "ZIP-only file: $($entry.FullName)" }
        $stream = $entry.Open()
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','') } finally { $hasher.Dispose(); $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) { throw "ZIP hash mismatch: $($entry.FullName)" }
    }
    $expected = @(Get-ChildItem -LiteralPath $release -Recurse -File | Where-Object { $_.Name -notin @('InsightScreenRecorder_Win_x64.zip','SHA256SUMS.txt','FILES-SHA256.txt') })
    if ($expected.Count -ne $archive.Entries.Count) { throw 'ZIP inventory count mismatch.' }
    Write-Output "PASS release inventory, embedded icon extraction and ZIP byte integrity: $($archive.Entries.Count) files"
} finally { $archive.Dispose() }

