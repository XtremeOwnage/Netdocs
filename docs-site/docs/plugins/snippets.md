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
The conventional `<project-root>/snippets` directory is always searched as well, so a file
placed at `snippets/notice.md` can be included as `--8<-- "notice.md"` with no configuration.

## Parameterized includes

Includes can also take `key="value"` arguments. Any `${key}` placeholder in the included
file is replaced with the supplied value (HTML-escaped). Because this form is not
line-anchored, it can be used mid-line — for example inside a table cell:

```markdown
| --8<-- "ebay.html" text="Some Product" url="https://ebay.us/abc" | 1 | $40.61 |
```

With `snippets/ebay.html` containing:

```html
<a href="${url}" class="ebay-affiliate-link">${text}</a>
```

The included content is trimmed to a single inline fragment so it won't break the
surrounding Markdown (tables, prose, etc.).

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
| `base_path` | array | `["."]` | Directories searched for included files. `<root>/snippets` is always searched too. |
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
