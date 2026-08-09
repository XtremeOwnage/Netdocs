---
title: social
---

# social

Generates Material-style **Open Graph social cards** for each page using
[ImageSharp](https://github.com/SixLabors/ImageSharp). Cards default to 1200×630 PNG, are
colored from the theme palette, and are referenced via `og:image` / `twitter:image` meta tags.

## Behaviour

- Runs on **build and serve**. Cards are cached by file existence, so a serve session
  only generates missing cards once (on the first build); later incremental rebuilds skip
  them. Large sites that don't want that one-time cost on serve can set
  `enabled_on_serve: false`.
- Background/accent colors are derived from the theme `palette` (`primary` / `accent`)
  unless you override them.
- The `og:image` / `twitter:image` meta tags are only emitted when a card will actually
  exist. Without this plugin (or with `cards: false`) a page has no image tags at all,
  unless it sets one itself — see [per-page overrides](#per-page-overrides).

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `cards` | bool | `true` | Master switch. `false` disables generation and suppresses the image meta tags. |
| `cache` | bool | `true` | Reuse previously generated cards when unchanged. |
| `enabled_on_serve` | bool | `true` | Generate cards during `serve`. Set `false` to skip on serve (build/production still generates). |
| `cards_dir` | string | `assets/social` | Output directory, relative to the site root. |
| `format` | string | `png` | Image format: `png`, `jpeg`, or `webp`. |
| `quality` | int | `90` | Encoder quality (1–100) for `jpeg` / `webp`. Ignored for `png`. |

### `cards_layout_options`

| Option | Type | Default | Description |
|---|---|---|---|
| `width` | int | `1200` | Card width in pixels. |
| `height` | int | `630` | Card height in pixels. |
| `background_color` | color | theme `primary` | Card background. |
| `background_image` | path | – | Full-bleed image drawn behind the text, scaled to cover. Relative to the project root. |
| `color` | color | `whitesmoke` | Title color. |
| `description_color` | color | `#c9ccd1` | Site name + description color. |
| `accent_color` | color | theme `accent` | Color of the bar down the left edge. |
| `accent_width` | int | `12` | Width of that bar in pixels; `0` hides it. |
| `padding` | int | `70` | Space between the card edge and its content. |
| `title_font_size` | int | `58` | Title size in points. |
| `description_font_size` | int | `30` | Description size in points. |
| `site_name_font_size` | int | `28` | Site name size in points. |
| `font_family` | string | – | Preferred installed font family (e.g. `Roboto`). |
| `font_path` | path | – | Explicit `.ttf`/`.otf` file, relative to the project root. Wins over `font_family` and needs nothing installed on the machine — the reliable choice for containers and CI. |
| `logo` | path | – | Image drawn in the top-right corner, relative to the project root. |
| `logo_size` | int | `96` | Logo height (and max width) in pixels. |
| `title` | string | page title | Static title for every card. |
| `description` | string | page/site description | Static description for every card. |
| `description_length` | int | `180` | Truncate the description at this many characters. |

**Colors** accept hex (`#101820`), CSS color functions and names understood by ImageSharp,
and the [Material palette names](../reference/theme.md) used elsewhere in the theme
(`indigo`, `blue-grey`, …). An unparseable value falls back to the default rather than
failing the build — likewise, a `logo`, `background_image` or `font_path` that cannot be
read is logged as a warning and skipped.

### Minimal

```json
{ "name": "social" }
```

### Fully configured

```json
{
  "name": "social",
  "options": {
    "cards": true,
    "cache": true,
    "enabled_on_serve": false,
    "cards_dir": "assets/social",
    "format": "jpeg",
    "quality": 85,
    "cards_layout_options": {
      "width": 1200,
      "height": 630,
      "background_color": "#101820",
      "color": "#ffffff",
      "description_color": "#c9ccd1",
      "accent_color": "amber",
      "accent_width": 20,
      "font_path": "docs/assets/fonts/Inter-Regular.ttf",
      "logo": "docs/assets/logo.png",
      "logo_size": 80,
      "description_length": 140
    }
  }
}
```

## Per-page overrides

A page can set its own card text from front matter, using the same shape as Material:

```yaml
---
social:
  cards_layout_options:
    title: A punchier headline
    description: Shown on the card instead of the page description.
---
```

To skip the generated card entirely for one page and point at an existing image, set
`image` (or `og_image`) in front matter. Relative values are resolved against `site_url`;
absolute URLs are used as-is:

```yaml
---
image: assets/img/launch-banner.png
---
```

!!! note
    A usable font is required. If `font_path` is unset and no system font is found — common
    in minimal containers — card generation is skipped with a warning, and the pages fall
    back to having no image meta tags.

## Attribution

Behavior is modeled on the [Material for MkDocs](https://github.com/squidfunk/mkdocs-material) social-cards plugin (MIT). See [Attributions](../about/attributions.md).
