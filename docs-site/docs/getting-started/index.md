---
title: Getting started
---

# Getting started

Netdocs turns a folder of Markdown into a Material-styled static website. This section
walks you through installing the CLI, setting up an authoring environment, scaffolding a
project, and running your first build.

1. **[Installation](installation.md)** — build from source or install a packaged CLI.
2. **[Authoring environment](authoring-environment.md)** — editor setup, the CLI on your `PATH`,
   and the live-preview loop.
3. **[Quick start](quickstart.md)** — create `appsettings.json`, add content, build and serve.

Already have a Material for MkDocs site? Skip the scaffolding and
**[migrate from MkDocs](../setup/migrating-from-mkdocs.md)** instead — the `netdocs import`
command converts your existing `mkdocs.yml` to `appsettings.json` in one step.

## The mental model

A Netdocs project is a directory that contains:

- an **`appsettings.json`** with a `Netdocs` section (site metadata, theme, nav, plugins);
- a **`docs/`** directory of Markdown content (configurable via `docsDir`);
- an output **`site/`** directory that Netdocs writes (configurable via `siteDir`).

The build pipeline discovers content, runs Markdown through Markdig with the configured
extensions, applies plugins, renders each page through the Scriban Material theme, and
writes HTML plus a search index, sitemap, and any plugin outputs.

## Installing the CLI

The [installation guide](installation.md) covers every distribution channel:

- **Debian/Ubuntu** — `apt install ./netdocs_amd64.deb`
- **RHEL/Fedora** — `yum install netdocs_x86_64.rpm`
- **Windows** — download and run `netdocs.exe`
- **Linux (portable)** — a self-contained single-file binary (no .NET runtime needed)
- **Docker** — `ghcr.io/xtremeownage/netdocs:latest`
- **From source** — `dotnet run --project src/Netdocs.Cli` for contributors

## Publishing

Once your site builds locally, [publish it](../setup/publishing.md). The most common path is
a **GitHub Actions** workflow that builds on push and deploys to GitHub Pages — either with
the reusable [`XtremeOwnage/Netdocs` action](../setup/publishing.md#using-the-netdocs-github-action)
(no .NET on the runner) or the [Docker image](../setup/docker.md). Netdocs can also publish
itself to a **filesystem** path, a **git branch** (`gh-pages`), or **AWS S3** via a `deploy`
section in `appsettings.json` — see [Publishing](../setup/publishing.md) for all targets.
