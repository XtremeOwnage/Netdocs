# Netdocs

A fast, flexible static site generator in **.NET 11** — a reimplementation of the
[Material for MkDocs](https://squidfunk.github.io/mkdocs-material/) experience with a
first-class C# plugin system. It reads existing `mkdocs.yml` projects and builds the
Markdown in `docs/` with little to no change.

## Quick start

```pwsh
# Build a site (looks for mkdocs.yml in the current dir, or pass --config)
dotnet run --project src/Netdocs.Cli -- build --config ..\Web\mkdocs.yml

# Serve with live reload
dotnet run --project src/Netdocs.Cli -- serve --config ..\Web\mkdocs.yml --port 8000
```

## Commands

| Command | Description |
|---|---|
| `netdocs build` | Build the site to the configured output dir (`site_dir`). |
| `netdocs serve` | Kestrel dev server with file-watch rebuilds + WebSocket live reload. |

Options: `--config/-f`, `--port/-p`, `--clean`, `--strict`, `--prod`, `--verbose/-v`.

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
Load mkdocs.yml → SiteConfig
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

Plugins are matched to `mkdocs.yml` plugin names via the CLI's `PluginRegistry`.

## Theming

Templates are [Scriban](https://github.com/scriban/scriban) (`.html`) in
`Netdocs.Theme.Material/templates`. Member access is snake_case (`page.title`,
`config.site_name`). Override templates by pointing `theme.custom_dir` at a folder of
**Scriban** templates. (Material's Jinja2 overrides are detected and ignored — see `TODO.md`.)

## Status

Core engine works end-to-end and builds the existing `Web/` blog (200+ pages in seconds).
See [plan/TODO.md](plan/TODO.md) for parity gaps and roadmap, and [plan/dotnet-ssg-plan.md](plan/dotnet-ssg-plan.md) for the design.
