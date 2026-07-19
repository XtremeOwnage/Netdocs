---
title: social
---

# social

Generates Material-style **Open Graph social cards** (1200×630 PNG) for each page using
[ImageSharp](https://github.com/SixLabors/ImageSharp). Cards are colored from the theme
palette and referenced via `og:image` / `twitter:image` meta tags.

## Behaviour

- Runs on **build and serve**. Cards are cached by file existence, so a serve session
  only generates missing cards once (on the first build); later incremental rebuilds skip
  them. Large sites that don't want that one-time cost on serve can set
  `enabled_on_serve: false`.
- Cards are **cached** so warm builds are fast.
- Background/accent colors are derived from the theme `palette` (`primary` / `accent`).

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `cache` | bool | `true` | Reuse previously generated cards when unchanged. |
| `enabled_on_serve` | bool | `true` | Generate cards during `serve`. Set `false` to skip on serve (build/production still generates). |

```json
{ "name": "social", "options": { "cache": true, "enabled_on_serve": true } }
```

!!! note
    A usable system font is required. If none is found, card generation is skipped with a
    warning.

## Attribution

Behavior is modeled on the [Material for MkDocs](https://github.com/squidfunk/mkdocs-material) social-cards plugin (MIT). See [Attributions](../about/attributions.md).
