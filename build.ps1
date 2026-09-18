param(
    [string]$FFmpegDirectory,
    [string]$PlinkPath
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
$references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll')
$gifSource = Join-Path $PSScriptRoot 'apps\gif-generator'
$monitorSource = Join-Path $PSScriptRoot 'apps\server-monitor'
$usageSource = Join-Path $PSScriptRoot 'apps\gpt-usage-tray'
$gifOutput = Join-Path $PSScriptRoot 'dist\GIF Generator'
$monitorOutput = Join-Path $PSScriptRoot 'dist\Lab Server Monitor'
$usageOutput = Join-Path $PSScriptRoot 'dist\GPT Usage Tray'
New-Item -ItemType Directory -Force -Path $gifOutput, $monitorOutput, $usageOutput, (Join-Path $gifOutput 'tools'), (Join-Path $monitorOutput 'settings') | Out-Null

& $compiler /nologo /target:winexe /optimize+ @references "/out:$gifOutput\GIF Generator.exe" "/win32icon:$gifSource\assets\gif-generator.ico" "$gifSource\GifMaker.cs"
if ($LASTEXITCODE -ne 0) { throw 'GIF Generator build failed.' }
& $compiler /nologo /target:winexe /optimize+ @references "/out:$monitorOutput\Lab Server Monitor.exe" "/win32icon:$monitorSource\monitor.ico" "$monitorSource\ServerMonitor.cs"
if ($LASTEXITCODE -ne 0) { throw 'Lab Server Monitor build failed.' }
Copy-Item -LiteralPath (Join-Path $monitorSource 'collector.sh') -Destination $monitorOutput -Force
Copy-Item -LiteralPath (Join-Path $monitorSource 'settings\servers.example.json') -Destination (Join-Path $monitorOutput 'settings') -Force
& $compiler /nologo /target:winexe /optimize+ @references "/out:$usageOutput\GPT Usage Tray.exe" "/win32icon:$usageSource\gpt-usage.ico" "$usageSource\GPTUsageTray.cs"
if ($LASTEXITCODE -ne 0) { throw 'GPT Usage Tray build failed.' }
Copy-Item -LiteralPath (Join-Path $usageSource 'gpt-usage.ico'), (Join-Path $usageSource 'README.md'), (Join-Path $usageSource 'Install.ps1'), (Join-Path $usageSource '설치.cmd') -Destination $usageOutput -Force

if ($FFmpegDirectory) {
    foreach ($name in @('ffmpeg.exe', 'ffprobe.exe')) {
        Copy-Item -LiteralPath (Join-Path $FFmpegDirectory $name) -Destination (Join-Path $gifOutput 'tools') -Force
    }
}
if ($PlinkPath) { Copy-Item -LiteralPath $PlinkPath -Destination (Join-Path $monitorOutput 'plink.exe') -Force }
Write-Host 'Build complete. See README.md for runtime dependencies and private server configuration.'
