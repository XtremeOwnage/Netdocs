---
title: CLI reference
---

# CLI reference

The `netdocs` executable exposes these commands: `build`, `profile`, `serve`, `watch`, `new`, `import`, and `export`.

```text
netdocs - static site generator

Usage:
  netdocs build [options]     Build the site to the output directory
  netdocs profile [options]   Build once and print a per-phase/plugin timing tree
  netdocs serve [options]     Serve with live reload
  netdocs watch [options]     Publish daemon: poll a git remote and rebuild on push
  netdocs new [path]           Scaffold an annotated appsettings.json
  netdocs import [mkdocs.yml]  Convert an mkdocs.yml to appsettings.json
  netdocs export <file.md>     Render one page to PDF/PNG/WebP (see 'netdocs export --help')
```

## Commands

| Command | Description |
|---|---|
| `netdocs build` | Build the site to the configured output dir (`siteDir`). |
| `netdocs profile` | Build once (cache-bypassed) and print a tree of where build time is spent. |
| `netdocs serve` | Kestrel dev server with file-watch rebuilds + WebSocket live reload. |
| `netdocs watch` | Long-running publish daemon: polls a git remote and rebuilds when the tracked branch advances. |
| `netdocs new` | Scaffold a fully-annotated `appsettings.json` (all common options + doc links). |
| `netdocs import` | Convert an existing `mkdocs.yml` into a Netdocs `appsettings.json`. |
| `netdocs export` | Render a single markdown file as a standalone page to PDF, PNG and/or WebP. |
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

### `netdocs profile`

`netdocs profile` runs a single, cache-bypassed build and prints a tree showing how long each
build phase took, broken down by plugin where a phase runs plugin hooks. Use it to find the
slowest part of a build — a heavy preprocessor, an expensive `OnBuildComplete` hook, or the
markdown/template render itself.

```pwsh
netdocs profile
```

The report lists phases biggest-first. Each line shows the elapsed time and its share of its
parent scope; nested lines are the individual plugins within that phase, and an `xN` suffix
counts how many times a scope ran (for example, a preprocessor invoked once per page):

```text
Build profile (time by phase):

  7. render markdown                        263.4 ms   30.0%
  10. template render                       242.1 ms   27.6%
  12. OnBuildComplete hooks                 142.2 ms   16.2%
    search                                     81.9 ms   57.7%
    social                                     57.6 ms   40.6%
    tags                                        2.4 ms    1.7%
  9. copy assets                             31.5 ms    3.6%
  6. preprocess markdown                     18.0 ms    2.0%
    calculator                                 17.6 ms   98.1% x48
  ...

Total measured: 877.8 ms
```

Because it bypasses the incremental render cache, `profile` always reflects a full cold build —
the numbers are comparable run to run rather than skewed by cache hits.


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

## Exporting a single page

`netdocs export` renders one markdown file on its own, with no navigation, search, header,
table of contents or footer, and writes it as a PDF, a PNG or a WebP image (or several at once).
It uses the same markdown extensions and Material styling as a site build.

```pwsh
# sample.md -> sample.pdf, sample.png and sample.webp next to it, in the dark scheme
netdocs export sample.md -format pdf,png,webp -theme dark

# A high-DPI image of a page in the site, written to a chosen folder
netdocs export docs/setup/install.md --format png --scale 2 -o exports/

# An A4 PDF with an explicit file name
netdocs export notes.md -o notes-a4.pdf --paper a4
```

| Option | Default | Description |
|---|---|---|
| `--format <list>` | `pdf` | Comma-separated output formats: `pdf`, `png`, `webp`. |
| `--theme <name>` | site palette | `light` or `dark`. Without it the site's first palette is used (light when there is no config). |
| `-o, --output <path>` | next to the file | A folder (existing, or ending in `/`) or a file name; each format gets its own extension. |
| `--width <px>` | `1000` | Page width in CSS pixels for the layout and images. |
| `--scale <n>` | `1` | Pixel ratio for PNG/WebP, e.g. `2` for high-DPI images. |
| `--paper <size>` | `letter` | PDF paper size: `letter`, `legal`, `tabloid`, `a3`, `a4`, `a5`. |
| `--browser <path>` | auto-detect | The browser used to render (see below). |
| `-f, --config <path>` | `./appsettings.json` | Site config to take markdown extensions, plugins and palette from. Optional. |

Options can be written with one dash or two (`-theme dark` and `--theme dark` are the same).

**Configuration is optional.** When an `appsettings.json` is found, the export runs that site's
markdown preprocessors (snippets, macros, abbreviations…), Markdig extensions, `extra_css` and
`extra_javascript`, and uses the palette whose scheme matches `--theme`, so a dark export keeps
the site's dark colors. A file inside `docs_dir` resolves images and links from the docs root,
like a site build; any other file resolves them from its own folder. Site-wide features (blog,
tags, search index, social cards) do not apply to a single page.

**Rendering uses a local Chrome, Chromium, Edge or Brave** in headless mode, found in the usual
install locations or on `PATH`. Point Netdocs at a specific browser with `--browser <path>` or the
`NETDOCS_BROWSER` environment variable. The PDF keeps the on-screen look (including the dark
scheme) with the page background running to the paper edge. Images capture the full page height;
a page taller than 16,383 pixels is scaled down to fit, since that is the largest image Chrome and
WebP can encode. Mermaid diagrams, MathJax and code highlighting load from their CDNs as on the
site, so they need network access during the export.

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
