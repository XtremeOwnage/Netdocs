---
title: Build validation
---

# Build validation

Netdocs can validate your site as part of the build and report problems as **warnings**. Pair it
with [`--strict`](cli.md#options) (or the `MKDOCS_STRICT` environment variable) to turn those
warnings into a non-zero exit code so CI fails on a broken site.

All checks are **opt-in** and default to `false`. Enable the ones you want under a `validation`
block in `appsettings.json`:

```json
"validation": {
  "links": true,
  "anchors": true,
  "unusedImages": false,
  "orphanPages": false
}
```

## Checks

| Option | What it checks | Emits a warning when… |
|---|---|---|
| `links` | Internal `href`/`src` targets in the rendered pages | a link resolves to a file that isn't in the output |
| `anchors` | `#fragment` anchors on internal links (requires `links`) | the target page has no element with that `id` |
| `unusedImages` | Image files under your `docs/` directory | an image is never referenced by any page |
| `orphanPages` | Source Markdown pages | a page isn't reachable from the navigation tree |

What is **not** flagged:

- **External URLs** (`http://`, `https://`, protocol-relative `//host`), `mailto:`, `tel:`,
  `data:` URIs, and unresolved template tokens are skipped — link checking is offline and never
  makes network requests.
- Generated pages (blog indexes, tag pages, etc.) are ignored by the orphan-page check.

Validation runs **last**, after every page, asset, and plugin output is written, so link targets
are resolved against the real files on disk.

## Failing the build in CI

Warnings alone don't change the exit code — they're informational. Add `--strict` to abort:

```bash
netdocs build --strict
```

```
warn: Broken link in guide/setup.md: '../missing/' does not resolve to an output file.
info: Validation found 1 problem(s).
error: Aborting build: 1 warning(s) treated as errors (strict mode).
```

The same applies to `netdocs deploy --strict`, which refuses to publish when validation fails.
`MKDOCS_STRICT=1` is honored as an alias so existing MkDocs CI pipelines keep working.

## Example: strict links in CI, lenient locally

Keep the checks enabled in config, but only fail the build in CI:

```json
"validation": { "links": true, "anchors": true }
```

```bash
# Local authoring — see warnings, but keep building
netdocs serve

# CI — fail on any broken link or anchor
netdocs build --prod --strict
```
