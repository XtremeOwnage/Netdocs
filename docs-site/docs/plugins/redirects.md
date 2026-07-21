---
title: redirects
---

# redirects

Emits tiny client-side redirect pages that forward old URLs to new destinations. Useful
for vanity links and for preserving inbound links after restructuring.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `redirect_maps` | object | — | Map of source path → destination URL. |

```json
{
  "name": "redirects",
  "options": {
    "redirect_maps": {
      "discord": "https://discord.gg/example",
      "old/page": "new/page/"
    }
  }
}
```

Each key becomes an HTML page (e.g. `site/discord/index.html`) that immediately redirects
to the mapped destination. Destinations may be absolute URLs or site-relative paths.
