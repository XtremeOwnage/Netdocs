<#
.SYNOPSIS
    Install the Netdocs CLI on Windows.

.DESCRIPTION
    Downloads the self-contained netdocs.exe from GitHub Releases (or copies a local build),
    installs it under %LOCALAPPDATA%\Programs\Netdocs, and adds that folder to the current
    user's PATH so `netdocs` works from any new terminal. No admin rights required.

.PARAMETER Version
    Release version to install without the leading 'v' (e.g. 1.2.3). Defaults to the latest
    release.

.PARAMETER FromFile
    Install a local netdocs.exe instead of downloading (useful for offline installs or testing
    a freshly built binary). Takes precedence over -Version.

.PARAMETER InstallDir
    Target directory. Defaults to %LOCALAPPDATA%\Programs\Netdocs.

.EXAMPLE
    irm https://raw.githubusercontent.com/XtremeOwnage/Netdocs/main/install/install.ps1 | iex

.EXAMPLE
    .\install.ps1 -Version 1.2.3

.EXAMPLE
    .\install.ps1 -FromFile .\artifacts\bin\Netdocs.Cli\release\netdocs.exe
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$FromFile,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Netdocs')
)

$ErrorActionPreference = 'Stop'
$repo = 'XtremeOwnage/Netdocs'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# 1. Resolve the source binary (local file or a GitHub release asset).
$target = Join-Path $InstallDir 'netdocs.exe'
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

if ($FromFile) {
    if (-not (Test-Path $FromFile)) { throw "File not found: $FromFile" }
    Write-Step "Installing from local file $FromFile"
    Copy-Item -Path $FromFile -Destination $target -Force
}
else {
    if (-not $Version) {
        Write-Step 'Resolving latest release'
        $rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" `
            -Headers @{ 'User-Agent' = 'netdocs-installer' }
        $Version = ($rel.tag_name -replace '^v', '')
    }
    $asset = "netdocs-$Version-win-x64.exe"
    $url = "https://github.com/$repo/releases/download/v$Version/$asset"
    Write-Step "Downloading $asset (v$Version)"
    Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing
}

# 2. Ensure the install dir is on the user PATH (persistent + current session).
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$paths = @($userPath -split ';' | Where-Object { $_ })
if ($paths -notcontains $InstallDir) {
    Write-Step "Adding $InstallDir to your PATH"
    $newPath = (@($paths + $InstallDir) -join ';')
    [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    $env:Path = "$env:Path;$InstallDir"
}
else {
    Write-Step 'PATH already contains the install directory'
}

# 3. Verify.
$installed = & $target --version 2>$null
$label = if ($installed) { "Netdocs $installed" } else { 'Netdocs' }
Write-Host ''
Write-Host "$label installed to $target" -ForegroundColor Green
Write-Host 'Open a new terminal (or restart your shell) and run: netdocs --help' -ForegroundColor Green
