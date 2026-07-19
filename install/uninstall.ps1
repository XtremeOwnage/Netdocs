<#
.SYNOPSIS
    Uninstall the Netdocs CLI on Windows.

.DESCRIPTION
    Removes the installed netdocs.exe and its install directory, and removes that directory
    from the current user's PATH. Mirrors install.ps1.

.PARAMETER InstallDir
    Directory Netdocs was installed to. Defaults to %LOCALAPPDATA%\Programs\Netdocs.

.EXAMPLE
    .\uninstall.ps1
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Netdocs')
)

$ErrorActionPreference = 'Stop'
function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# 1. Remove from user PATH.
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$paths = @($userPath -split ';' | Where-Object { $_ -and $_ -ne $InstallDir })
if (($userPath -split ';') -contains $InstallDir) {
    Write-Step "Removing $InstallDir from your PATH"
    [Environment]::SetEnvironmentVariable('Path', ($paths -join ';'), 'User')
}

# 2. Delete the install directory.
if (Test-Path $InstallDir) {
    Write-Step "Deleting $InstallDir"
    Remove-Item -Path $InstallDir -Recurse -Force
}

Write-Host ''
Write-Host 'Netdocs uninstalled. Open a new terminal for the PATH change to take effect.' -ForegroundColor Green
