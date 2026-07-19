---
title: search
---

# search

Emits a Material-compatible `search/search_index.json` so the vendored lunr search worker
can power in-page search.

## Behaviour

- One **page-level** document per page whose `text` is the page **intro** (content before
  the first heading) — this keeps search-result teasers concise.
- One **section-level** document per `H1`/`H2`/`H3`, so deep links land on the right
  heading with a short excerpt.
- Front-matter `tags` are indexed and boosted.

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `lang` | string | `"en"` | Search language for the lunr index config. |

```json
{ "name": "search", "options": { "lang": "en" } }
```

## Output

`site/search/search_index.json` with the Material index schema (`config` + `docs`).
