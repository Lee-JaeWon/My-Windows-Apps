param(
    [switch]$Publish,
    [string]$InstallerPath = 'release/My.Windows.Apps.Setup.exe'
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content '.github/releases/installer.json' -Raw | ConvertFrom-Json
if ($manifest.tag -notmatch '^my-windows-apps-setup-v\d+\.\d+\.\d+$' -or $manifest.sha256 -notmatch '^[a-f0-9]{64}$') {
    throw 'Invalid release tag or SHA-256.'
}

# Check that the approved binary still corresponds to the source being released.
foreach ($entry in $manifest.source_blobs.PSObject.Properties) {
    $actual = git hash-object -- $entry.Name
    if ($LASTEXITCODE -ne 0 -or $actual -ne $entry.Value) {
        throw "Installer does not match the current source: $($entry.Name)"
    }
}
if (!(Test-Path -LiteralPath $InstallerPath)) {
    if ($Publish) { throw 'Verified installer artifact is missing.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $InstallerPath) | Out-Null
    try {
        Invoke-WebRequest -Uri $manifest.source_url -OutFile $InstallerPath -TimeoutSec 180
    } catch {
        # The public release redirect can time out while the asset API still works.
        # Download the exact same asset; SHA-256 verification below is mandatory.
        $sourceUri = [Uri]$manifest.source_url
        $parts = $sourceUri.AbsolutePath.Trim('/').Split('/')
        if ($sourceUri.Scheme -ne 'https' -or $sourceUri.Host -ne 'github.com' -or $parts.Count -ne 6 -or $parts[2] -ne 'releases' -or $parts[3] -ne 'download') { throw }
        Write-Warning 'Public release download failed; retrying through the GitHub asset API.'
        $sourceRepo = $parts[0] + '/' + $parts[1]
        gh release download ([Uri]::UnescapeDataString($parts[4])) --repo $sourceRepo --pattern ([Uri]::UnescapeDataString($parts[5])) --output $InstallerPath --clobber
        if ($LASTEXITCODE -ne 0) { throw 'Release asset API download failed.' }
    }
}
if ((Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256) {
    throw 'Installer SHA-256 mismatch.'
}
Write-Output "Verified installer and source for $($manifest.tag)."
if (!$Publish) { return }
if ($env:GITHUB_REPOSITORY -ne 'Lee-JaeWon/My-Windows-Apps' -or $env:GITHUB_REF -ne 'refs/heads/main') {
    throw 'Publication is allowed only from the upstream main branch.'
}

# Retry safely: never overwrite a published asset with different bytes.
$json = gh release view $manifest.tag --repo $env:GITHUB_REPOSITORY --json assets,isDraft 2>$null
if ($LASTEXITCODE -eq 0) {
    $release = $json | ConvertFrom-Json
    $asset = @($release.assets | Where-Object name -eq 'My.Windows.Apps.Setup.exe')
    if ($asset.Count -gt 0) {
        if ($asset.Count -ne 1 -or $asset[0].digest -ne ('sha256:' + $manifest.sha256)) {
            throw 'Existing release installer has a different or unavailable SHA-256.'
        }
    } else {
        gh release upload $manifest.tag $InstallerPath --repo $env:GITHUB_REPOSITORY
        if ($LASTEXITCODE -ne 0) { throw 'Installer upload failed.' }
    }
    gh release edit $manifest.tag --repo $env:GITHUB_REPOSITORY --draft=false --latest
} else {
    gh release create $manifest.tag $InstallerPath --repo $env:GITHUB_REPOSITORY --target $env:GITHUB_SHA --title $manifest.title --notes-file '.github/releases/installer-notes.md' --latest
}
if ($LASTEXITCODE -ne 0) { throw 'Release publication failed.' }
