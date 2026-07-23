---
title: Front-end dependencies
---

# Front-end dependencies

Netdocs keeps the .NET build free of a Node toolchain, so the browser libraries the theme
needs are loaded from a **CDN** (jsDelivr) with pinned versions rather than bundled through
npm. To keep those pinned versions visible to security tooling, the repository ships a
[`package.json`](https://github.com/XtremeOwnage/Netdocs/blob/main/package.json) that
mirrors them and is watched by Dependabot.

## How it works

- `/package.json` is a **tracking manifest only** — nothing is installed from it, and it is
  marked `"private": true`. It exists so Dependabot's `npm` ecosystem can flag new releases.
- When Dependabot opens an update PR against `package.json`, bump the matching CDN URL(s) in
  the theme/plugins to the same version in the **same PR**, then merge. That keeps the
  manifest and the actual loaded version in lockstep.

## Where each library is referenced

| Library | Manifest entry | Loaded from |
|---|---|---|
| highlight.js | `highlight.js` | `templates/partials/highlight.html` |
| highlightjs-line-numbers.js | `highlightjs-line-numbers.js` | `templates/partials/highlight.html` |
| Mermaid | `mermaid` | `templates/partials/mermaid.html` |
| MathJax | `mathjax` | `ArithmatexPlugin.cs` (`arithmatex` plugin) |
| GLightbox | `glightbox` | `StubPlugins.cs` (`glightbox` plugin) |
| Twemoji | `@twemoji/api` | `TwemojiExtension.cs` (emoji SVG base URL) |

!!! note "Twemoji"
    Emoji SVGs are loaded from a GitHub-tagged asset path
    (`gh/jdecked/twemoji@<version>/assets/svg/`), not an npm package. The npm
    package `@twemoji/api` is versioned in lockstep with those GitHub release
    tags, so it is listed in `package.json` purely to let Dependabot flag new
    releases. When it bumps, set `TwemojiExtension.DefaultBaseUrl` to the same
    `jdecked/twemoji@<version>` tag in the same PR.

## Updating a version manually

1. Change the version in `/package.json`.
2. Update the matching CDN URL(s) from the table above.
3. Rebuild and smoke-test the affected feature (syntax highlighting, diagrams, math,
   lightbox) before merging.
