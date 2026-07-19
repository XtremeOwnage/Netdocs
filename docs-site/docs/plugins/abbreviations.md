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

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `files` | array | — | Abbreviation files appended to every page. |

```json
{ "name": "abbreviations", "options": { "files": [ "_include/abbv.md" ] } }
```
