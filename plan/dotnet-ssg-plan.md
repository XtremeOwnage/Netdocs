Ok, here is your porject plan.


# Plan: .NET Static Site Generator ("XoSsg")

A from-scratch static site generator in **.NET 10** that builds this blog with full
feature parity to the current MkDocs (Material) setup, a first-class **plugin
system**, and a **parallel, incremental** build for speed. Frontend (CSS/JS/templates)
is **adapted from Material for MkDocs**, not rebuilt from scratch.

> Status: PLAN ONLY. Implementation happens on a dedicated branch.

---

## 1. Goals & non-goals

### Goals
- **Author experience unchanged.** All existing Markdown in `docs/` builds without
  edits: admonitions (`!!! note`), content tabs (`=== "Tab"`), snippets (`--8<---`),
  attribute lists (`{target=_blank}`), footnotes, abbreviations, keys, critic,
  caret/mark/tilde, task lists, tables, emoji, Mermaid.
- **Plugin system.** Every non-core capability (abbreviations, blog, tags, rss,
  social cards, git dates, redirects, sitemap, snippets) is a plugin implementing
  well-defined hooks. Third-party/local plugins load via DI + assembly scanning.
- **Retain search behavior exactly.** Reuse Material's client-side search
  (lunr-based worker + UI). We emit a `search_index.json` in the **same schema**
  Material's search JS consumes, so the UX is identical.
- **Fast builds.** Parallel page rendering (TPL Dataflow), content-hash incremental
  cache, target seconds (not minutes) for ~2000 pages.
- **Config compatibility.** Read the existing `mkdocs.yml` (YamlDotNet) so we do not
  re-author configuration. Unknown keys map to plugin options or are ignored with a
  warning.

### Non-goals (initial release)
- Perfect byte-for-byte parity with every Material feature flag.
- Python macro parity beyond porting the few macros in `main.py`.
- MkDocs plugin binary compatibility (we re-implement behavior, not the plugin API).

---

## 2. Current-site feature inventory (must support)

Derived from `mkdocs.yml`, `main.py`, `.file-filter.yml`, `.mkdocsignore`.

### Markdown extensions (Markdig + custom)
| Feature | Current (pymdownx / markdown) | .NET plan |
|---|---|---|
| Admonitions | `admonition`, `pymdownx.details` | Custom Markdig extension (`!!!`, `???`) |
| Content tabs | `pymdownx.tabbed` (linked) | Custom Markdig extension (`===`) |
| Snippets/includes | `pymdownx.snippets` (`--8<---`, base_path, auto_append) | Custom preprocessor plugin |
| Attribute lists | `attr_list` | Markdig `GenericAttributes` |
| Abbreviations | `abbr` + auto-appended `abbv.md` | Abbreviations plugin (preprocess) |
| Footnotes | `footnotes` | Markdig `Footnotes` |
| Code highlight + line anchors | `pymdownx.highlight` (anchor_linenums, inline linenums) | ColorCode/Prism + custom renderer |
| Inline highlight | `pymdownx.inlinehilite` | Markdig inline code + highlighter |
| Superfences + Mermaid | `pymdownx.superfences` | Markdig custom fenced container -> `<pre class="mermaid">` |
| Keys | `pymdownx.keys` | Custom inline extension (`++ctrl+c++`) |
| Critic / caret / mark / tilde | `pymdownx.*` | Custom extensions |
| Tasklist | `pymdownx.tasklist` | Markdig `TaskLists` |
| Emoji | `pymdownx.emoji` (twemoji) | Emoji extension + twemoji SVG map |
| Tables | `tables` | Markdig `PipeTables` |
| TOC + permalinks | `toc` (permalink) | Markdig `AutoIdentifiers` + TOC builder |
| Tooltips | `content.tooltips`, footnote tooltips | CSS/JS from Material |

### Site / theme features
- Material theme look: palette (light, grey/orange accent), Open Sans font, logo.
- Navigation: tabs (sticky), sections, path/breadcrumbs, prune, indexes, top button,
  instant nav + prefetch + progress, tracking, TOC follow.
- Header (custom `overrides/partials/header.html`), footer nav, social links, copyright.
- Content actions: view/edit source, code copy/select/annotate, linked content tabs.
- 404 page.

### Content plugins
- **Blog**: `blog_dir`, post date/url formats, excerpts, categories (allow-list),
  archive by year, pagination (20/page), authors (`.authors.yml`).
