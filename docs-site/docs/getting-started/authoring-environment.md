---
title: Authoring environment
---

# Setting up an authoring environment

This guide sets up a productive **local environment for writing docs** with Netdocs: an editor, the
`netdocs` CLI on your `PATH`, and a live-preview workflow. It's aimed at content authors — if you
want to hack on Netdocs itself, see [Installation → From source](../getting-started/installation.md#from-source-contributors).

## 1. Install an editor

Any editor works, but [Visual Studio Code](https://code.visualstudio.com/) is a good default for
Markdown:

- **Markdown All in One** — shortcuts, TOC, list editing.
- **markdownlint** — catches malformed Markdown as you type.
- **Even Better TOML / YAML** and a **JSON** language mode — for editing `appsettings.json`.
- **Mermaid preview** (optional) — preview diagrams without a full build.

Enable *Format on Save* and *Trim Trailing Whitespace* so pages stay tidy.

## 2. Install the `netdocs` CLI

Grab a prebuilt binary for your platform (no .NET runtime required). See
[Installation](../getting-started/installation.md) for all options; the short version:

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

    Download `netdocs.exe` from the [releases page](https://github.com/XtremeOwnage/Netdocs/releases/latest)
    and place it in a folder on your `PATH`.

=== "macOS / Linux (portable)"

    ```bash
    chmod +x netdocs
    sudo mv netdocs /usr/local/bin/
    netdocs --help
    ```

### Put it on your PATH

Being able to type `netdocs` from any directory makes the workflow below smoother.

=== "Windows (PowerShell)"

    ```powershell
    # Add the folder containing netdocs.exe to your user PATH (persists across sessions)
    $dir = "C:\tools\netdocs"
    [Environment]::SetEnvironmentVariable(
      "Path", "$([Environment]::GetEnvironmentVariable('Path','User'));$dir", "User")
    # Open a new terminal, then:
    netdocs --help
    ```

=== "macOS / Linux (bash/zsh)"

    ```bash
    # If you moved the binary to /usr/local/bin it's already on PATH.
    # Otherwise add its folder in your shell profile:
    echo 'export PATH="$HOME/bin:$PATH"' >> ~/.profile
    source ~/.profile
    netdocs --help
    ```

## 3. Get the docs project

Clone (or open) the repository that contains your `docs/` folder and `appsettings.json`:

```bash
git clone https://github.com/your-org/your-docs.git
cd your-docs
```

If you're coming from MkDocs and only have a `mkdocs.yml`, convert it first:

```bash
netdocs import mkdocs.yml --out appsettings.json
```

See [Migrating from MkDocs](../setup/migrating-from-mkdocs.md).

## 4. The live-preview loop

Run the dev server. It builds the site, serves it locally, watches your files, and reloads the
browser on every save:

```bash
netdocs serve --config appsettings.json
```

Open <http://localhost:8000> and start editing. Each save triggers an incremental rebuild and a
live-reload over a WebSocket, so the page refreshes on its own.

Handy flags (full list in the [CLI reference](../reference/cli.md)):

| Flag | Use |
|---|---|
| `--port 8080` / `-p` | Serve on a different port. |
| `--no-cache` | Bypass the incremental render cache if a page looks stale. |
| `--strict` | Fail the build on plugin/template errors (good before committing). |
| `--verbose` / `-v` | Trace logging when something isn't rendering. |

!!! tip "Batch/one-shot builds"
    For a plain output build (CI, or just to inspect `site/`), use `netdocs build`. Add `--clean` to
    wipe stale output and `--prod` to enable production-only plugins such as social cards.

## 5. Before you commit

- Run a strict production build to catch errors your dev loop might tolerate:

  ```bash
  netdocs build --prod --strict --clean --config appsettings.json
  ```

- Review the rendered page(s) you changed — don't assume "it parsed" means "it looks right".
- Keep commits small and focused; the [publishing guide](../setup/publishing.md) covers deploying.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `netdocs: command not found` | The binary isn't on your `PATH` — see step 2, and open a new terminal. |
| A page shows stale content | Add `--no-cache`, or delete the `.cache/` folder in your project. |
| Diagrams/emoji don't appear | They load from a CDN; check your network, or see the [Mermaid](../reference/mermaid.md) offline note. |
| Live reload doesn't fire | Confirm you're viewing the `serve` URL (not an old `site/` file) and that the terminal shows a rebuild on save. |
