# Netdocs

A fast, flexible static site generator in **.NET 11** — a reimplementation of the
[Material for MkDocs](https://squidfunk.github.io/mkdocs-material/) experience with a
first-class C# plugin system. Site configuration lives in **`appsettings.json`**, and
the Markdown in `docs/` builds with little to no change.

> [!IMPORTANT]
> **Netdocs is an AI-generated derivative work** inspired by, and partially reusing
> files from, [MkDocs](https://www.mkdocs.org/) and
> [Material for MkDocs](https://github.com/squidfunk/mkdocs-material). Full credit for
> the theme, its compiled CSS/JS, the lunr-based search, and the overall design goes to
> **Martin Donath ([@squidfunk](https://github.com/squidfunk))** and the Material for
> MkDocs contributors. This project bundles their assets under
> `src/Netdocs.Theme.Material/assets/vendor/material/` and redistributes them unmodified
> under the MIT License — see [`THIRD_PARTY_LICENSES/`](THIRD_PARTY_LICENSES/) and
> [`LICENSE`](LICENSE). If you want a supported, production-grade tool, use Material for
> MkDocs directly and consider [sponsoring it](https://github.com/sponsors/squidfunk).

## Credits & attribution

- **Material for MkDocs** — © 2016-2025 Martin Donath. MIT. https://github.com/squidfunk/mkdocs-material
  Vendored: compiled `main`/`palette` CSS, `bundle.js`, the search worker, and lunr.
- **MkDocs** — the static-site-generator concepts and configuration model this project mirrors.
- **lunr / lunr-languages** — MIT, © Oliver Nightingale and contributors.

See [`src/Netdocs.Theme.Material/assets/vendor/material/NOTICE.md`](src/Netdocs.Theme.Material/assets/vendor/material/NOTICE.md)
for the full list of vendored files and their origins.

## Quick start

```pwsh
# Build a site (looks for ./appsettings.json, or pass --config)
dotnet run --project src/Netdocs.Cli -- build --config ..\Web\appsettings.json

# Serve with live reload
dotnet run --project src/Netdocs.Cli -- serve --config ..\Web\appsettings.json --port 8000
```

In Visual Studio / VS Code, press **F5** — the `Netdocs: serve Web` launch profile builds
and serves the `Web/` site with the debugger attached (a `Netdocs: build Web` profile is
also provided).

## Commands

| Command | Description |
|---|---|
| `netdocs build` | Build the site to the configured output dir (`site_dir`). |
| `netdocs serve` | Kestrel dev server with file-watch rebuilds + WebSocket live reload. |

Options: `--config/-f`, `--port/-p`, `--clean`, `--strict`, `--prod`, `--verbose/-v`.

## Configuration & logging

Site config is the `Netdocs` section of `appsettings.json` (site name, theme, nav,
plugins, markdown extensions, extra). Logging is the standard .NET `Logging` section —
set per-category levels (e.g. `"Build": "Debug"`, `"Netdocs": "Trace"`). The
`--verbose` flag forces Trace globally.

## Architecture

```
Netdocs.slnx
 ├─ src/
 │  ├─ Netdocs.Abstractions/   Plugin contracts + page/site models (depends only on Markdig)
 │  ├─ Netdocs.Core/           Config, discovery, Markdig pipeline, Scriban templating, build engine, dev server pieces
 │  ├─ Netdocs.Plugins/        Built-in plugins (snippets, search, tags, blog, meta, rss, ...)
 │  ├─ Netdocs.Theme.Material/ Scriban templates + theme assets (CSS/JS)
 │  └─ Netdocs.Cli/            `netdocs build|serve`
 └─ tests/
    └─ Netdocs.Core.Tests/
```

### Build pipeline

```
Load appsettings.json (Netdocs section) → SiteConfig
  → Discover docs/** (respect .mkdocsignore + NavigationFilters)
  → Load plugins (config plugins + markdown-extension-backed like snippets)
  → OnBuildStart hooks
  → Content generators (blog index/archive/category, ...)
  → Preprocess markdown (snippets/auto-append, macros)      [IMarkdownPreprocessor]
  → Parse + render (Markdig, per-thread pipeline)           [IMarkdigContributor]  [parallel]
  → Resolve navigation
  → Template render (Scriban + theme)                       [parallel]  → emit HTML + 404
  → OnPageRendered hooks
  → Copy assets (theme + docs static + plugin files)
  → OnBuildComplete hooks (search_index.json, tags.json, rss)
```

## Plugin model

Implement `IPlugin` plus any hook interfaces you need:

- `IMarkdownPreprocessor` — transform raw Markdown (snippets, macros).
- `IMarkdigContributor` — add Markdig extensions.
- `IContentGenerator` — emit virtual pages (blog lists, tags).
- `IBuildHook` — lifecycle (`OnBuildStart` / `OnPageRendered` / `OnBuildComplete`).
- `INavigationFilter` — include/exclude pages.

Plugins are matched to `appsettings.json` plugin names via the CLI's `PluginRegistry`.

## Theming

Templates are [Scriban](https://github.com/scriban/scriban) (`.html`) in
`Netdocs.Theme.Material/templates`. Member access is snake_case (`page.title`,
`config.site_name`). Override templates by pointing `theme.custom_dir` at a folder of
**Scriban** templates. (Material's Jinja2 overrides are detected and ignored — see `TODO.md`.)

## Status

Core engine works end-to-end and builds the existing `Web/` blog (200+ pages in seconds).
See [plan/TODO.md](plan/TODO.md) for parity gaps and roadmap, and [plan/dotnet-ssg-plan.md](plan/dotnet-ssg-plan.md) for the design.
