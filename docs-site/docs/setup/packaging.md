---
title: Packaging
---

# Packaging (.deb / .rpm / .msi)

Netdocs provides a workflow that builds native Linux packages of the `netdocs` CLI for
Debian/Ubuntu (`.deb`) and RHEL/Fedora (`.rpm`), so users can install it with their
system package manager, plus a Windows Installer package (`.msi`).

## Installing

=== "Debian / Ubuntu"

    ```bash
    sudo dpkg -i netdocs_<version>_amd64.deb
    # or
    sudo apt install ./netdocs_<version>_amd64.deb
    netdocs --help
    ```

=== "RHEL / Fedora"

    ```bash
    sudo rpm -i netdocs-<version>-1.x86_64.rpm
    # or
    sudo dnf install ./netdocs-<version>-1.x86_64.rpm
    netdocs --help
    ```

=== "Windows"

    ```powershell
    # per-user, no admin rights (adds netdocs to the user PATH)
    msiexec /i netdocs-<version>-win-x64.msi
    # machine-wide (elevated; adds netdocs to the system PATH)
    msiexec /i netdocs-<version>-win-x64.msi ALLUSERS=1
    ```

    Running the `.msi` interactively lets you pick **Just for you** or **Everyone**.

The package installs the self-contained CLI (binary plus its `theme/` templates and
assets) under `/opt/netdocs` and symlinks `/usr/bin/netdocs`; no .NET runtime is required
on the target machine.

## How it's built

The workflow publishes a self-contained `linux-x64` single-file binary and uses
[`nfpm`](https://nfpm.goreleaser.com/) to produce both package formats from a single
`packaging/nfpm.yaml` manifest:

```yaml
name: netdocs
arch: amd64
platform: linux
version: ${VERSION}
maintainer: Netdocs contributors
description: A fast, flexible static site generator in .NET (Material for MkDocs derivative).
license: MIT
contents:
  # The self-contained publish folder (binary + theme).
  - src: ./dist
    dst: /opt/netdocs
  - src: /opt/netdocs/netdocs
    dst: /usr/bin/netdocs
    type: symlink
```

## Releasing

`.github/workflows/packages.yml` runs on version tags (`v*`) and on manual dispatch. It
builds the binary, runs `nfpm` for `deb` and `rpm`, and attaches the packages to the
GitHub Release (and as workflow artifacts).

The `msi` job then builds the Windows installer on a Windows runner from the published
`win-x64` binary with [WiX Toolset](https://wixtoolset.org/), using
`packaging/msi/Netdocs.wxs`. The package is dual-purpose (`Scope="perUserOrMachine"`): a
per-user install goes to `%LOCALAPPDATA%` and appends to the user `PATH`, a per-machine install
goes to Program Files and appends to the system `PATH`. The job installs and uninstalls the
MSI in both modes as a smoke test before attaching it to the release. Upgrades replace the
previous version within the same scope; switching scope means uninstalling the old one first.
