---
title: file-filter
---

# file-filter

Dynamically includes or excludes pages based on front-matter **labels** and the build
environment — for example, hiding `draft`/`internal` content from production while keeping
it visible during local development.

Configuration lives in a **`.file-filter.yml`** at the project root (next to
`appsettings.json`).

## `.file-filter.yml`

```yaml
# Master switch — here driven by an env var, defaulting to true.
enabled: !ENV [IS_LOCAL_BUILD, true]
# Whether filtering also applies during `serve`.
enabled_on_serve: true

# Front-matter property that holds the labels.
metadata_property: labels

# Path-based exclusion via a .mkdocsignore file (see below).
mkdocsignore: true
mkdocsignore_file: .mkdocsignore

# Pages with any of these labels are excluded…
exclude_tag:
  - draft
  - internal
  - alpha
# …unless they also carry an include label (which always wins).
include_tag:
  - release
```

## Page usage

```yaml
---
labels:
  - draft
---
# Work in progress

This page is excluded when the filter is active.
```

## Path exclusion with `.mkdocsignore`

For whole sections you don't want in production, list their paths in a **`.mkdocsignore`**
at the project root (same glob style as `.gitignore`):

```text title=".mkdocsignore"
# Dev-only areas — present locally, hidden in production
internal-notes/
teams/
```

Path exclusion follows the **same `enabled` gate** as label filtering, so those sections
stay visible during development / `serve` and disappear only from production builds. The
filter turns on when `enabled` resolves true — drive it from a build flag, e.g.
`enabled: !ENV [MKDOCS_PROD_BUILD, false]` (Netdocs sets `MKDOCS_PROD_BUILD=true` on
`--prod`).

!!! note "No `.file-filter.yml`?"
    If you have a `.mkdocsignore` but **no** `.file-filter.yml`, the ignore file is always
    applied (like `.gitignore`). The gate only exists once you opt into the filter.

## Behaviour

- The filter is **active** when `enabled` is true (on `serve`, `enabled_on_serve` must
  also be true).
- While active, `.mkdocsignore` paths are pruned from discovery; while inactive they are
  kept, so dev-only sections reappear on non-production builds.
- An **include** label always keeps a page.
- Otherwise, any **exclude** label prunes the page from discovery and navigation.
- If no `exclude_tag` values are configured, the **label** filter is a no-op (path
  exclusion via `.mkdocsignore` still applies when active).

## Enabling the plugin

```json
{ "name": "file-filter" }
```

## Attribution

Behavior is modeled on the mkdocs-file-filter concept (MIT). See [Attributions](../about/attributions.md).
