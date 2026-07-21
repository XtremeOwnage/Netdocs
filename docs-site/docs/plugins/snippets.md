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

## Named sections

Include only a marked region of a file by appending `:section` to the path. In the
source file, wrap the region in start/end markers:

```markdown
<!-- in includes/sample.py -->
# --8<-- [start:setup]
import os
config = os.environ
# --8<-- [end:setup]
```

Then include just that section from any page:

```markdown
--8<-- "includes/sample.py:setup"
```

Only the lines between the matching `[start:setup]` and `[end:setup]` markers are
included; the marker lines themselves are stripped. This mirrors
[pymdownx.snippets sections](https://facelessuser.github.io/pymdown-extensions/extensions/snippets/#snippet-sections).

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

## Attribution

Behavior is modeled on [pymdownx.snippets](https://facelessuser.github.io/pymdown-extensions/extensions/snippets/) by @facelessuser (MIT). See [Attributions](../about/attributions.md).
