---
title: Configuration
---

# Configuration

Netdocs reads its configuration from the **`Netdocs`** section of `appsettings.json`.
Values map to the MkDocs configuration model, so migrating a `mkdocs.yml` is mostly a
matter of translating keys to JSON.

## Top-level keys

| Key | Type | Default | Description |
|---|---|---|---|
| `siteName` | string | `"Documentation"` | Site title, shown in the header and `<title>`. |
| `siteUrl` | string | — | Canonical base URL (used for sitemap, OG tags, RSS). |
| `siteAuthor` | string | — | Author meta tag. |
| `siteDescription` | string | — | Default meta description. |
| `copyright` | string | — | Footer copyright line. |
| `repoUrl` | string | — | Source repository URL. |
| `repoName` | string | — | Display name for the repo link. |
| `docsDir` | string | `"docs"` | Source content directory. |
| `siteDir` | string | `"site"` | Output directory. |
| `theme` | object | — | [Theme configuration](theme.md). |
| `nav` | array | — | Ordered navigation tree. |
| `plugins` | array | — | Enabled [plugins](../plugins/index.md) and their options. |
| `markdownExtensions` | array | — | [Markdown extensions](markdown-extensions.md) and options. |
| `extraCss` | array | — | Extra stylesheet hrefs injected into every page. |
| `extraJavaScript` | array | — | Extra script srcs injected before `</body>`. |
| `exclude` | array | — | Glob patterns (docs-relative) to exclude from discovery. |
| `extra` | object | — | Arbitrary data exposed to templates (e.g. `social` links). |

## Navigation

`nav` is an ordered list of entries. An entry is either a **link** (has `path`) or a
**section** (has `children`):

```json
"nav": [
  { "title": "Home", "path": "index.md" },
  {
    "title": "Guides",
    "children": [
      { "path": "guides/install.md" },
      { "title": "Advanced", "path": "guides/advanced.md" }
    ]
  }
]
```

- `title` is optional for a link — when omitted, the page's first heading (or
  front-matter `title`) is used.
- Paths are relative to `docsDir`.

## Plugins

Each plugin entry has a `name` and an optional `options` map:

```json
"plugins": [
  { "name": "search", "options": { "lang": "en" } },
  { "name": "blog", "options": { "blog_dir": "blog/" } }
]
```

Order matters — plugins run in the order listed. See the
[plugin catalogue](../plugins/index.md) for each plugin's options.

## Markdown extensions

```json
"markdownExtensions": [
  { "name": "admonition" },
  { "name": "toc", "options": { "permalink": true } },
  { "name": "pymdownx.superfences" }
]
```

See [Markdown extensions](markdown-extensions.md) for the supported set.

## Exclusions

Two mechanisms exclude content from a build:

- **`exclude`** — glob patterns in `appsettings.json` (e.g. `"_include/**"`).
- **`.mkdocsignore`** — a gitignore-style file honored during content discovery.

## Slugify

Generated URLs — blog post slugs, category pages, author pages, and tag pages — are
produced by a configurable slugifier. Add a `slugify` block to control it:

```json
{
  "Netdocs": {
    "slugify": {
      "case": "lower",
      "separator": "-",
      "ascii": false
    }
  }
}
```

| Option | Default | Description |
| --- | --- | --- |
| `case` | `lower` | Letter casing: `lower`, `upper`, or `none` (preserve). |
| `separator` | `-` | String inserted between words (e.g. `_` or `.`). |
| `ascii` | `false` | When `true`, drop non-ASCII letters/digits instead of keeping them. |

With the defaults, `"Hello World!"` becomes `hello-world`. Set `"separator": "_"` to get
`hello_world`, or `"case": "none"` to keep the original casing.

## Environment overrides

Any key can be overridden with an environment variable prefixed `NETDOCS_`, using `__`
as the section separator — for example `NETDOCS_Netdocs__siteName=Staging`.
