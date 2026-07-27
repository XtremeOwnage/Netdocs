---
title: redirects
---

# redirects

Emits tiny client-side redirect pages that forward old URLs to new destinations. Useful
for vanity links and for preserving inbound links after restructuring.

Redirects can be declared in four ways, from most to least local:

1. **Per-page front matter** (`redirect_from`) — recommended for content that moved or was
   renamed, because the old URLs live next to the page and travel with it.
2. **`redirect_maps`** — a small inline table in the config.
3. **`redirect_files`** — one or more external JSON files for large bulk tables.
4. **`slugify_redirects`** — generated automatically when the site's `slugify` settings change,
   so old slugified URLs keep resolving.

All are merged. Precedence, from lowest to highest: generated `slugify_redirects`, then per-page
`redirect_from`, then explicitly configured `redirect_maps`/`redirect_files`. So an explicit
redirect always overrides an automatically generated one for the same source.

## Per-page redirects (`redirect_from`)

List the old URLs a page replaces in its front matter under `redirect_from` (a single string
or a list). Each becomes a redirect to that page's current URL — so when you rename or move
the page later, its redirects move with it. `aliases` is accepted as a synonym.

```markdown
---
title: LS – How to turn on the alternator
redirect_from:
  - blog/2018/ls--how-to-turn-on-the-alternator/
  - 2018/03/20/ls-how-to-turn-on-the-alternator/
---
```

No plugin options are required for this; just enable the `redirects` plugin.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `redirect_maps` | object | — | Inline map of source path → destination URL. |
| `redirect_files` | string \| array | — | One or more JSON files holding bulk redirects. |
| `slugify_redirects` | object \| array | — | One or more *previous* slugify configs; old URLs are regenerated and redirected to current ones. |

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

## Bulk redirects from JSON file(s)

Inline maps are convenient for a handful of entries, but migration redirect tables can run
to hundreds of rows. Keep those in one or more JSON files and point `redirect_files` at them.
The value may be a single path or an array of paths. Paths resolve against the project root
first, then the docs directory, then as an absolute path.

```json
{
  "name": "redirects",
  "options": {
    "redirect_files": ["redirects/blog.json", "redirects/legacy.json"]
  }
}
```

Each file may use either shape:

**Object map** — source path → destination URL:

```json
{
  "blog/2018/ls--how-to-turn-on-the-alternator/": "/blog/2018/ls-how-to-turn-on-the-alternator/",
  "old/page/": "/new/page/"
}
```

**Array of objects** — `source`/`target` (aliases `from`/`to` and `old`/`new` are also
accepted). An optional `status` field is allowed for documentation/tooling but is ignored
by the client-side redirect (a `meta refresh` cannot set an HTTP status code):

```json
[
  { "source": "old/x/", "target": "/new/x/", "status": 308 },
  { "from": "old/y/", "to": "/new/y/" }
]
```

Files load first, then inline `redirect_maps` — so an inline entry overrides a file entry
with the same source. Sources with a leading slash are normalized relative to the site
directory. A missing or malformed file logs a warning and is skipped rather than failing the
build.

## Automatic redirects on a slugify change (`slugify_redirects`)

URLs for blog posts, categories, authors, tags and (when `slugify.urls` is on) content pages are
derived from the site's `slugify` settings — the casing, word separator and ASCII folding. If you
later change any of those settings, every affected URL changes too, silently breaking existing
inbound links and bookmarks.

`slugify_redirects` fixes that. List the **previous** slugify configuration(s) and, on the next
build, every current URL is re-slugified under each old configuration; wherever the old slug
differs from the current one, a redirect from the old URL to the current URL is generated
automatically. You do not have to enumerate individual pages.

```json
{
  "name": "redirects",
  "options": {
    "slugify_redirects": { "case": "lower", "separator": "_" }
  }
}
```

The example above says "we used to slugify with an underscore separator." After switching the
site's `slugify.separator` to `-`, a post now at `/blog/2018/ls-how-to/` also answers at its old
`/blog/2018/ls_how_to/` URL.

Provide an array to record several historical changes at once:

```json
{
  "name": "redirects",
  "options": {
    "slugify_redirects": [
      { "separator": "_" },
      { "case": "upper", "separator": "-" }
    ]
  }
}
```

Each entry accepts the same keys as the site-level `slugify` block: `case` (`lower` \| `upper` \|
`none`, default `lower`), `separator` (default `-`), and `ascii` (default `false`).

**Notes and limits**

- Only URLs that the **current** slugify config would itself produce are considered, so
  hand-authored, non-slugified paths are left untouched.
- A generated redirect is never written at a path already occupied by a real page.
- Old URLs are reconstructed by re-slugifying the current URL, which captures `separator` and
  `case` changes. A change to `ascii` folding cannot be reconstructed — the dropped characters are
  already gone from the current URL — so handle those with an explicit `redirect_from` on the page.

## Attribution

Behavior is modeled on [mkdocs-redirects](https://github.com/mkdocs/mkdocs-redirects) (MIT). See [Attributions](../about/attributions.md).
