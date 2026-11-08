$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'apps\gpt-usage-tray'
$output = Join-Path $root 'dist\details-checks'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /main:DetailsUiChecks /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll "/out:$output\DetailsUiChecks.exe" "/resource:$source\fonts\Pretendard-Regular.ttf,Fonts.Regular" "/resource:$source\fonts\Pretendard-Bold.ttf,Fonts.Bold" "$source\GPTUsageTray.cs" "$source\DetailsForm.cs" (Join-Path $PSScriptRoot 'DetailsUiChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI checks did not compile.' }
& (Join-Path $output 'DetailsUiChecks.exe') $output
if ($LASTEXITCODE -ne 0) { throw 'UI checks failed.' }
