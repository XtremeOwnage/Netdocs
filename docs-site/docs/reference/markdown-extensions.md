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

!!! tip "See them rendered"
    The **[Admonitions & tabs](admonitions-and-tabs.md)** page shows every admonition type and
    tab pattern rendered live next to its Markdown — the fastest way to find the right syntax.

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

### Keyboard keys

Wrap a key combination in `++…++` to render Material keyboard keys:

```markdown
Save with ++ctrl+s++ · quit with ++ctrl+alt+del++
```

Common key names (`ctrl`, `alt`, `shift`, `cmd`, `tab`, `esc`, `enter`, arrows, `f1`–`f24`,
…) get their Material glyph and label; anything else renders as a `key-<name>` `<kbd>`.

### CriticMarkup

| Syntax | Renders |
| --- | --- |
| `{++added++}` | inserted text |
| `{--removed--}` | deleted text |
| `{==highlight==}` | highlighted text |
| `{>>comment<<}` | editorial comment |
| `{~~old~>new~~}` | substitution (delete + insert) |

### Footnotes

Footnotes are always on. Reference a note inline with `[^id]` and define it anywhere in
the document:

```markdown
Here is a statement that needs a citation.[^src]

[^src]: The supporting detail, rendered at the foot of the page with a back-link.
```

The reference renders as a superscript link to the definition, and the definition gets a
return arrow back to the reference.

### Emoji

Emoji shortcodes render as [Twemoji](https://github.com/jdecked/twemoji) SVG images
(matching Material's `pymdownx.emoji` + twemoji generator):

```markdown
Ship it :rocket:
```

produces `<img class="twemoji" …>` elements served from a pinned jsDelivr CDN. To
self-host the assets (or pin a different set), override the base URL:

```json
"markdownExtensions": [
  { "name": "pymdownx.emoji", "options": { "base": "/assets/twemoji/" } }
]
```

### Code blocks

Fenced code blocks are highlighted client-side (highlight.js) with line numbers. Use
```` ```mermaid ```` fences for Mermaid diagrams.

The pymdownx.highlight fence options are supported, in both the bare and the attr-list
brace form:

````markdown
```python linenums="5" hl_lines="2 4-5" title="example.py"
print("first")
print("highlighted")
```

```{ .yaml .no-copy title="config.yml" }
key: value
```
````

- `linenums="N"` — start the line-number gutter at `N`.
- `hl_lines="2 4-5"` — highlight the listed lines (ranges allowed).
- `title="…"` — render a file-name caption above the block.
- Line numbers are anchor-linkable (each line gets an `id`).

Inline code highlighting (pymdownx.inlinehilite) uses a shebang: `` `#!python range(10)` ``
renders the span as highlighted `python`.

To include a maintained, tested region of a source file in a code block instead of
copy-pasting, use snippet [named sections](../plugins/snippets.md#named-sections).

!!! note "Code annotations"
    Material's numbered code annotations (`(1)` markers paired with a following list) are
    not yet supported. Use `title=`, `hl_lines=`, and admonitions beneath the block to
    explain code for now.

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
