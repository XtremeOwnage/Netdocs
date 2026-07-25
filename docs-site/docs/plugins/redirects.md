---
title: redirects
---

# redirects

Emits tiny client-side redirect pages that forward old URLs to new destinations. Useful
for vanity links and for preserving inbound links after restructuring.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `redirect_maps` | object | — | Inline map of source path → destination URL. |
| `redirect_files` | string \| array | — | One or more JSON files holding bulk redirects. |

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

## Attribution

Behavior is modeled on [mkdocs-redirects](https://github.com/mkdocs/mkdocs-redirects) (MIT). See [Attributions](../about/attributions.md).
