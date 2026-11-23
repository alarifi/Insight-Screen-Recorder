param([string]$Compiler = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'output'
if (-not (Test-Path -LiteralPath $Compiler -PathType Leaf)) { throw 'Inno Setup compiler not found. Supply -Compiler with the ISCC.exe path.' }
& (Join-Path $PSScriptRoot 'Verify-Release.ps1')
$manifest = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tools\TOOLCHAIN.json') -Raw | ConvertFrom-Json)
foreach ($entry in $manifest) {
    if ($entry.File -notin @('ffmpeg.exe','ffprobe.exe')) { throw 'Unexpected tool manifest entry.' }
    foreach ($base in @((Join-Path $root 'Release\tools'),(Join-Path $PSScriptRoot 'tools'))) {
        if ((Get-FileHash -LiteralPath (Join-Path $base $entry.File)).Hash -ne $entry.SHA256) { throw "Tool metadata mismatch: $($entry.File). Regenerate TOOLCHAIN.json for the actual tools." }
    }
}
if ($manifest.Count -ne 2) { throw 'Both media tools must be documented.' }
$version = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\InsightScreenRecorder\InsightScreenRecorder.csproj') -Raw)).Project.PropertyGroup.Version
$recipe = Get-Content -LiteralPath (Join-Path $root 'InsightScreenRecorder.iss') -Raw
if ($recipe -notmatch ('(?m)^#define AppVersion "'+[regex]::Escape($version)+'"')) { throw 'Installer version does not match application source.' }
$releaseInfo = Get-Content -LiteralPath (Join-Path $root 'Release\BUILD-INFO.json') -Raw | ConvertFrom-Json
if ($releaseInfo.Version -ne $version) { throw 'Build the current application before compiling its installer.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $Compiler (Join-Path $root 'InsightScreenRecorder.iss') *> (Join-Path $output 'installer-build.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $output 'installer-build.log') -Tail 30; throw 'Installer compilation failed.' }
$installer = Join-Path $output "InsightScreenRecorder_Setup_${version}_x64.exe"
$file = Get-Item -LiteralPath $installer
if ($file.Length -eq 0) { throw 'Empty installer.' }
$hash = (Get-FileHash -LiteralPath $installer).Hash
$compilerLog = Get-Content -LiteralPath (Join-Path $output 'installer-build.log') -Raw
$compilerVersion = [regex]::Match($compilerLog,'Compiler engine version: ([^\r\n]+)').Groups[1].Value
"$hash  $($file.Name)" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8
[ordered]@{Version=$version;BuiltUtc=[DateTime]::UtcNow.ToString('o');Installer=$file.Name;Bytes=$file.Length;SHA256=$hash;CompilerVersion=$compilerVersion;CompilerNonCommercialBanner=($compilerLog -match 'Non-commercial use only');DefaultInstallDirectory='C:\Program Files\Insight Tools';ApplicationSHA256=$releaseInfo.ApplicationSHA256;Signed=$false;CleanMachineInstallationTested=$false;RecordingStabilityVerified=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'INSTALLER-INFO.json') -Encoding utf8
Write-Output "INSTALLER CREATED: $installer ($($file.Length) bytes)"
