# Netdocs — Master TODO

Deferred / future work, roughly prioritized. Core engine (config, discovery, Markdig
pipeline, plugin host, Scriban theme, build + serve) is implemented and builds the
existing `Web/` blog.

## High priority (parity)
- [ ] **Social cards** (`social` plugin): OG image generation. Choose ImageSharp vs SkiaSharp. Cache by content hash. Prod-only.
- [ ] **Server-side syntax highlighting** parity with Pygments/Material (currently emits `.highlight > pre > code` with a language class only; no token spans/colors). Options: port a highlighter or ship a client-side one styled to match Material.
- [ ] **Search parity**: currently a lightweight client index + custom search JS. Evaluate porting Material's lunr worker + copying its search UI for exact behavior (suggest/highlight/share). Index schema already matches Material.
- [ ] **Real Material CSS/JS assets**: current theme is a functional Material-like stylesheet. Copy Material's compiled bundles for pixel parity (respect license/attribution) or keep the lightweight theme.
- [ ] **Jinja→Scriban override porting**: `docs/overrides/**` (Material Jinja) is ignored. Port `partials/header.html` and any others to Scriban so custom_dir works.

## Markdown extension gaps
- [ ] **Keys** (`++ctrl+c++`) inline extension.
- [ ] **Critic** markup (`{++ ++}`, `{-- --}`, `{~~ ~>~~}`).
- [ ] **Highlight line numbers / anchors / inline linenums** (pymdownx.highlight options: anchor_linenums, line_spans, linenums_style).
- [ ] **Content tabs linked** (`content.tabs.link`) — sync tabs with same label across a page.
- [ ] **Emoji**: map to Twemoji SVG (currently Markdig unicode emoji). Material uses twemoji SVG.
- [ ] **Abbreviations tooltips** styling (`content.tooltips`) and footnote tooltips.

## Plugins
- [ ] **Macros** (`main.py` port): implement `fileuri()` and `ebay()` via Scriban; honor `render_by_default: false` (opt-in per page).
- [ ] **Tags**: dedicated tag listing pages + hierarchical tags (`/` separator) + in-page tag chips + `[TAGS]` marker injection. (Currently: collect, shadow filtering, `tags.json` export.)
- [ ] **Blog**: authors (`.authors.yml`), author pages, `post_excerpt` modes, `categories_allowed` enforcement, drafts, related links, proper excerpt HTML. (Currently: URL rewrite, paginated index, category + archive pages, RSS.)
- [ ] **file-filter**: implement `.file-filter.yml` env-driven label include/exclude + nav pruning. (Currently: pass-through; `.mkdocsignore` honored.)
- [ ] **git-revision-date**: real git created/updated via LibGit2Sharp. (Currently: filesystem timestamps.)
- [ ] **table-reader**: embed CSV/data as tables.
- [ ] **redirects**: client-side redirect map (e.g. `/discord`).
- [ ] **typeset**: smart typography.

## Engine / infra
- [ ] **Incremental build cache**: content-hash sources/templates/plugin inputs; skip unchanged (`.cache/` manifest).
- [ ] **Sitemap.xml** generation (built-in).
- [ ] **External plugin loading** from `./plugins/*.dll` via AssemblyLoadContext.
- [ ] **Navigation**: breadcrumbs (`navigation.path`), section index pages (`navigation.indexes`), prune, active-trail expansion, prev/next.
- [ ] **Instant navigation / prefetch / progress / TOC-follow** client behavior.
- [ ] **Config**: honor all env flags (IS_LOCAL_BUILD, MKDOCS_* etc.), strict mode, `!!python/...` slugify mapping.
- [ ] **Scriban security advisories**: review Scriban 6.2.1 GHSA advisories / pin a patched version; `NuGetAudit` currently disabled to reduce noise.
- [ ] **Unit + golden-file tests**: Markdig extensions, config parser, slugify, search shaping; parity diff vs MkDocs output.
- [ ] **CI**: GitHub Actions build + deploy `site/`.

## Notes
- `.NET 11` (preview) SDK pinned via `global.json`. Solution is `.slnx`.
- Build output centralized under `artifacts/` (UseArtifactsOutput).
