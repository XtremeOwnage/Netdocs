---
title: rss
---

# rss

Generates an [RSS 2.0](https://www.rssboard.org/rss-specification) feed of blog posts,
ordered by creation date, and optionally an [Atom 1.0](https://validator.w3.org/feed/docs/atom.html)
feed. Requires the [blog](blog.md) plugin to have collected posts.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `length` | int | `20` | Maximum number of items in the feed (alias: `limit`). |
| `rss_file` | string | `feed_rss_created.xml` | Output filename for the RSS feed. |
| `atom` | bool | `false` | Also emit an Atom 1.0 feed. |
| `atom_file` | string | `feed_atom_created.xml` | Output filename for the Atom feed. |
| `full_content` | bool | `false` | Include the full rendered HTML (`content:encoded` / Atom `content`) instead of just the excerpt. |
| `feed_title` | string | `site_name` | Override the feed/channel title. |
| `feed_description` | string | `site_description` | Override the feed/channel description. |
| `image` | string | — | Channel image (logo) URL; relative URLs are resolved against `site_url`. |
| `ttl` | int | — | RSS `<ttl>` in minutes (advisory cache time for readers). |

```json
{ "name": "rss", "options": { "length": 20, "atom": true, "full_content": false } }
```

## Field mapping

Each blog post maps onto feed elements as follows. Per-post front matter overrides win over
the derived defaults:

| Feed element (RSS / Atom) | Source | Per-post override (front matter) |
|---|---|---|
| `title` | Page title (first `# H1` or front-matter `title`) | `rss_title` |
| `link` / `id` | `site_url` + post URL | — |
| `pubDate` / `published`, `updated` | Post creation date | — |
| `category` (RSS) / `category term` (Atom) | Blog `categories` | — |
| `description` / `summary` | Post excerpt | `rss_description` |
| `content:encoded` / `content` | Rendered HTML *(only when `full_content: true`)* | — |
| `enclosure` (RSS image) | Front-matter `image`, else the first `<img>` in the post | `image` |

Relative image paths are resolved against the post URL; a leading `/` is resolved against
`site_url`; absolute (`http(s)://`) URLs are used as-is.

Example front matter using the overrides:

```yaml
---
title: My Post
rss_title: A snappier title just for feed readers
rss_description: A custom one-line summary for the feed.
image: hero.png
---
```

## Output

`site/feed_rss_created.xml` (and `site/feed_atom_created.xml` when `atom: true`) — link them
from your `extra.social` block or `<head>`:

```json
"extra": {
  "social": [
    { "icon": "fontawesome/solid/rss", "link": "/feed_rss_created.xml" }
  ]
}
```

The RSS feed also advertises itself with an `atom:link rel="self"` element so readers can
discover the canonical feed URL.

## Attribution

Behavior is modeled on [mkdocs-rss-plugin](https://github.com/Guts/mkdocs-rss-plugin) by @Guts (MIT). See [Attributions](../about/attributions.md).