- **Tags**: hierarchical (`/` separator), tag pages, slugify rules, JSON export
  (`tags.json`), shadow tags (Draft/Internal/ToDo) hidden in prod, shown on serve.
- **Search**: Material client search — **retain exactly** (index schema + worker + UI).
- **RSS**: created-date feed, category mapping, prod-only.
- **Social cards**: OG images per page, prod-only.
- **Git revision dates**: created + last-updated per page.
- **Redirects**: (currently commented, e.g. `/discord`) — support map.
- **file-filter / .mkdocsignore**: env-driven include/exclude, label-based
  draft/internal/alpha filtering, nav filtering.
- **table-reader**: embed CSV/data as tables.
- **glightbox**: image lightbox (JS include + wrapper).
- **meta**: per-directory `.meta.yml` front-matter defaults.
- **macros** (`main.py`): `fileuri()` and friends — port to Scriban functions.

### Environment / build flags (from CI)
`IS_LOCAL_BUILD`, `MKDOCS_PROD_BUILD`, `MKDOCS_FILE_FILTER`, `MKDOCS_CI_BUILD`,
`MKDOCS_INCLUDE_SHADOW_TAGS`, `MKDOCS_MACROS_ENABLED`, `MKDOCS_NAV_PRUNE`,
`SITE_NAME`, `REPO_NAME`, `REPO_URL`. Map to build options/env in the new engine.

---

## 3. Architecture

### Solution layout
```
XoSsg.sln
 ├─ src/
 │  ├─ XoSsg.Abstractions/     # Plugin interfaces, context, page model (no deps)
 │  ├─ XoSsg.Core/            # Build engine, Markdig pipeline, config, dev server
 │  ├─ XoSsg.Cli/             # `xossg build|serve|new` (System.CommandLine)
 │  ├─ XoSsg.Theme.Material/  # Ported templates + copied Material CSS/JS assets
 │  └─ XoSsg.Plugins.*/       # One project (or folder) per built-in plugin
 └─ tests/
    ├─ XoSsg.Core.Tests/
    └─ XoSsg.Plugins.Tests/
```

### Core libraries
- **Markdig** — Markdown parsing + custom extensions.
- **Scriban** — templating (closest to Jinja; hosts ported Material templates + macros).
- **YamlDotNet** — `mkdocs.yml`, front-matter, `.authors.yml`, `.meta.yml`.
- **System.CommandLine** — CLI.
- **LibGit2Sharp** — git dates/authors (git-revision plugin).
- **SixLabors.ImageSharp** (or SkiaSharp) — social cards.
- **Microsoft.Extensions.DependencyInjection / Hosting / Logging** — DI + plugin host.
- **System.Threading.Tasks.Dataflow** — parallel render pipeline.
- Dev server: **Kestrel** minimal API + WebSocket for live reload; **FileSystemWatcher**.

### Build pipeline (stages, each plugin-extensible)
```
Load config (mkdocs.yml -> SiteConfig)
  → Discover content (docs/**, respect ignore/filters)
  → Load plugins (built-in + local ./plugins/*.dll) via DI
  → OnBuildStart hooks
  → Preprocess Markdown (abbreviations, snippets, macros)   [IMarkdownPreprocessor]
  → Parse + render (Markdig pipeline w/ contributed exts)   [IMarkdigContributor]  [PARALLEL]
  → Content generation (blog lists, tag pages, archives)    [IContentGenerator]
  → Template render (Scriban + Material theme)              [PARALLEL]
  → OnPageRendered hooks (collect search docs, headings)    [IBuildHook]
  → OnBuildComplete hooks (search_index.json, rss, sitemap, social cards, redirects)
  → Emit ./site (incremental: skip unchanged by content hash)
```

---

## 4. Plugin system (design)

`XoSsg.Abstractions` (dependency-free) defines the contract. Plugins implement only
the hooks they need. Discovery: built-ins registered in DI; local plugins loaded from
`./plugins/*.dll` via `AssemblyLoadContext` + attribute scan.

```csharp
public interface IPlugin
{
    string Name { get; }
    void Configure(IPluginContext ctx);   // register options, DI services, assets
}

public interface IMarkdownPreprocessor      // raw md -> md (abbr, snippets, macros)
{ Task<string> ProcessAsync(PageContext page, string markdown, CancellationToken ct); }

public interface IMarkdigContributor         // add Markdig extensions
{ void Extend(MarkdownPipelineBuilder builder); }

public interface IContentGenerator           // create virtual pages (blog, tags)
{ IAsyncEnumerable<GeneratedPage> GenerateAsync(SiteContext site, CancellationToken ct); }

public interface IBuildHook                  // lifecycle
{
    Task OnBuildStartAsync(SiteContext site, CancellationToken ct);
    Task OnPageRenderedAsync(PageContext page, CancellationToken ct);
    Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct);
}

public interface INavigationFilter           // file-filter / shadow tags / prune
{ bool ShouldInclude(PageContext page, BuildContext build); }
```

