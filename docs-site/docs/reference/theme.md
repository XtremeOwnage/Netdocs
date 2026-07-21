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
| `navigation.indexes` | Section landing pages (`index.md`). |
| `navigation.top` | Back-to-top button. |
| `navigation.footer` | Prev/next footer navigation. |
| `search.suggest` | Search-as-you-type suggestions. |
| `search.highlight` | Highlight matches on the target page. |
| `search.share` | Shareable search-result deep links. |
| `content.code.copy` | Copy-to-clipboard on code blocks. |
| `content.tabs.link` | Link content tabs with the same label across a page. |
| `toc.follow` | Table of contents follows the scroll position. |

## Overrides (`customDir`)

Point `customDir` at a directory of Scriban templates/partials to override the built-in
theme. Files there take precedence over the bundled theme, letting you replace partials
such as `partials/footer.html` or the base `main.html`.

!!! note
    Netdocs templates use **Scriban**, not Jinja2. Material's `overrides/**` Jinja
    templates are auto-ignored; port them to Scriban to re-enable `customDir`.
