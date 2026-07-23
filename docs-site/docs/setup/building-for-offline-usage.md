---
title: Building for offline usage
---

# Building for offline usage

Sometimes a site can't be hosted on the web — it ships on a USB stick, lives on an air-gapped
network, or is opened straight from disk with `file://`. Netdocs loads a few assets from CDNs
(syntax highlighting, Mermaid, web fonts, emoji) during development; offline mode **self-hosts all
of them** so the built site is fully self-contained. It runs automatically on production builds.

## Enable it

Offline self-hosting is **on by default for production builds** — `netdocs build --prod` (and
`deploy`) self-host CDN assets automatically, while `serve`/dev builds skip it so local iteration
stays fast. You don't have to configure anything to get a self-contained published site.

To control it explicitly, set `optimize.offline` in `appsettings.json`:

```json
"optimize": {
  "offline": true
}
```

| Value | Behaviour |
|---|---|
| omitted / `null` | **Default.** Self-host on production builds (`--prod`, `deploy`), skip during `serve`. |
| `true` | Always self-host, including local and dev builds. |
| `false` | Never self-host (keep CDN references). |

Then build as usual:

```bash
netdocs build --prod
```

```
info: Offline: self-hosted 27 external asset(s) into assets/external across 45 page(s).
```

## What it does

During the build (after pages and assets are written) Netdocs:

1. Scans every emitted page for external assets — `<script src>`, `<link rel="stylesheet">`,
   `<img src>`, and the Mermaid dynamic `import()`.
2. Downloads each one **once** into `assets/external/`.
3. Follows `url(...)` references inside downloaded CSS (e.g. web-font `.woff2`/`.ttf` files) and
   self-hosts those too, rewriting the stylesheet to the local copies.
4. Rewrites every page to point at the local files using **page-relative** paths, so the site
   works from a sub-folder *and* from `file://`.

Only asset tags are rewritten — ordinary `<a href="https://…">` links and `rel="preconnect"`
hints are left untouched.

!!! note "Network is required **at build time**"
    Offline mode downloads the CDN assets while building, so the build machine needs internet
    access. The *output* is then fully offline. Any asset that fails to download keeps its CDN
    URL and logs a warning; combine with [`--strict`](../reference/validation.md) to fail the
    build if the site can't be made fully self-contained.

## Verifying

Open the built `site/` from disk with your browser (double-click `site/index.html`) with the
network disabled. Code highlighting, Mermaid diagrams, fonts, and emoji should all still render.
You can also confirm there are no remaining CDN references:

```bash
# Should print nothing
grep -R "cdn.jsdelivr.net\|fonts.googleapis.com" site/ --include="*.html"
```

## Trade-offs

- The output directory grows by the size of the vendored assets (fonts dominate).
- First build is slower because of the downloads; subsequent incremental builds reuse the
  already-written files.
- Because assets are copied verbatim, you get the exact pinned versions the theme references.
