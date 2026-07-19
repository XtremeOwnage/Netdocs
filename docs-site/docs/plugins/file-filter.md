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

## Behaviour

- The filter is **active** when `enabled` is true (on `serve`, `enabled_on_serve` must
  also be true).
- An **include** label always keeps a page.
- Otherwise, any **exclude** label prunes the page from discovery and navigation.
- If no `exclude_tag` values are configured, the filter is a no-op.

## Enabling the plugin

```json
{ "name": "file-filter" }
```