`IPluginContext` exposes: config access, path resolution, asset registration
(inject CSS/JS into `<head>`), logging, DI service registration, options binding.

### Built-in plugins (map 1:1 to current features)
| Plugin | Hooks used | Notes |
|---|---|---|
| `Abbreviations` | Preprocessor | Auto-append `docs/_include/abbv.md`; `*[ABBR]: def` |
| `Snippets` | Preprocessor | `--8<---` includes, base paths, sections |
| `Macros` | Preprocessor (Scriban) | Port `fileuri()` etc. from `main.py` |
| `Blog` | ContentGenerator + BuildHook | Posts, categories, archive, pagination, authors, excerpts |
| `Tags` | ContentGenerator + BuildHook | Hierarchical, tag pages, `tags.json`, shadow tags |
| `Search` | BuildHook | Emit Material-schema `search_index.json` (see §5) |
| `Rss` | BuildHook | created-date feed, category map, prod-only |
| `SocialCards` | BuildHook | ImageSharp OG images, prod-only, cached |
| `GitRevisionDate` | BuildHook | LibGit2Sharp created/updated |
| `Redirects` | BuildHook | client-side redirect html map |
| `Sitemap` | BuildHook | `sitemap.xml` |
| `FileFilter` | NavigationFilter | env flags, `.mkdocsignore`, label include/exclude |
| `TableReader` | Preprocessor/Markdig | CSV → table |
| `Meta` | Config/discovery | per-dir `.meta.yml` defaults |
| `Glightbox` | Asset injection + Markdig | wrap images, include JS |

---

## 5. Search — retain exactly

**Requirement:** keep the current search UX. Strategy:

1. **Reuse Material's search frontend as-is** — copy `search.js` worker + the search
   UI partial/markup from Material into `XoSsg.Theme.Material`. No behavior change.
2. **Emit an identical index.** Material's search worker loads `search/search_index.json`
   with shape:
   ```json
   {
     "config": { "lang": ["en"], "separator": "...", "pipeline": ["stemmer", ...] },
     "docs": [
       { "location": "path/", "title": "Page title", "text": "plain text...", "tags": ["..."] }
     ]
   }
   ```
   The `Search` plugin collects, per rendered page, `location`, `title`, section
   `text` (HTML stripped to text, split on headings like Material does), and `tags`,
   then writes `site/search/search_index.json` in this schema.
3. **Validate** against a current MkDocs build's `search_index.json` (diff schema +
   spot-check queries in the browser) to guarantee parity.
4. Keep `search.suggest`, `search.highlight`, `search.share` — all client-side in the
   copied JS; nothing to reimplement server-side.

Result: same search box, same suggestions/highlighting/sharing, powered by our index.

---

## 6. Theme strategy (adapt Material)

- **Copy** Material's compiled CSS + JS bundles into `XoSsg.Theme.Material/assets`
  (respect license/attribution). Keep `docs/stylesheets/extra.css` working as-is.
- **Port templates** we actually use from Jinja2 → Scriban: `base`, `main`, nav
  (tabs/sections/breadcrumbs), toc, `blog` post + list, tag pages, 404, and the
  existing `overrides/partials/header.html`. Only the subset in use, not all of Material.
- Instant nav / prefetch / progress / TOC-follow / code-copy: provided by the copied
  Material JS — we emit compatible markup + `data-md-*` attributes so it "just works."
- Provide a template-override mechanism (`docs/overrides/**`) mirroring current setup.

**Risk:** Jinja→Scriban porting is the main manual effort; scope tightly to used templates.

---

## 7. Config mapping (`mkdocs.yml` → SiteConfig)

- Parse with YamlDotNet into a `SiteConfig` (site_name/url/description, theme, nav,
  markdown_extensions, plugins, extra, extra_css).
