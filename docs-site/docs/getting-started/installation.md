---
title: Installation
---

# Installation

Netdocs ships as a single CLI executable named **`netdocs`**. Most users should download
a prebuilt binary; building from source is only needed if you're contributing.

## Requirements

- A supported OS: Linux, macOS, or Windows.
- **No .NET runtime required** for the prebuilt/self-contained binaries.
- The **.NET 11 SDK** (preview, pinned via `global.json`) is only needed to build from
  source.

## Download a release (recommended)

Grab the build for your platform from the
**[releases page](https://github.com/XtremeOwnage/Netdocs/releases/latest)**.

=== "Debian / Ubuntu"

    ```bash
    curl -LO https://github.com/XtremeOwnage/Netdocs/releases/latest/download/netdocs_amd64.deb
    sudo apt install ./netdocs_amd64.deb
    netdocs --help
    ```

=== "RHEL / Fedora"

    ```bash
    sudo yum install https://github.com/XtremeOwnage/Netdocs/releases/latest/download/netdocs_x86_64.rpm
    netdocs --help
    ```

=== "Windows"

    Download `netdocs.exe` from the releases page and run it directly (optionally add its
    folder to your `PATH`):

    ```powershell
    .\netdocs.exe --help
    ```

=== "Linux (portable)"

    Download the self-contained `netdocs` binary, then:

    ```bash
    chmod +x netdocs
    ./netdocs --help
    ```

=== "Docker"

    ```bash
    docker run --rm -v "$PWD:/site" ghcr.io/xtremeownage/netdocs:latest build --config /site/appsettings.json
    ```

    See [Docker](../setup/docker.md) for details.

!!! note
    Exact asset file names may vary between releases — check the
    [releases page](https://github.com/XtremeOwnage/Netdocs/releases/latest) for the
    file that matches your platform and architecture. Packaging details live under
    [Packaging](../setup/packaging.md).

## From source (contributors)

If you're hacking on Netdocs itself:

```pwsh
git clone https://github.com/XtremeOwnage/Netdocs.git
cd Netdocs
dotnet build Netdocs.slnx -c Release
```

Run the CLI directly through `dotnet run`:

```pwsh
dotnet run --project src/Netdocs.Cli -- build --config path/to/appsettings.json
```

Or publish your own self-contained executable:

```pwsh
dotnet publish src/Netdocs.Cli -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -o dist
./dist/netdocs --help
```

Replace `linux-x64` with `win-x64` or `osx-arm64` as appropriate.

## Verify the install

```pwsh
netdocs --help
```

You should see the usage summary. Continue to the **[Quick start](quickstart.md)**.
