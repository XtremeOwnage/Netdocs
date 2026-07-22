---
title: Theme
---

# Theme

Netdocs ships the **Material** theme — a Scriban port of the Material for MkDocs DOM,
driven by the vendored Material CSS/JS bundle. The theme is configured under
`theme` in `appsettings.json`.

## Theme keys

| Key | Type | Default | Description |
|---|---|---|---|
| `name` | string | `"material"` | Theme name. |
| `language` | string | `"en"` | Content language (`<html lang>`). |
| `logo` | string | — | Path to a logo image. |
| `favicon` | string | — | Path to a favicon. |
| `customDir` | string | — | Override directory for templates/partials (Scriban). |
| `highlight` | string | `"highlightjs"` | Client-side syntax-highlighting renderer for code blocks (see [Code highlighting](#code-highlighting)). |
| `palette` | array | — | Color schemes (see below). |
| `features` | array | — | Enabled Material feature flags. |
| `font` | object | — | `text` and `code` font families. |
| `icon` | object | — | Icon overrides. |

## Palette

```json
"palette": [
  {
    "media": "(prefers-color-scheme: light)",
    "scheme": "default",
    "primary": "indigo",
    "accent": "indigo"
  }
]
```

- `scheme` — `default` (light) or `slate` (dark).
- `primary` / `accent` — Material color names (e.g. `indigo`, `teal`, `grey`, `orange`).
- `media` — optional media query used for automatic light/dark switching.
- `toggle` — optional `{ "icon": "...", "name": "..." }`. When present, the theme renders a
  header switcher button so visitors can flip between schemes (persisted in the browser).

### Light/dark toggle

List two palettes, each with a `toggle`, to render a header switcher. The **first** palette is
the default applied on initial load, so put `slate` first for a dark-by-default site:

```json
"palette": [
  {
    "scheme": "slate",
    "primary": "indigo",
    "accent": "indigo",
    "toggle": { "icon": "material/brightness-4", "name": "Switch to light mode" }
  },
  {
    "scheme": "default",
    "primary": "indigo",
    "accent": "indigo",
    "toggle": { "icon": "material/brightness-7", "name": "Switch to dark mode" }
  }
]
```

Recognized toggle icons are `material/brightness-7` (sun), `material/brightness-4` (moon), and
`material/brightness-auto`. The selected scheme is stored in the browser and restored on the
visitor's next visit. Add `media` queries instead if you prefer the scheme to follow the
operating-system setting automatically.

## Fonts

```json
"font": { "text": "Roboto", "code": "Roboto Mono" }
```

Fonts are loaded from Google Fonts. Set to your preferred families.

## Features

Feature flags mirror Material for MkDocs. Commonly used flags:

| Feature | Effect |
|---|---|
| `navigation.tabs` | Top-level sections become header tabs. |
| `navigation.sections` | Render top-level sections as groups in the sidebar. |
| `navigation.indexes` | Section landing pages (first `index.md`/`README.md` child links the section title). |
| `navigation.path` | Breadcrumb trail above page content. |
| `navigation.top` | Back-to-top button. |
| `navigation.footer` | Prev/next footer navigation (also sequences blog posts by date). |
| `search.suggest` | Search-as-you-type suggestions. |
| `search.highlight` | Highlight matches on the target page. |
| `search.share` | Shareable search-result deep links. |
| `content.code.copy` | Copy-to-clipboard on code blocks. |
| `content.tabs.link` | Link content tabs with the same label across a page. |
| `toc.follow` | Table of contents follows the scroll position. |

## Code highlighting

Code blocks are produced in two independent stages:

1. **Parsing (core, always on).** The Markdig fence parser understands the fence
   syntax — language, `title="…"`, `linenums`, `hl_lines`, the brace/attr-list
   form, `#!lang` inline shebangs, and ` ```mermaid ` — and emits neutral,
   renderer-agnostic HTML: `<pre><code class="language-x" …>`. This never changes.
2. **Highlighting (theme, swappable).** A client-side renderer colourises that
   HTML in the browser. This is what the `highlight` key selects.

| Value | Behaviour |
|---|---|
| `highlightjs` *(default)* | [highlight.js](https://highlightjs.org) via CDN, with line numbers, `hl_lines` row highlighting, and an extended language set (PowerShell, Dockerfile, nginx, HTTP, DNS zones, …). Zero configuration. |
| `none` | No highlighter is injected. Blocks render as plain, monospaced text. Titles (`title="…"`) still render — that styling is part of the core theme, not the highlighter. Line numbers and `hl_lines` are not shown (they are produced by the highlight.js renderer). |
| *(any other value)* | Treated as **`custom`**: Netdocs injects no highlighter and gets out of your way. Wire up your own (Prism, Shiki, a self-hosted highlight.js build, …) through `extra_css` / `extra_javascript`. Your script sees the same `<pre><code class="language-x">` markup. |

```json title="appsettings.json"
{
  "Netdocs": {
    "theme": {
      "name": "material",
      "highlight": "highlightjs"
    }
  }
}
```

### Bringing your own highlighter

Set `highlight` to any value other than `highlightjs`/`none` and load your
renderer as an extra asset:

```json title="appsettings.json"
{
  "Netdocs": {
    "theme": { "highlight": "prism" },
    "extra_css": ["https://cdn.jsdelivr.net/npm/prismjs/themes/prism-tomorrow.css"],
    "extra_javascript": ["https://cdn.jsdelivr.net/npm/prismjs/prism.min.js"]
  }
}
```

Because the core parser already stamps each block with `class="language-x"`, most
highlighters work with no further wiring. If your theme uses instant navigation,
re-run your highlighter on Material's `document$` observable so blocks are
re-processed after each page swap.

## Overrides (`customDir`)

Point `customDir` at a directory of Scriban templates/partials to override the built-in
theme. Files there take precedence over the bundled theme, letting you replace partials
such as `partials/footer.html` or the base `main.html`.

### Overridable templates & partials

The Material theme is deliberately split into small pieces so you can override **just the
part you care about** — for example, replace `partials/social.html` to change how social
links render without copying the whole header. Create a file with the **same path** under
your `customDir` and it wins over the bundled version.

| File | Purpose |
|---|---|
| `main.html` | Base page layout: `<html>`, includes `head`, `header`, nav, content, `footer`, `scripts`. Defines the `render_nav` / `render_toc` helpers. |
| `partials/head.html` | Everything inside `<head>`: meta, Open Graph / Twitter cards, stylesheets, fonts, and the inline `__md_*` helper script. Override to add analytics or verification tags. |
| `partials/header.html` | The single-row header: logo, top-level nav (left), social + palette toggle + search (right). |
| `partials/social.html` | Social links at the right of the header (driven by `extra.social`). |
| `partials/search.html` | Search box + results overlay markup. |
| `partials/nav.html` | Sidebar navigation drawer. |
| `partials/tabs.html` | Header tabs bar (only when `navigation.tabs` is off and you want a second row). |
| `partials/content-actions.html` | Floating Edit / View-source buttons at the top-right of the article (`content.action.edit` / `content.action.view`). |
| `partials/breadcrumbs.html` | Breadcrumb trail above the page title (`navigation.path`, driven by `breadcrumbs`). |
| `partials/source-file.html` | "Last updated / Created" line under the article body (`show_source_meta`, non-post pages). |
| `partials/toc.html` | Right-hand table of contents. |
| `partials/footer.html` | Site footer: prev/next nav, copyright, social links. |
| `partials/post-meta.html` | Blog-post byline: author avatar/name, date, reading time, categories. |
| `partials/blog-nav.html` | Blog sidebar: recent posts / categories / archive listing. |
| `partials/scripts.html` | End-of-body scripts: Material `__config` blob, the vendored bundle, hashed site scripts, inline scripts, and the highlight/mermaid loaders. |
| `partials/highlight.html` | highlight.js CDN loader + init (only injected when `highlight: highlightjs`). |
| `partials/mermaid.html` | Mermaid CDN loader + init for ` ```mermaid ` diagrams. |
| `404.html` | Not-found page layout. |

!!! tip "Blog & tag pages don't have their own template"
    The **blog index** and **tag** pages are produced by the blog/tags plugins as ordinary
    content pages — their body HTML is generated, then rendered through `main.html` like any
    other page. To restyle them, override `main.html` (or the blog partials `post-meta.html`
    / `blog-nav.html`), or target their generated markup via `extra_css`. There is no
    `blog.html` / `tags.html` to copy.

Every bundled partial starts with a `{{ # … }}` comment block documenting the globals it
receives and how to override it. Those Scriban comments produce **no output**, so they never
appear in your HTML (minified or not) — read them straight from the
[theme source](https://github.com/XtremeOwnage/Netdocs/tree/main/src/Netdocs.Theme.Material/templates).


!!! note
    Netdocs templates use **Scriban**, not Jinja2. If any `.html` file under `customDir`
    still contains Jinja constructs (`{% %}`, `lang.t`, `super()`), the whole directory is
    ignored with a warning — port those files to Scriban to re-enable `customDir`.

### Porting a Material override to Scriban

Material's Jinja partials map fairly directly onto Scriban. The common substitutions:

| Jinja | Scriban |
|---|---|
| `{% if x %}…{% endif %}` | `{{ if x }}…{{ end }}` |
| `{% for x in xs %}…{% endfor %}` | `{{ for x in xs }}…{{ end }}` |
| `{% include "partials/p.html" %}` | `{{ include "partials/p.html" }}` |
| `{% set c = "a" %}` | `{{ c = "a" }}` |
| `"feature" in features` | `features | array.contains "feature"` |
| `{{ config.site_name }}` | `{{ config.site_name }}` (unchanged) |

Use the trim markers `{{~` / `~}}` to strip surrounding whitespace, and the theme helpers
(`social_icon`, `strip_slash`, `base_url`) instead of Material's Jinja filters. For example,
an override that renders the configured social links in the header:

```html
{{~ if extra.social ~}}
<div class="header-social">
  {{~ for link in extra.social ~}}
  <a href="{{ link.link }}" class="md-social__link md-header__button md-icon"
     title="{{ link.icon }}" target="_blank" rel="noopener">{{ social_icon link.icon }}</a>
  {{~ end ~}}
</div>
{{~ end ~}}
```

## Social links & icons

Configure the links shown in the header and footer under `extra.social`. Each entry has an
`icon` name (Material/FontAwesome-style, e.g. `fontawesome/brands/github`) and a `link`:

```json
{
  "Netdocs": {
    "extra": {
      "social": [
        { "icon": "fontawesome/brands/github", "link": "https://github.com/you/repo" },
        { "icon": "fontawesome/brands/mastodon", "link": "https://fosstodon.org/@you" },
        { "icon": "material/rss", "link": "feed_rss_created.xml" }
      ]
    }
  }
}
```

The `social_icon` helper maps an icon name to an inline SVG. Built-in glyphs are matched by
substring, so both the Material (`material/github`) and FontAwesome
(`fontawesome/brands/github`) naming styles work:

| Matches | Renders |
|---|---|
| `github` | GitHub |
| `discord` | Discord |
| `reddit` | Reddit |
| `rss` | RSS |
| `x-twitter` / `twitter` | X / Twitter |
| `mastodon` | Mastodon |
| `linkedin` | LinkedIn |
| `youtube` | YouTube |
| `mail` / `email` / `envelope` | Email |
| *(anything else)* | Globe (generic link) |

### Custom icons

Add or override glyphs without editing the theme by supplying `extra.social_icons` — a map
of icon-name substring to an SVG `path` (using a `0 0 24 24` viewBox). Custom entries take
precedence over the built-ins:

```json
{
  "Netdocs": {
    "extra": {
      "social_icons": {
        "bluesky": "M12 10.8C10.7 8.3 7.2 3.6 3.9 ... Z"
      },
      "social": [
        { "icon": "fontawesome/brands/bluesky", "link": "https://bsky.app/profile/you" }
      ]
    }
  }
}
```

## Icons

Nav items can show a small glyph before their label — the same "reference" look as
mkdocs-material. Set an icon in **either** place:

- **Front matter** on a page (mkdocs-material compatible):

    ```yaml
    ---
    title: Code blocks
    icon: code-braces
    ---
    ```

- **Nav config** (`icon` on any nav entry, including sections):

    ```json
    "nav": [
      { "title": "Setup", "icon": "rocket-launch", "children": [ /* … */ ] },
      { "path": "reference/code-blocks.md", "icon": "code-braces" }
    ]
    ```

An icon name is resolved in this order: a custom `extra.social_icons` override → a curated
Material glyph → a brand glyph (`social_icon`) → a literal emoji/short string rendered as text
(so `icon: "🚀"` works too). Fully-qualified names like `material/rocket-launch` also match —
only the last path segment is used.

### Curated icon names

| Category | Names |
|---|---|
| Callouts | `alert`, `alert-circle`, `information`, `plus-circle` |
| Content | `code-braces`, `tab`, `table`, `format-size`, `grid`, `image`, `format-list-bulleted`, `sigma`, `math-integral`, `tooltip-text` |
| Diagrams | `sitemap`, `graph` |
| Sections | `book`, `book-open-variant`, `puzzle`, `cog`, `cog-outline`, `rocket`, `rocket-launch`, `home`, `flask`, `heart` |
| Files & actions | `file-document`, `cloud-upload`, `download`, `content-save`, `magnify`, `tag`, `shield-check` |
| Fun | `emoticon-happy`, `cursor-default-click` |

Need one that isn't listed? Add it via [`extra.social_icons`](#custom-icons) — the override map
feeds nav icons and [badges](../plugins/macros.md) too.

