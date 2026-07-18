---
title: Migrating from MkDocs
---

# Migrating from MkDocs

Netdocs is a drop-in-minded successor to a Material for MkDocs project: it reads the same
Markdown, ships equivalents for the common plugins, and can convert your existing
`mkdocs.yml` for you. This guide walks a real migration — the
[XtremeOwnage blog](https://static.xtremeownage.com/blog), which this project was built to
replace — from an mkdocs-container CI pipeline to Netdocs.

## 1. Convert the configuration

Run the built-in importer against your existing `mkdocs.yml` to generate an
`appsettings.json`:

```pwsh
netdocs import mkdocs.yml --out appsettings.json
```

The importer maps site metadata, `theme`, `nav` (including nested sections), `plugins`,
`markdown_extensions`, `extra_css`, `extra_javascript`, and `extra`. See the
[import command reference](../reference/cli.md#importing-from-mkdocs) for details.

Review the generated file and remove any plugins that don't have a Netdocs equivalent, then
build locally to validate:

```pwsh
netdocs build --config appsettings.json
```

## 2. Map environment-driven behavior

The MkDocs blog toggled behavior with `MKDOCS_*` environment variables (prod builds,
file-filtering, macros, nav pruning). In Netdocs these become explicit config plus the
`--prod` flag:

| MkDocs env var | Netdocs equivalent |
|---|---|
| `MKDOCS_PROD_BUILD=true` | `netdocs build --prod` (enables prod-only plugins like social cards) |
| `MKDOCS_FILE_FILTER=true` | [`file-filter` plugin](../plugins/file-filter.md) in `plugins` |
| `MKDOCS_MACROS_ENABLED=true` | [`macros` plugin](../plugins/macros.md) in `plugins` |
| `SITE_NAME`, `REPO_URL`, `REPO_NAME` | `siteName`, `repoUrl`, `repoName` (or `NETDOCS_`-prefixed env vars) |

Any config value can still be overridden at build time with a `NETDOCS_`-prefixed
environment variable, e.g. `NETDOCS_SITENAME`.

## 3. Replace the CI workflow

The XO blog previously built inside an mkdocs container and deployed with
`mkdocs gh-deploy`. Two drop-in replacements are available.

### Option A — Netdocs Docker image (closest to the old pipeline)

The published [GHCR image](docker.md) bundles the CLI, so the workflow barely changes —
swap the mkdocs image for `ghcr.io/xtremeownage/netdocs` and the build step for
`netdocs build` + git deploy:

```yaml
name: Build Docs On Tag Release
on:
  workflow_dispatch:
  release:
    types: [published]

permissions:
  contents: write

jobs:
  build:
    runs-on: ubuntu-latest
    container:
      image: ghcr.io/xtremeownage/netdocs:latest
    env:
      SITE_NAME: My Documentation
      NETDOCS_REPONAME: ${{ github.repository }}
      NETDOCS_REPOURL: ${{ github.server_url }}/${{ github.repository }}
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0   # git-revision-date needs full history
      - name: Build and deploy
        run: netdocs build --prod --config appsettings.json
```

Configure the `deploy` section in `appsettings.json` to publish the built site to the
`gh-pages` branch (mirrors `mkdocs gh-deploy`):

```json
"deploy": {
  "target": "gh-pages",
  "branch": "gh-pages",
  "remote": "origin",
  "message": "Deploy docs",
  "push": true
}
```

For the DEV workflow (`push` to non-`main` branches), drop `--prod` and point the deploy
`branch` at `gh-pages-dev`.

### Option B — setup-dotnet (no container)

If you'd rather not use a container, install the runtime and run the CLI directly — this
matches the workflow that builds this documentation site:

```yaml
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Build docs
        run: dotnet run --project src/Netdocs.Cli -c Release -- build --prod --config appsettings.json
```

End users who aren't building from source should instead download a
[release binary](../getting-started/installation.md) or use the Docker image rather than
running `dotnet`.

## 4. Verify parity

Before switching DNS/Pages over, build both generators and diff the output:

- Page count and nav match (`Built <N> pages` should be close to your mkdocs page count).
- Search, tags, blog, and social cards render as expected.
- Internal links resolve under your Pages base path (Netdocs rewrites links to be
  base-path safe automatically).

See the [benchmarks](../about/benchmarks.md) for the performance you can expect after the
switch, and the [plugins reference](../plugins/index.md) for the built-in plugin set.
