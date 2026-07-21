---
title: git-revision-date
---

# git-revision-date

Sets each page's **updated** (and optionally **created**) date from git history, using a
single-pass LibGit2Sharp walk. Falls back to filesystem timestamps when git data is not
available, and is skipped during `serve` for speed.

The dates surface in the page footer ("Last updated" / "Created") and feed other plugins
such as the blog and RSS.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `enable_creation_date` | bool | `false` | Also set the created date from the first commit. |

```json
{
  "name": "git-revision-date-localized",
  "options": { "enable_creation_date": true }
}
```

!!! note
    The plugin is registered under the name `git-revision-date-localized` for MkDocs
    compatibility.

## Attribution

Behavior is modeled on [mkdocs-git-revision-date-localized-plugin](https://github.com/timvink/mkdocs-git-revision-date-localized-plugin) by @timvink (MIT). See [Attributions](../about/attributions.md).
