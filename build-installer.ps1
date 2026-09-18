param(
    [string]$FFmpegDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\Gif50\tools'),
    [string]$PlinkPath = (Join-Path $env:LOCALAPPDATA 'Programs\LabServerMonitor\plink.exe'),
    [string]$FFmpegLicensePath = (Join-Path $env:LOCALAPPDATA 'Programs\Gif50\FFmpeg-LICENSE.txt'),
    [string]$PuttyLicensePath
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
foreach ($name in @('ffmpeg.exe', 'ffprobe.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $FFmpegDirectory $name))) { throw "$name was not found in $FFmpegDirectory" }
}
if (!(Test-Path -LiteralPath $PlinkPath)) { throw "plink.exe was not found: $PlinkPath" }
if (!(Test-Path -LiteralPath $FFmpegLicensePath)) { throw "FFmpeg license was not found: $FFmpegLicensePath" }

& (Join-Path $PSScriptRoot 'build.ps1') -FFmpegDirectory $FFmpegDirectory -PlinkPath $PlinkPath

$staging = Join-Path $PSScriptRoot 'installer\staging'
$release = Join-Path $PSScriptRoot 'release'
$resolvedRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
$resolvedStaging = [IO.Path]::GetFullPath($staging)
if (!$resolvedStaging.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging directory.' }
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging, $release | Out-Null

$gifPackage = Join-Path $staging 'gif'
$monitorPackage = Join-Path $staging 'monitor'
$usagePackage = Join-Path $staging 'usage'
New-Item -ItemType Directory -Force -Path (Join-Path $gifPackage 'tools'), (Join-Path $monitorPackage 'settings'), $usagePackage | Out-Null

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\GIF Generator\GIF Generator.exe') -Destination $gifPackage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\GIF Generator\tools\ffmpeg.exe'), (Join-Path $PSScriptRoot 'dist\GIF Generator\tools\ffprobe.exe') -Destination (Join-Path $gifPackage 'tools')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'apps\gif-generator\README.md'), $FFmpegLicensePath -Destination $gifPackage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'apps\gif-generator\assets\gif-generator.ico') -Destination $gifPackage

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\Lab Server Monitor\Lab Server Monitor.exe'), (Join-Path $PSScriptRoot 'dist\Lab Server Monitor\collector.sh'), (Join-Path $PSScriptRoot 'dist\Lab Server Monitor\plink.exe'), (Join-Path $PSScriptRoot 'apps\server-monitor\monitor.ico'), (Join-Path $PSScriptRoot 'apps\server-monitor\README.md') -Destination $monitorPackage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'apps\server-monitor\settings\servers.example.json') -Destination (Join-Path $monitorPackage 'settings')
$packagedPuttyLicense = Join-Path $monitorPackage 'PUTTY-LICENSE.html'
if ($PuttyLicensePath -and (Test-Path -LiteralPath $PuttyLicensePath)) {
    Copy-Item -LiteralPath $PuttyLicensePath -Destination $packagedPuttyLicense
} else {
    $downloaded = $false
    foreach ($attempt in 1..3) {
        try { Invoke-WebRequest -Uri 'https://www.chiark.greenend.org.uk/~sgtatham/putty/licence.html' -OutFile $packagedPuttyLicense -TimeoutSec 60; $downloaded = $true; break }
        catch { if ($attempt -eq 3) { throw } }
    }
    if (!$downloaded) { throw 'PuTTY license download failed.' }
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\GPT Usage Tray\GPT Usage Tray.exe'), (Join-Path $PSScriptRoot 'apps\gpt-usage-tray\gpt-usage.ico'), (Join-Path $PSScriptRoot 'apps\gpt-usage-tray\README.md') -Destination $usagePackage

$gifZip = Join-Path $staging 'gif.zip'
$monitorZip = Join-Path $staging 'monitor.zip'
$usageZip = Join-Path $staging 'usage.zip'
Compress-Archive -Path (Join-Path $gifPackage '*') -DestinationPath $gifZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $monitorPackage '*') -DestinationPath $monitorZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $usagePackage '*') -DestinationPath $usageZip -CompressionLevel Optimal

$output = Join-Path $release 'My Windows Apps Setup.exe'
$references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll')
& $compiler /nologo /target:winexe /optimize+ @references "/win32icon:$PSScriptRoot\apps\gif-generator\assets\gif-generator.ico" "/resource:$gifZip,Payload.Gif.zip" "/resource:$monitorZip,Payload.Monitor.zip" "/resource:$usageZip,Payload.Usage.zip" "/out:$output" (Join-Path $PSScriptRoot 'installer\MyWindowsAppsSetup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Write-Host "Installer created: $output"
