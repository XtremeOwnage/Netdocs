---
title: Installation
---

# Installation

Netdocs ships as a single CLI executable named **`netdocs`**. You can build it from
source or use one of the packaged distributions.

## Requirements

- **.NET 11 SDK** (preview) — pinned via `global.json` when building from source.
- A supported OS: Linux, macOS, or Windows.

## From source

```pwsh
git clone https://github.com/XtremeOwnage/Netdocs.git
cd Netdocs
dotnet build Netdocs.slnx -c Release
```

Run the CLI directly through `dotnet run`:

```pwsh
dotnet run --project src/Netdocs.Cli -- build --config path/to/appsettings.json
```

Or publish a self-contained executable:

```pwsh
dotnet publish src/Netdocs.Cli -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -o dist
./dist/netdocs --help
```

Replace `linux-x64` with `win-x64` or `osx-arm64` as appropriate.

## Packaged distributions

Netdocs provides workflows that produce ready-to-install artifacts:

- **Docker image** — see [Docker](../setup/docker.md).
- **Debian/Ubuntu (`.deb`) and RHEL/Fedora (`.rpm`) packages** — see
  [Packaging](../setup/packaging.md).

=== "Debian / Ubuntu"

    ```bash
    sudo dpkg -i netdocs_<version>_amd64.deb
    netdocs --help
    ```

=== "RHEL / Fedora"

    ```bash
    sudo rpm -i netdocs-<version>.x86_64.rpm
    netdocs --help
    ```

=== "Docker"

    ```bash
    docker run --rm -v "$PWD:/site" ghcr.io/xtremeownage/netdocs:latest build
    ```

## Verify the install

```pwsh
netdocs --help
```

You should see the usage summary. Continue to the **[Quick start](quickstart.md)**.
