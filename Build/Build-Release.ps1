param([string]$RuntimeVersion = '10.0.13')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sourceRoot = $PSScriptRoot
$root = Split-Path -Parent $sourceRoot
$project = Join-Path $sourceRoot 'src\InsightScreenRecorder\InsightScreenRecorder.csproj'
$release = Join-Path $root 'Release'
$stage = Join-Path $root ('.release-staging-' + [Guid]::NewGuid().ToString('N'))
$log = Join-Path $sourceRoot 'build-release.log'
function Assert-WorkspacePath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Path is outside the workspace: $resolved" }
}
function Run-DotNet([string[]]$Arguments) {
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE). See build-release.log" }
}
try {
    Push-Location $root
    try {
        foreach ($running in @(Get-Process InsightScreenRecorder -ErrorAction SilentlyContinue)) {
            if ($running.Path -and $running.Path.StartsWith($release.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Close Insight Screen Recorder before building. The current Release will not be changed.'
            }
        }
        'Offline win-x64 self-contained Release build' | Set-Content -LiteralPath $log -Encoding UTF8
        Run-DotNet @('--version')
        $encoder = Join-Path $sourceRoot 'tools\ffmpeg.exe'
        if (-not (Test-Path -LiteralPath $encoder)) { throw 'Local tools\ffmpeg.exe is required. No download attempted.' }
        $encoderDevices = (& $encoder -hide_banner -devices 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0 -or $encoderDevices -notmatch 'gdigrab') { throw 'Local FFmpeg must provide Windows gdigrab capture.' }
        $encoderCodecs = (& $encoder -hide_banner -encoders 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0 -or $encoderCodecs -notmatch 'libx264') { throw 'Local FFmpeg must provide libx264 encoding.' }
        $encoderHelp = (& $encoder -hide_banner -h full 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0 -or $encoderHelp -notmatch 'fps_mode') { throw 'Local FFmpeg is too old: fps_mode support is required.' }
        $dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
        $hostRoot = Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Host.win-x64'
        $hostFile = Get-ChildItem -LiteralPath $hostRoot -Filter apphost.exe -Recurse -File | Where-Object { $_.FullName -match '\\10\.[^\\]+\\runtimes\\win-x64\\native\\apphost.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($null -eq $hostFile) { throw 'No local .NET 10 x64 apphost is available. No download attempted.' }
        $offline = @("-p:RuntimeFrameworkVersion=$RuntimeVersion", '-p:EnableAppHostPackDownload=false', "-p:AppHostSourcePath=$($hostFile.FullName)")
        Run-DotNet (@('restore', $project, '--configfile', (Join-Path $sourceRoot 'NuGet.Config'), '-r', 'win-x64', '-p:SelfContained=true', '-p:NuGetAudit=false', '--verbosity', 'minimal') + $offline)
        Run-DotNet (@('publish', $project, '-c', 'Release', '-r', 'win-x64', '--no-restore', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:UseSharedCompilation=false', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $stage, '--verbosity', 'minimal') + $offline)
        $exe = Join-Path $stage 'InsightScreenRecorder.exe'
        if (-not (Test-Path -LiteralPath $exe) -or (Get-Item -LiteralPath $exe).Length -eq 0) { throw 'No executable produced.' }
        foreach ($name in @('README.md','THIRD_PARTY_NOTICES.md','VERSION.txt','default-settings.json')) { $documentRoot = if ($name -eq 'THIRD_PARTY_NOTICES.md') { $root } else { $sourceRoot }; Copy-Item -LiteralPath (Join-Path $documentRoot $name) -Destination $stage }
        $localTools = Join-Path $sourceRoot 'tools'
        if (Test-Path -LiteralPath (Join-Path $localTools 'ffmpeg.exe')) {
            New-Item -ItemType Directory -Path (Join-Path $stage 'tools') -Force | Out-Null
            foreach ($tool in @('ffmpeg.exe','ffprobe.exe')) { $toolPath = Join-Path $localTools $tool; if (Test-Path -LiteralPath $toolPath) { Copy-Item -LiteralPath $toolPath -Destination (Join-Path $stage 'tools') } }
            $prevEA = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
            & (Join-Path $localTools 'ffmpeg.exe') -L 2>$null | Out-File -LiteralPath (Join-Path $stage 'tools\FFMPEG-LICENSE-INFO.txt') -Encoding utf8
            & (Join-Path $localTools 'ffprobe.exe') -L 2>$null | Out-File -LiteralPath (Join-Path $stage 'tools\FFPROBE-LICENSE-INFO.txt') -Encoding utf8
            $ErrorActionPreference = $prevEA
            foreach ($notice in @('TOOLCHAIN.json','GPL-3.0.txt')) { Copy-Item -LiteralPath (Join-Path $localTools $notice) -Destination (Join-Path $stage 'tools') }
        }
        [ordered]@{
            Version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
            BuiltUtc = [DateTime]::UtcNow.ToString('o')
            Runtime = $RuntimeVersion
            Platform = 'win-x64'
            Offline = $true
            Encoder = 'tools\ffmpeg.exe'
            EncoderSHA256 = (Get-FileHash -LiteralPath (Join-Path $stage 'tools\ffmpeg.exe') -Algorithm SHA256).Hash
            ApplicationSHA256 = (Get-FileHash -LiteralPath (Join-Path $stage 'InsightScreenRecorder.dll') -Algorithm SHA256).Hash
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'BUILD-INFO.json') -Encoding utf8
        if (Test-Path -LiteralPath $release) {
            $backup = Join-Path $root ('Archive\PreviousReleases\release-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
            Assert-WorkspacePath $release; Assert-WorkspacePath $backup; Assert-WorkspacePath $stage
            New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
            [IO.Directory]::Move($release, $backup)
            try { [IO.Directory]::Move($stage, $release) } catch { [IO.Directory]::Move($backup, $release); throw }
        } else { [IO.Directory]::Move($stage, $release) }
        & (Join-Path $sourceRoot 'Package-Release.ps1')
        if (-not $?) { throw 'Packaging failed.' }
        Write-Host "BUILD AND PACKAGE CREATED: $(Join-Path $release 'InsightScreenRecorder.exe')"
        Write-Host 'User-supplied local FFmpeg tools are included when available. No downloads were performed.'
    } finally { Pop-Location }
} catch { Write-Host "BUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }

