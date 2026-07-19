---
title: abbreviations
---

# abbreviations

Appends shared abbreviation-definition files to every page so `<abbr>` tooltips work
site-wide without repeating definitions in each document.

## Usage

Create an abbreviations file:

```markdown
<!-- docs/_include/abbv.md -->
*[HTML]: HyperText Markup Language
*[CSS]: Cascading Style Sheets
*[SSG]: Static Site Generator
```

Reference the terms anywhere; matching words render as `<abbr>` with a tooltip:

```markdown
Netdocs is an SSG that outputs HTML and CSS.
```

Abbreviations are expanded in prose only. Terms that appear inside inline code
(`` `HTML` ``) or fenced code blocks are left untouched, so code samples are never
rewritten.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `files` | array | `["_include/abbv.md"]` | Abbreviation files appended to every page. |

```json
{ "name": "abbreviations", "options": { "files": [ "_include/abbv.md" ] } }
```

The plugin only runs when it is listed in `plugins`. If your abbreviations are not
rendering, confirm the entry above is present and that each `files` path exists
(relative to your `docs` directory).

## Attribution

Behavior is modeled on Python-Markdown `abbr` / [PyMdown Extensions](https://facelessuser.github.io/pymdown-extensions/) (BSD-3-Clause). See [Attributions](../about/attributions.md).
