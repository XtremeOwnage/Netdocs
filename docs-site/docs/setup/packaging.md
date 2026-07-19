---
title: Packaging
---

# Packaging (.deb / .rpm)

Netdocs provides a workflow that builds native Linux packages of the `netdocs` CLI for
Debian/Ubuntu (`.deb`) and RHEL/Fedora (`.rpm`), so users can install it with their
system package manager.

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
