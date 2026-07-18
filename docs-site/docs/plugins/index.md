---
title: Plugins
---

# Plugins

Netdocs has a first-class C# plugin system. Plugins are enabled and configured in the
`plugins` array of `appsettings.json`, and run in the order listed.

```json
"plugins": [
  { "name": "search", "options": { "lang": "en" } },
  { "name": "tags", "options": { "export": true } },
  { "name": "blog", "options": { "blog_dir": "blog/" } }
]
```

!!! info "Plugins are opt-in"
    Unlike the theme and Markdown extensions, **no plugin runs unless you list it** in the
    `plugins` array. There is no implicit default set — this keeps builds predictable and fast,
    and means a plugin can never surprise you by activating on its own. The trade-off is that a
    fresh `appsettings.json` starts minimal; use the recommended set below as a starting point.

## Recommended configuration

Most sites want the majority of plugins enabled — they are inexpensive and only act on the
content that uses them (for example [table-reader](table-reader.md) is a no-op on pages with no
`read_csv` directive). A good general-purpose starting point:

```json
"plugins": [
  { "name": "search", "options": { "lang": "en" } },
  { "name": "meta" },
  { "name": "tags", "options": { "export": true } },
  { "name": "snippets" },
  { "name": "abbreviations" },
  { "name": "macros" },
  { "name": "table-reader" },
  { "name": "glightbox" },
  { "name": "git-revision-date", "options": { "enabled": true } },
  { "name": "redirects" },
  { "name": "rss" },
  { "name": "social" }
]
```

Add [`blog`](blog.md) if you publish posts, [`file-filter`](file-filter.md) for
environment-driven content gating, and [`link-notes`](link-notes.md) if you want to
annotate outbound links (e.g. affiliate disclosures). A few plugins are intentionally left out of the "enable everything" default:

| Plugin | Why it's opt-in |
|---|---|
| [blog](blog.md) | Only meaningful for sites with a `blog/` posts directory. |
| [social](social.md) | Generates images (slower); best gated behind `netdocs build --prod`. |
| [file-filter](file-filter.md) | Needs a `.file-filter.yml` and env vars to do anything. |
| [affiliate-links](affiliate-links.md) | Requires you to declare your affiliate programs. |
| [arithmatex](arithmatex.md) / [b64](b64.md) | Enable when you actually use math or inline-image embedding. |

Ordering only matters for Markdown preprocessors (they transform source text in sequence);
see each plugin page for its order. Plugins that emit assets or pages are order-independent.

## Disabling a plugin per page

Every configured plugin runs on every page by default. To turn one off (or back on) for a single
page, use its front matter. Two forms are supported — the map form wins, so it can re-enable a
plugin that a list disabled:

```yaml
---
title: Raw template snippet
plugins:
  macros: false        # don't run the macros preprocessor on this page
disable_plugins:
  - table-reader       # list form: turn these off
---
```

This is handy for pages that contain literal `{{ … }}` (which the [macros](macros.md) plugin would
otherwise try to expand), or to skip [table-reader](table-reader.md)/[snippets](snippets.md) on a
page that shouldn't be transformed. Only per-page hooks are gated: Markdown **preprocessors** and
per-page **build hooks** (e.g. search indexing). Global contributions — Markdig extensions and
content generators — are not page-scoped and always apply.

## Built-in plugins

| Plugin | Purpose |
|---|---|
| [search](search.md) | Material-compatible `search_index.json` (page + per-section docs). |
| [social](social.md) | 1200×630 Open Graph social cards. |
| [blog](blog.md) | Posts, paginated index, categories, archive, post metadata. |
| [tags](tags.md) | Hierarchical tag index, shadow tags, `tags.json` export. |
| [meta](meta.md) | Per-directory `.meta.yml` front-matter defaults. |
| [snippets](snippets.md) | `--8<--` file includes. |
| [abbreviations](abbreviations.md) | Shared `<abbr>` definition files. |
| [git-revision-date](git-revision-date.md) | Real created/updated dates from git. |
| [redirects](redirects.md) | Client-side redirect map. |
| [glightbox](glightbox.md) | Image lightbox. |
| [rss](rss.md) | RSS feed of blog posts. |
| [file-filter](file-filter.md) | Env-driven label include/exclude. |
| [macros](macros.md) | `fileuri()` / `button()` Markdown macros (mkdocs-macros subset). |
| [table-reader](table-reader.md) | Expand `read_csv()` / `read_table()` into Markdown tables. |
| [affiliate-links](affiliate-links.md) | Auto tooltip + footer disclosure for affiliate links. |
| [b64](b64.md) | Embed local images as inline `data:` URIs (pymdownx.b64). |
| [arithmatex](arithmatex.md) | LaTeX math typeset with MathJax (pymdownx.arithmatex). |
| [typeset](typeset.md) | Smart typography (curly quotes, en/em dashes, ellipses) via SmartyPants. |

## How plugins hook into the build

A plugin implements `IPlugin` (with `Configure`) plus any of the opt-in hook interfaces
(`IMarkdownPreprocessor`, `IMarkdigContributor`, `IContentGenerator`, `IBuildHook`,
`INavigationFilter`, …). Rather than duplicate that here, see:

- **[Build lifecycle](../development/lifecycle.md)** — a visual diagram of *where* each hook runs in
  the pipeline, so you know which interface to implement for a given job.
- **[Events & callbacks](../development/events-and-callbacks.md)** — the full reference for every
  hook interface, its method signatures, ordering, and when it fires.
- **[External plugins](../development/external-plugins.md)** — how to ship a plugin as a separate
  DLL and load it via config.

See the source under `src/Netdocs.Plugins/` for reference implementations.
