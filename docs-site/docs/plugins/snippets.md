---
title: snippets
---

# snippets

Includes external files into Markdown using the `--8<--` syntax, so you can share
reusable fragments (files, scripts, notices) across pages.

## Usage

Inline single-line include:

```markdown
--8<-- "includes/notice.md"
```

Block include of multiple files:

```markdown
--8<--
includes/header.md
includes/footer.md
--8<--
```

Paths are resolved against the configured `base_path` entries (and the project root).

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `base_path` | array | `["."]` | Directories searched for included files. |
| `auto_append` | array | — | Files appended to every page (e.g. shared abbreviations). |

```json
{
  "name": "snippets",
  "options": {
    "base_path": [ ".", "docs/_include/files", "docs/snippets" ]
  }
}
```

!!! tip
    Keep include sources out of the built site by excluding them
    (e.g. `"exclude": [ "_include/**", "snippets/**" ]`).
