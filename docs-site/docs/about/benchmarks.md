---
title: Performance benchmarks
---

# Performance benchmarks

Netdocs is a compiled .NET static site generator, so it builds large documentation
sets and image-heavy blogs in **seconds** rather than minutes. The numbers below are
measured against a real, production content set — the
[XtremeOwnage blog](https://static.xtremeownage.com/blog) — not a synthetic corpus.

## Headline results

Building the production XO blog (243 pages, **1,880 images**, 2,196 output files,
~255 MB of generated output):

| Scenario | Pages | Wall time | Throughput |
|---|---:|---:|---:|
| Cold build (no cache, clean output) | 243 | ~6.0 s | ~40 pages/s |
| Warm build (incremental render cache) | 243 | ~5.4 s | ~45 pages/s |
| This documentation site | 32 | ~0.5 s | ~64 pages/s |

Cold and warm times are close because the dominant cost for this content set is
copying and hashing ~1,880 images and ~255 MB of assets, not Markdown rendering — the
render cache primarily accelerates the Markdown parse/render step (see the
[incremental render cache](../reference/cli.md#incremental-render-cache)).

## Test environment

| Component | Value |
|---|---|
| CPU | Intel Core Ultra 9 185H |
| RAM | 64 GB |
| OS | Windows 11 (10.0.26100) |
| Runtime | .NET 11 |
| Build | `netdocs build` (Release) |
| Content | [static.xtremeownage.com/blog](https://static.xtremeownage.com/blog) — 243 pages, 1,880 images |

Times are wall-clock for the `Built N pages in …` figure the CLI reports, averaged over
three runs.

## How to reproduce

The benchmark is just a timed `build` against your own content, so you can validate the
numbers on your hardware:

```pwsh
# Cold build: clear the incremental cache and output first
Remove-Item -Recurse -Force .\.cache, .\site -ErrorAction SilentlyContinue
netdocs build --config .\appsettings.json

# Warm build: run again to reuse the render cache
netdocs build --config .\appsettings.json
```

The CLI prints `Built <N> pages in <ms> ms` at the end of each run, and warm builds also
log `Render cache: <reused>/<total> pages reused`.

## How this compares to mkdocs-material

[Material for MkDocs](https://squidfunk.github.io/mkdocs-material/) is the reference this
project is modelled on, so we benchmark against it directly on the **same content set** —
the XO blog repository — with social-card generation disabled on both sides (it shells out
to a headless browser / Cairo and is not equivalent work).

| Generator | Cold build (same content) | Relative |
|---|---:|---:|
| **Netdocs** (`netdocs build`, Release) | **~6 s** | **1×** |
| Material for MkDocs 9.x (`mkdocs build`) | ~97 s (best) – ~146 s (under load) | ~16–24× slower |

Both were run on the same machine (see [Test environment](#test-environment)) against the
same Markdown + images, with the Material `social` plugin disabled so neither tool pays for
social-card rendering. Material for MkDocs reported `Documentation built in 96.92 seconds`
on its fastest run; Netdocs reports `Built 243 pages in ~6 s` for the equivalent content.

The gap comes from architecture, not tuning: Material for MkDocs starts a Python interpreter,
loads plugins, and renders every page through Jinja in a single process, while Netdocs is a
single compiled .NET binary that renders pages in parallel with an incremental render cache.

### Reproducing the mkdocs-material number

```pwsh
# From the MkDocs content repo (with mkdocs-material installed):
python -m mkdocs build -d site_mkdocs
```

We publish the **methodology** as well, because a like-for-like comparison depends heavily on
the Python interpreter, enabled plugins (especially social-card generation), and the
image-optimization pipeline:

1. Point both generators at the same content set (same Markdown, same images).
2. Disable non-equivalent plugins on both sides so you compare the same work.
3. Measure a cold build (clear each tool's cache) and a warm build.
4. Compare wall-clock time and peak memory.

Run the reproduction steps above against your own content to get numbers that reflect your
plugins and hardware.
