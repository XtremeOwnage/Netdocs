---
title: tags
---

# tags

Collects page `tags` from front matter, renders a hierarchical tag index into a marker,
optionally hides shadow tags in production, and exports a `tags.json`.

## Usage

Add tags to a page's front matter:

```yaml
---
tags:
  - Networking
  - Networking/VLANs
tags_title: A friendlier name on the tags page
---
```

Then place the marker on your tags page (e.g. `docs/tags.md`):

```markdown
# Tags

<!-- material/tags -->
```

Nested tags separated by `/` nest under their parent, producing a hierarchical index.

Each tag heading is given an explicit anchor id matching MkDocs Material's tags plugin: the literal
prefix `tag:` followed by each `/`-separated segment slugified on its own and rejoined with `/`
(for example `Development/C#` becomes `#tag:development/c`). This keeps deep links to a tag identical
between Netdocs and Material rather than collapsing into an auto-generated id (`#developmentc`).

### Scoped listings

A marker can carry an optional scope object to list only some tags. This is handy when you want
a dedicated page per top-level category:

```markdown
<!-- material/tags { include: [Application] } -->
<!-- material/tags { exclude: [Draft, Internal] } -->
```

- **`include`** — list only tags matching one of these entries. When omitted, every tag is listed.
- **`exclude`** — drop tags matching one of these entries.

Matching is **prefix-aware**: an entry like `Application` selects the `Application` tag *and* every
nested child (`Application/AWS`, `Application/ActiveDirectory`, …). Each marker is rendered
independently, so several scoped listings can coexist across pages. If a scope matches no tags the
section renders empty and the build logs a warning naming how many markers came up blank.

### Custom display name

Use the front-matter **`tags_title`** override to control how a page appears in the tag
list (and in `tags.json`). Without it, the page's title (or first `H1`, or filename) is
used.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `export` | bool | `true` | Write a `tags.json` export. |
| `export_file` | string | `"tags.json"` | Export filename. |
| `shadow` | bool | `false` | Enable shadow-tag hiding. |
| `shadow_on_serve` | bool | `true` | Keep shadow tags visible during `serve`. |
| `shadow_tags_prefix` | string | `"_"` | Prefix that marks a tag as shadow. |
| `shadow_tags` | array | — | Explicit shadow-tag names. |

```json
{
  "name": "tags",
  "options": {
    "export": true,
    "shadow": true,
    "shadow_tags": [ "Draft", "Internal" ]
  }
}
```

## Output

`site/tags.json` — a map of tag → `[{ title, url }]`.

## Attribution

Behavior is modeled on the [Material for MkDocs](https://github.com/squidfunk/mkdocs-material) tags plugin (MIT). See [Attributions](../about/attributions.md).
