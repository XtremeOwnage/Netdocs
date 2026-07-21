---
title: CLI reference
---

# CLI reference

The `netdocs` executable exposes three commands: `build`, `serve`, and `watch`.

```text
netdocs - static site generator

Usage:
  netdocs build [options]     Build the site to the output directory
  netdocs serve [options]     Serve with live reload
  netdocs watch [options]     Publish daemon: poll a git remote and rebuild on push
```

## Commands

| Command | Description |
|---|---|
| `netdocs build` | Build the site to the configured output dir (`siteDir`). |
| `netdocs serve` | Kestrel dev server with file-watch rebuilds + WebSocket live reload. |
| `netdocs watch` | Long-running publish daemon: polls a git remote and rebuilds when the tracked branch advances. |
| `netdocs --help` | Print usage. |

If no command is given, `build` is assumed.

## Options

| Option | Alias | Description |
|---|---|---|
| `--config <path>` | `-f` | Path to `appsettings.json` (default `./appsettings.json`). |
| `--port <port>` | `-p` | Dev server port for `serve` (default `8000`). |
| `--clean` | | Remove existing output before building. |
| `--no-cache` | | Ignore the incremental render cache and re-render every page. |
| `--strict` | | Fail on plugin/template errors. |
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
