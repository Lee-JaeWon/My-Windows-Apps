$ErrorActionPreference = 'Stop'
$sourceDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExecutable = Join-Path $sourceDirectory 'GPT Usage Tray.exe'
if (!(Test-Path -LiteralPath $sourceExecutable)) { throw 'GPT Usage Tray.exe를 찾을 수 없습니다.' }

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\GPTUsageTray'
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
$installedExecutable = Join-Path $installDirectory 'GPT Usage Tray.exe'

Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedExecutable } | Stop-Process -Force
Copy-Item -LiteralPath $sourceExecutable -Destination $installedExecutable -Force
foreach ($name in @('gpt-usage.ico', 'README.md')) {
    $source = Join-Path $sourceDirectory $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $installDirectory -Force }
}

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'GPT Usage Tray.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExecutable
$shortcut.WorkingDirectory = $installDirectory
$icon = Join-Path $installDirectory 'gpt-usage.ico'
if (Test-Path -LiteralPath $icon) { $shortcut.IconLocation = $icon + ',0' }
$shortcut.Description = 'Codex Pro 주간 사용량 표시'
$shortcut.Save()

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty -Path $runKey -Name 'GPT Usage Tray' -Value ('"' + $installedExecutable + '"') -PropertyType String -Force | Out-Null
Start-Process -FilePath $installedExecutable
Write-Host '설치가 완료되었습니다. 작업표시줄 맨 왼쪽의 사용량 위젯을 확인하세요.'
