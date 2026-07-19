---
title: versioning
nav_title: versioning
---

# versioning

Adds a **version selector** to the header so readers can jump between different published
versions of your documentation — the Netdocs equivalent of Material for MkDocs'
[versioning](https://squidfunk.github.io/mkdocs-material/setup/setting-up-versioning/) (mike)
integration. The plugin emits a mike-compatible `versions.json` at the site root and renders a
dropdown in the header populated either from an explicit list or from your repository's release
tags.

The plugin is **opt-in** — add it to `plugins` to enable the selector.

## How multi-version sites are laid out

Like mike, a versioned site serves **each version from its own subdirectory** at the site root:

```text
/            → redirects to the default version
/2.0/        → docs built from the v2.0 tag
/1.0/        → docs built from the v1.0 tag
/versions.json
```

You build each version separately (typically in CI, checking out each tag) into its own output
folder, and publish them side by side. The version selector then links between those folders.
`versions.json` is the manifest other tooling (and the selector) reads.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `provider` | string | `static` | `static` (use the `versions` list) or `git-tags` (discover versions from release tags). |
| `versions` | array | `[]` | Explicit version entries (see below). Always merged first; config wins over discovered tags. |
| `current` | string | first / `latest` alias | The version id to mark as active in the dropdown. |
| `label` | string | `Version` | Accessible label for the selector button. |
| `url_template` | string | `../{version}/` | URL for a version without an explicit `url`. `{version}` is substituted. |
| `tag_pattern` | string | `^v?\d+(\.\d+)*$` | (git-tags) Regex a tag must match to become a version. |

Each entry in `versions` accepts:

| Field | Type | Description |
|---|---|---|
| `version` | string | Version id (required), e.g. `2.0`. |
| `title` | string | Display text in the dropdown (defaults to `version`). |
| `url` | string | Link target (defaults to `url_template` with `{version}` substituted). |
| `aliases` | array | Extra names such as `latest`; used to pick `current` when it isn't set. |

## Static list

Best when you publish a small, curated set of versions and want full control over titles and
links:

```json
{
  "name": "versioning",
  "options": {
    "current": "2.0",
    "versions": [
      { "version": "2.0", "title": "2.0 (latest)", "url": "/2.0/", "aliases": ["latest"] },
      { "version": "1.0", "title": "1.0", "url": "/1.0/" }
    ]
  }
}
```

## From release tags

Set `provider` to `git-tags` to build the list automatically from the repository's tags. Tags are
filtered by `tag_pattern` and ordered newest-first (numeric-aware, so `1.10` sorts after `1.9`):

```json
{
  "name": "versioning",
  "options": {
    "provider": "git-tags",
    "current": "2.1",
    "url_template": "/{version}/",
    "tag_pattern": "^v\\d+\\.\\d+\\.\\d+$"
  }
}
```

Each matching tag becomes a version whose id and title are the tag name and whose URL is
`url_template` with `{version}` replaced. Discovered tags are merged **after** any explicit
`versions`, so you can override a specific tag's title or URL by listing it in `versions`.

!!! note
    `git-tags` shells out to `git` in the project root. If git is unavailable or the directory
    is not a repository, the plugin logs a warning and falls back to the explicit `versions`
    list (which may be empty, in which case no selector is shown).

## Output

- A `versions.json` manifest at the site root: `[{ "version", "title", "aliases" }]`.
- A version selector in the header, shown only when at least one version resolves.

## Attribution

The versioning model and `versions.json` schema follow
[mike](https://github.com/jimporter/mike) and Material for MkDocs (both MIT). See
[Attributions](../about/attributions.md).
