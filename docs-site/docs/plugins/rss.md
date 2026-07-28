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
| `social_icon` | bool | `false` | Add the feed as an RSS icon in the header/footer social row (see [Output](#output)). |
| `social_feed` | string | `rss` | Which feed the social icon links when `social_icon: true` — `rss` or `atom`. |

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
| `description` / `summary` | Rendered HTML of the post intro (content before `<!-- more -->`, or the first paragraph) | `rss_description` |
| `content:encoded` / `content` | Rendered HTML *(only when `full_content: true`)* | — |
| `enclosure` (RSS image) | Front-matter `image`, else the first `<img>` in the post | `image` |
| `guid` (RSS) / `id` (Atom) | `site_url` + post URL (`isPermaLink="true"`) | `rss_guid` |

Relative image paths are resolved against the post URL; a leading `/` is resolved against
`site_url`; absolute (`http(s)://`) URLs are used as-is. Links and images inside the rendered
`description` are absolutized the same way so they resolve in any reader.

### Stable GUIDs across URL changes

The `guid` (RSS) / `id` (Atom) uniquely identifies each item; readers use it to tell new posts
from ones they've already shown. By default it's the post's permalink, so **changing a post's
slug or URL changes its guid**, and readers re-surface it as new. To keep a post's identity
stable across such a move, pin an explicit `rss_guid` in front matter (any stable string — a URN,
a UUID, or the post's original URL). When set, the guid is emitted with `isPermaLink="false"`:

```yaml
rss_guid: https://static.xtremeownage.com/blog/2024/01/04/power-consumption-versus-price/
```

Example front matter using the overrides:

```yaml
---
title: My Post
rss_title: A snappier title just for feed readers
rss_description: A custom one-line summary for the feed.
rss_guid: urn:uuid:1e5f2c9a-0b3d-4a1e-9c2f-8b7a6d5e4c3b
image: hero.png
---
```

## Output

Feeds are written to the **root of your built site** (next to `index.html`), so they are served at
a stable, predictable URL:

| Feed | Default file | Served at | Configure with |
|---|---|---|---|
| RSS 2.0 | `feed_rss_created.xml` | `<site_url>/feed_rss_created.xml` | `rss_file` |
| Atom 1.0 *(when `atom: true`)* | `feed_atom_created.xml` | `<site_url>/feed_atom_created.xml` | `atom_file` |

The RSS feed advertises itself with an `atom:link rel="self"` element so readers can discover the
canonical feed URL.

### Show the feed as a social icon

Set `social_icon: true` and the plugin adds an RSS entry to your `extra.social` row automatically —
no manual link needed. It links `feed_rss_created.xml` by default, or the Atom feed with
`social_feed: atom`:

```json
{ "name": "rss", "options": { "atom": true, "social_icon": true } }
```

To place or style the link yourself instead, add it to `extra.social` by hand:

```json
"extra": {
  "social": [
    { "icon": "fontawesome/solid/rss", "link": "/feed_rss_created.xml" }
  ]
}
```

## Attribution

Behavior is modeled on [mkdocs-rss-plugin](https://github.com/Guts/mkdocs-rss-plugin) by @Guts (MIT). See [Attributions](../about/attributions.md).
