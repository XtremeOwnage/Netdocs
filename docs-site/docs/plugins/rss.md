---
title: rss
---

# rss

Generates an RSS feed of blog posts, ordered by creation date. Requires the
[blog](blog.md) plugin to have collected posts.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `length` | int | `20` | Maximum number of items in the feed. |

```json
{ "name": "rss", "options": { "length": 20 } }
```

## Output

`site/feed_rss_created.xml` — link it from your `extra.social` block or `<head>`:

```json
"extra": {
  "social": [
    { "icon": "fontawesome/solid/rss", "link": "/feed_rss_created.xml" }
  ]
}
```

## Attribution

Behavior is modeled on [mkdocs-rss-plugin](https://github.com/Guts/mkdocs-rss-plugin) by @Guts (MIT). See [Attributions](../about/attributions.md).
