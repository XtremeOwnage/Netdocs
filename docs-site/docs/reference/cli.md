---
title: CLI reference
---

# CLI reference

The `netdocs` executable exposes these commands: `build`, `serve`, `watch`, `new`, and `import`.

```text
netdocs - static site generator

Usage:
  netdocs build [options]     Build the site to the output directory
  netdocs serve [options]     Serve with live reload
  netdocs watch [options]     Publish daemon: poll a git remote and rebuild on push
  netdocs new [path]           Scaffold an annotated appsettings.json
  netdocs import [mkdocs.yml]  Convert an mkdocs.yml to appsettings.json
```

## Commands

| Command | Description |
|---|---|
| `netdocs build` | Build the site to the configured output dir (`siteDir`). |
| `netdocs serve` | Kestrel dev server with file-watch rebuilds + WebSocket live reload. |
| `netdocs watch` | Long-running publish daemon: polls a git remote and rebuilds when the tracked branch advances. |
| `netdocs new` | Scaffold a fully-annotated `appsettings.json` (all common options + doc links). |
| `netdocs import` | Convert an existing `mkdocs.yml` into a Netdocs `appsettings.json`. |
| `netdocs --help` | Print usage. |

### `netdocs new`

Writes a ready-to-edit `appsettings.json` with every common setting, sane defaults, and inline
links back to this documentation. The file is JSONC — `//` comments and trailing commas are kept
and parsed at build time, so you can leave the guidance in place.

```bash
netdocs new                    # writes ./appsettings.json
netdocs new docs/appsettings.json
netdocs new --force            # overwrite an existing file
```

Starting from scratch:

```bash
netdocs new
mkdir docs && echo "# Home" > docs/index.md
netdocs serve
```


If no command is given, `build` is assumed.

## Options

| Option | Alias | Description |
|---|---|---|
| `--config <path>` | `-f` | Path to `appsettings.json` (default `./appsettings.json`). |
| `--port <port>` | `-p` | Dev server port for `serve` (default `8000`). |
| `--clean` | | Remove existing output before building. |
| `--no-cache` | | Ignore the incremental render cache and re-render every page. |
| `--strict` | | Treat build warnings (including [validation](validation.md) problems, plugin/template errors) as failures. |
| `--prod` | `--production` | Production build (enables prod-only plugins such as social cards). |
| `--verbose` | `-v` | Verbose (Trace) logging. |
| `--remote <name>` | | `watch`: git remote to poll (default `origin`). |
| `--branch <name>` | | `watch`: branch to track (default the current branch). |
| `--interval <sec>` | | `watch`: poll interval in seconds (default `30`). |
| `--once` | | `watch`: run a single check-and-rebuild, then exit (useful for cron/testing). |

!!! note
    Every build writes output incrementally: files are only rewritten when their bytes
    change, and files no longer produced are pruned. Unchanged pages keep their previous
    contents and timestamps, which is what makes republishing a small change cheap.

## Incremental render cache

The Markdown parse/render step is content-hash cached under `.cache/render.json`
(gitignored). On each build, a page's rendered HTML, title, plain text, and table of
contents are reused when its processed Markdown, the pipeline configuration, and the site
link map are all unchanged — so warm builds skip re-rendering unchanged pages.

The cache is self-invalidating: adding, removing, or renaming any page changes the link map
hash and invalidates every entry (a full re-render), which keeps cross-page links and
navigation correct. Pass `--no-cache` to bypass it entirely, or delete the `.cache/`
directory to reset it. Each build logs how many pages were reused, e.g.
`Render cache: 243/243 pages reused`.

## Watch daemon

`netdocs watch` is a long-running publish daemon — distinct from `serve`, which watches
local files for a developer. Instead, `watch` polls a **git remote** and rebuilds the site
in place whenever the tracked branch advances, so a push (e.g. from CI or a teammate)
becomes a live update without a full CI job:

```pwsh
netdocs watch --remote origin --branch main --interval 30
```

On each poll it runs `git fetch` and compares your `HEAD` to the remote branch:

- **Remote advanced (fast-forward):** the working tree is advanced with `git merge --ff-only`
  and the site is rebuilt.
- **Local ahead or diverged:** the sync is skipped with a warning and **no local commits are
  touched** — the daemon never runs a destructive reset.

Every rebuild is a full, cache-accelerated build: the render cache reuses unchanged Markdown
and the output writer republishes only the files whose bytes actually changed (pruning any
that are no longer produced). This keeps the on-disk diff minimal for a small content change
while still correctly reflecting structural changes — navigation, blog, or tag updates touch
every affected page, not just one.

Use `--once` to run a single check-and-rebuild and exit, which is handy for a cron job or a
smoke test. The daemon requires the project root to be a git repository.

## Examples

Build using an explicit config file:

```pwsh
netdocs build --config ./appsettings.json
```

Production build with verbose logging:

```pwsh
netdocs build --prod --verbose
```

Serve on a specific port:

```pwsh
netdocs serve --port 8080
```

Run the publish daemon, tracking `main` on `origin` every 15 seconds:

```pwsh
netdocs watch --remote origin --branch main --interval 15
```

## Importing from MkDocs

If you already have a Material for MkDocs project, `netdocs import` converts its
`mkdocs.yml` into an equivalent Netdocs `appsettings.json` so you can migrate without
rewriting configuration by hand:

```pwsh
# Convert ./mkdocs.yml -> ./appsettings.json
netdocs import

# Convert an explicit file to a chosen output path
netdocs import path/to/mkdocs.yml --out appsettings.json

# Overwrite an existing appsettings.json
netdocs import --force
```

The importer maps site metadata, `theme` (name, palette, features, font, icon, logo,
favicon), `nav` (including nested sections), `plugins`, `markdown_extensions`,
`extra_css`, `extra_javascript`, and `extra` into the Netdocs schema. Empty and default
values are omitted. It refuses to overwrite an existing output file unless you pass
`--force`.

!!! note
    Not every MkDocs plugin has a Netdocs equivalent. Review the generated
    `appsettings.json`, remove any plugins Netdocs does not implement, and run
    `netdocs build` to validate. See the [plugins reference](../plugins/index.md) for the
    built-in set.

## Environment variables

Configuration can be overridden with environment variables prefixed `NETDOCS_`
(standard `Microsoft.Extensions.Configuration` binding). Production builds also set
`MKDOCS_PROD_BUILD=true` so prod-only behaviour (like social-card generation) activates.

## Logging

Logging is the standard .NET `Logging` section of `appsettings.json`. Set per-category
levels, for example:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Build": "Debug",
      "Netdocs": "Trace"
    }
  }
}
```

The `--verbose` flag forces `Trace` globally.
