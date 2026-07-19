---
title: table-reader
---

# table-reader

Expands `{{ read_csv(...) }}` / `{{ read_table(...) }}` directives into Markdown pipe
tables at build time, so tabular data can live in a `.csv`/`.tsv` file next to your page
instead of being hand-maintained as Markdown. This is a port of
[mkdocs-table-reader-plugin](https://github.com/timvink/mkdocs-table-reader-plugin).

## Usage

Reference a delimited file from anywhere in a page:

```markdown
{{ read_csv("data/parts.csv") }}
```

The first row becomes the table header. A `read_csv` call defaults to a comma delimiter;
`read_table` defaults to a TAB. Pass an explicit delimiter as the second argument:

```markdown
{{ read_table("data/parts.tsv", ";") }}
```

Use `\t` for a tab and `\n` for a newline in the delimiter argument.

## Path resolution

Paths are resolved in the same order as mkdocs-table-reader, so a post can reference a
sibling file without knowing the docs root:

1. The **page's own directory** (e.g. `assets/foo.csv` next to the post).
2. The configured **docs directory**.
3. The **project root**.

Absolute paths are used as-is. If the file cannot be found, an HTML comment is emitted in
place of the table (`<!-- table-reader: file not found: ... -->`) so the build doesn't fail.

## Quoted fields

The parser honors RFC 4180-style double-quoted fields, so values may contain the delimiter,
line breaks, or escaped quotes (`""`):

```csv
name,notes
"LSI 9207-8i","HBA, flashed to IT mode"
"Cable","2 m, ""thick"" gauge"
```

Cell values containing a literal `|` are escaped automatically so they don't break the
generated pipe table.

## Options

This plugin has no options — enable it by name:

```json
{ "name": "table-reader" }
```

It runs as a Markdown preprocessor (order `20`), so directives are expanded before the
Markdown is parsed and before later preprocessors (such as
[link-notes](link-notes.md)) inspect the resulting table.

## Attribution

Behavior is modeled on
[mkdocs-table-reader-plugin](https://github.com/timvink/mkdocs-table-reader-plugin) by
@timvink (MIT). See [Attributions](../about/attributions.md).
