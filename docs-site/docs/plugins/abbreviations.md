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

## First occurrence only

By default, only the **first** occurrence of each term on a page is wrapped in an
`<abbr>` tooltip; later occurrences render as plain text. This keeps pages readable —
a term repeated a dozen times no longer fills the page with dotted-underline
placeholders, matching how print glossaries gloss a term once.

Set `first_instance_only` to `false` to restore the classic behaviour of marking
*every* occurrence:

```json
{ "name": "abbreviations", "options": { "first_instance_only": false } }
```

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `files` | array | `["_include/abbv.md"]` | Abbreviation files appended to every page. |
| `first_instance_only` | bool | `true` | Mark only the first occurrence of each term per page. |

```json
{ "name": "abbreviations", "options": { "files": [ "_include/abbv.md" ], "first_instance_only": true } }
```

The plugin only runs when it is listed in `plugins`. If your abbreviations are not
rendering, confirm the entry above is present and that each `files` path exists
(relative to your `docs` directory).

## Attribution

Behavior is modeled on Python-Markdown `abbr` / [PyMdown Extensions](https://facelessuser.github.io/pymdown-extensions/) (BSD-3-Clause). See [Attributions](../about/attributions.md).
