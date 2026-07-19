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
| `lang` | string \| string[] | `"en"` | Search language(s) for the lunr index config. A list enables lunr's multi-language stemmers. |
| `separator` | string | `"[\\s\\-]+"` | Token separator regex handed to lunr (controls how terms are split). |
| `pipeline` | string[] | `["stemmer", "stopWordFilter", "trimmer"]` | lunr token pipeline. Pass a shorter list (or `[]`) to disable stemming/stop-word filtering. |

```json
{ "name": "search", "options": { "lang": ["en", "de"], "separator": "[\\s\\-\\.]+" } }
```

## Output

`site/search/search_index.json` with the Material index schema (`config` + `docs`).

## Search architecture &amp; the lunr trade-off

Search is powered by Material's **vendored, unmodified** search stack: the
`workers/search.*.min.js` web worker (built on **lunr.js**) fetches `search_index.json`
and builds the lunr index **in the browser**. This keeps full parity with Material for
MkDocs' search UX (highlighting, teasers, keyboard nav) with zero external services.

The known trade-off — flagged upstream in
[squidfunk/mkdocs-material#6307](https://github.com/squidfunk/mkdocs-material/discussions/6307)
— is that lunr indexes **in the browser**, so on very large sites the `search_index.json`
payload and the client-side indexing time both grow with content. For reference, this
docs site emits a ~50&nbsp;KB index, while a large 2,700-document site emits ~2&nbsp;MB.

### Options considered

| Approach | Keeps Material UI? | Notes |
|---|---|---|
| **lunr in-browser (current)** | ✅ | Zero deps, full Material parity. Index size/build time grow with the site. |
| **Prebuilt lunr index** (`config.index`) | ✅ | Material's worker can load a pre-serialized index, skipping in-browser indexing. Requires running lunr (Node) at build time to serialize the index — a build-time JS dependency we don't currently take. |
| **Alternative JS engines** (MiniSearch, FlexSearch, Fuse.js) | ⚠️ | Smaller/faster for big corpora, but would mean replacing Material's search worker and UI wiring. |
| **[Pagefind](https://pagefind.app/)** | ⚠️ | Purpose-built for large static sites: a chunked binary index fetched on demand, so the browser never downloads the whole thing. Best scaling story, but it ships its own UI and would replace Material's search. |
| **Hosted search** (Algolia DocSearch) | ⚠️ | Excellent relevance/scale, but adds an external service and account. |

### Decision

Keep the Material + lunr stack (the UI is the point) and make the previously hard-coded
lunr config tunable via the options above. Two practical levers reduce the payload cost
without leaving Material:

- **Transport compression** — `search_index.json` is highly compressible; most static
  hosts (GitHub Pages, Netlify, CDNs) gzip/brotli JSON automatically, cutting a 2&nbsp;MB
  index to a few hundred&nbsp;KB on the wire.
- **Index shaping** — the emitter already keeps page-level `text` to the intro only and
  splits bodies into per-section docs, which bounds each document's size.

**Pagefind** is the recommended migration path if in-browser indexing ever becomes the
bottleneck for a very large site; it is tracked as a future, opt-in alternative rather
than a replacement for the default Material search.
