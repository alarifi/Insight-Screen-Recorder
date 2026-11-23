$ErrorActionPreference = 'Stop'
$release = Join-Path (Split-Path -Parent $PSScriptRoot) 'Release'
$zip = Join-Path $release 'InsightScreenRecorder_Win_x64.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
if (Test-Path -LiteralPath $zip) { throw 'Archive already exists. Build a fresh staged release before packaging again.' }
$files = @(Get-ChildItem -LiteralPath $release -Recurse -File)
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($release.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
& (Join-Path $PSScriptRoot 'Verify-Release.ps1')
$files | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + $_.Path.Substring($release.Length + 1) } | Set-Content (Join-Path $release 'FILES-SHA256.txt') -Encoding utf8
Get-FileHash -LiteralPath (Join-Path $release 'InsightScreenRecorder.exe'),$zip -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + [IO.Path]::GetFileName($_.Path) } | Set-Content (Join-Path $release 'SHA256SUMS.txt') -Encoding utf8

