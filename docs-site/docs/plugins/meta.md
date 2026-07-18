---
title: meta
---

# meta

Applies per-directory front-matter **defaults** from a `.meta.yml` file. Keys defined in a
directory's `.meta.yml` are merged into every page in that directory (and below), unless
the page overrides them in its own front matter.

## Usage

```text
docs/
└─ guides/
   ├─ .meta.yml
   ├─ intro.md
   └─ advanced.md
```

```yaml
# docs/guides/.meta.yml
template: guide.html
tags:
  - Guides
```

Every page under `guides/` inherits `template` and `tags` unless it sets its own.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `meta_file` | string | `".meta.yml"` | Filename to look for in each directory. |

```json
{ "name": "meta", "options": { "meta_file": ".meta.yml" } }
```

## Attribution

Behavior is modeled on the [Material for MkDocs](https://github.com/squidfunk/mkdocs-material) meta plugin (MIT). See [Attributions](../about/attributions.md).
