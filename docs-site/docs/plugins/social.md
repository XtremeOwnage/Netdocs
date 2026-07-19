---
title: social
---

# social

Generates Material-style **Open Graph social cards** (1200×630 PNG) for each page using
[ImageSharp](https://github.com/SixLabors/ImageSharp). Cards are colored from the theme
palette and referenced via `og:image` / `twitter:image` meta tags.

## Behaviour

- Runs on **production/build only** — skipped during `serve` because generation is
  expensive.
- Cards are **cached** so warm builds are fast.
- Background/accent colors are derived from the theme `palette` (`primary` / `accent`).

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `cache` | bool | `true` | Reuse previously generated cards when unchanged. |

```json
{ "name": "social", "options": { "cache": true } }
```

!!! note
    A usable system font is required. If none is found, card generation is skipped with a
    warning.
