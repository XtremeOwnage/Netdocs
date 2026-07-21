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

## Overrides (`customDir`)

Point `customDir` at a directory of Scriban templates/partials to override the built-in
theme. Files there take precedence over the bundled theme, letting you replace partials
such as `partials/footer.html` or the base `main.html`.

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

