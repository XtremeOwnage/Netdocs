---
title: Markdown extensions
---

# Markdown extensions

Netdocs renders Markdown with **[Markdig](https://github.com/xoofx/markdig)**, configured
to closely match the Material for MkDocs / Python-Markdown feature set. Many extensions
are always on; others are enabled via `markdownExtensions` in `appsettings.json`.

## Always-on extras

The shared pipeline enables these by default:

| Feature | Syntax | Notes |
|---|---|---|
| YAML front matter | `--- ... ---` | Parsed into page metadata. |
| Pipe tables | `\| a \| b \|` | GitHub-style tables. |
| Grid tables | ASCII grid | Multi-line cells. |
| Emphasis extras | `~~del~~`, `~sub~`, `^sup^`, `==mark==` | |
| Task lists | `- [ ]` / `- [x]` | |
| Footnotes | `[^1]` | |
| Auto identifiers | — | Heading anchors for the ToC. |
| Generic attributes | `{ .class #id target=_blank }` | `attr_list` equivalent. |
| Abbreviations | `*[HTML]: HyperText…` | `<abbr>` tooltips. |
| Definition lists | `Term\n: Definition` | |
| Autolinks | bare URLs | |
| Media links | image/video embeds | |
| Mathematics | `$…$`, `$$…$$` | |

## Built-in block extensions

| Extension | Syntax |
|---|---|
| Admonitions | `!!! note "Title"` and collapsible `???` / `???+` |
| Content tabs | `=== "Tab"` |
| Material code blocks | fenced code with highlight + line numbers |

### Admonitions

```markdown
!!! note "Heads up"
    Body text here.

???+ tip "Open by default"
    Collapsible content.
```

### Content tabs

```markdown
=== "Python"
    ```python
    print("hi")
    ```

=== "C#"
    ```csharp
    Console.WriteLine("hi");
    ```
```

Enable `content.tabs.link` in theme features to link tabs with the same label across the
page.

### Code blocks

Fenced code blocks are highlighted client-side (highlight.js) with line numbers. Use
```` ```mermaid ```` fences for Mermaid diagrams.

## Configurable extensions

Declared under `markdownExtensions`:

```json
"markdownExtensions": [
  { "name": "admonition" },
  { "name": "attr_list" },
  { "name": "toc", "options": { "permalink": true } },
  { "name": "pymdownx.tabbed", "options": { "alternate_style": true } },
  { "name": "pymdownx.superfences" },
  { "name": "pymdownx.snippets", "options": { "base_path": [ "docs/_include" ] } }
]
```

- **`toc`** — `permalink: true` adds anchor links to headings.
- **`pymdownx.tabbed`** — content tabs (alternate style).
- **`pymdownx.superfences`** — nested fenced code / custom fences (e.g. Mermaid).
- **`pymdownx.snippets`** — file includes via [`--8<--`](../plugins/snippets.md).

!!! note
    Unrecognized extension names are ignored gracefully, so a `mkdocs.yml`-derived list
    won't break the build.