- `!ENV [VAR, default]` → resolve from environment (implement a small YAML tag handler).
- `plugins:` list → activate matching built-in plugins + bind their options.
- Python-specific tags (`!!python/...`) → recognized + mapped to built-in behavior
  (slugify rules become C# equivalents) or ignored with a warning.
- Unknown keys → warning, non-fatal (strict mode optional).

---

## 8. CLI & dev server

- `xossg build [--clean] [--strict] [--prod]` → parallel build to `./site`.
- `xossg serve [--port 8000]` → Kestrel static host + FileSystemWatcher +
  WebSocket live-reload; incremental rebuild of changed pages only.
- `xossg new <path>` → scaffold (optional, later).
- Honor existing env flags (§2) for prod/CI behavior.

---

## 9. Performance plan

- **Parallelize** parse + render with TPL Dataflow (bounded by CPU count).
- **Incremental cache**: content-hash each source + template + plugin-input; skip
  emit when unchanged (persist manifest under `.cache/`).
- **Reuse one Markdig pipeline** instance (thread-safe render).
- **Lazy/parallel** social cards + git lookups; cache social cards by content hash.
- Target: full cold build in seconds; warm/incremental near-instant per page.

---

## 10. Testing & validation

- **Unit**: each Markdig extension (admonition, tabs, snippets, keys, critic…),
  config parser, slugify, search-index shaping.
- **Golden-file**: render representative pages; compare HTML to expected.
- **Parity checks**: build the same `docs/` with MkDocs and XoSsg; diff
  `search_index.json` schema, sitemap, RSS, and spot-check rendered pages.
- **Perf test**: measure full-build time vs current ~5 min baseline.

---

## 11. Milestones (incremental, each independently useful)

1. **M1 — Skeleton**: solution, `Abstractions`, `Core` with config load + content
   discovery + Markdig basic pipeline + emit raw HTML. CLI `build`.
2. **M2 — Markdown parity**: custom extensions (admonitions, tabs, snippets, keys,
   critic, emoji, highlight, mermaid). Golden-file tests. `Abbreviations` +
   `Snippets` + `Macros` plugins.
3. **M3 — Theme**: copy Material assets, port base/nav/toc templates via Scriban,
   render a real page that looks like today. `extra.css` honored.
4. **M4 — Plugin host**: DI + local plugin loading; formalize hooks; `FileFilter`,
   `Meta`, `Redirects`, `Sitemap`.
5. **M5 — Search (retain)**: `Search` plugin emits Material-schema index; wire copied
   search JS; validate parity in browser.
6. **M6 — Blog + Tags**: categories, archive, pagination, authors, excerpts;
   hierarchical tags + `tags.json` + shadow tags.
7. **M7 — Content extras**: `Rss`, `SocialCards`, `GitRevisionDate`, `TableReader`,
   `Glightbox`.
8. **M8 — Dev server + incremental**: Kestrel + live reload + content-hash cache.
9. **M9 — Perf + parity pass**: parallelize, benchmark vs MkDocs, fix diffs.
10. **M10 — CI**: GitHub Actions build + deploy `site/` to `gh-pages` (replace current).

---

## 12. Open questions (decide before/early in build)
- **Templating**: Scriban (chosen — closest to Jinja) vs Razor. Confirm.
- **Images**: ImageSharp (managed, license) vs SkiaSharp (native). Confirm for social cards.
- **Plugin loading**: in-tree built-ins only at first, or support external `./plugins/*.dll`
  from M4? (Plan assumes external supported at M4.)
- **Namespace/product name**: placeholder `XoSsg` — rename before scaffolding.
- **Search**: confirm we pin to current Material search JS version to guarantee index-schema match.

---

## 13. Risks
- **Jinja→Scriban template port** is the biggest manual effort → mitigate by scoping to
  only used templates.
- **Search index schema drift** across Material versions → pin the JS version we copy.
- **Custom Markdig extensions** for niche pymdownx syntax (linked tabs, snippet
  sections, inline line numbers) → allocate test coverage; treat as M2 core work.
- **Ongoing maintenance** ownership (vs Zensical, maintained by Material's team) →
  accepted trade for speed + full control + C# plugin system.




------------

Important Functionality / Plugins:

- Blog: https://squidfunk.github.io/mkdocs-material/plugins/blog/
- Search: https://squidfunk.github.io/mkdocs-material/plugins/search/
    - EXTREMELY important that search works as good.
- Social: https://squidfunk.github.io/mkdocs-material/plugins/social/
- Tags: https://squidfunk.github.io/mkdocs-material/plugins/tags/
- Meta: https://squidfunk.github.io/mkdocs-material/plugins/meta/


It Must be fast. It MUST be flexible. I should be able to customize the theme, without having to edit the source code. This can be done through templates.