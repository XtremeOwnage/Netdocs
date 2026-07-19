---
title: macros
---

# macros

A minimal port of [mkdocs-macros] with two example macros that show the pattern you can
extend for your own site: `fileuri` and `button`. Macros are expanded as a Markdown
**preprocessor**, before the page is parsed.

[mkdocs-macros]: https://mkdocs-macros-plugin.readthedocs.io/

## Macros

### `fileuri("name")`

Resolves a file to its published URL, prefixed with `site_url`. A page in the **same
directory** as the current page is preferred; otherwise the first file with a matching
name (any page, then any static asset under the docs directory) wins.

```markdown
See the [installation guide]({{ fileuri("install.md") }}).
```

If the file cannot be found, an HTML comment is emitted in its place so the build does
not fail.

### `button("text", "url")`

Renders a Material-styled call-to-action button (`.md-button`) linking to the given URL.

```markdown
{{ button("Get started", "getting-started/") }}
```

## Controlling where macros run

Mirroring mkdocs-macros, macro expansion is gated:

| Setting | Effect |
|---|---|
| `render_by_default: true` *(default)* | Every page renders macros… |
| front matter `render_macros: false` | …unless a page opts out. |
| front matter `ignore_macros: true` | Skip a page entirely. |
| `render_by_default: false` | Only pages with `render_macros: true` render. |

```json
{ "name": "macros", "options": { "render_by_default": true } }
```

```yaml
---
render_macros: true
---
```

## Enabling the plugin

```json
{ "name": "macros" }
```
