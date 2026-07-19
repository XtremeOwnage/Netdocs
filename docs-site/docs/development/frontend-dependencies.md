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

!!! note "Twemoji"
    Emoji SVGs come from a GitHub-tagged asset path
    (`gh/jdecked/twemoji@…`), not an npm package, so it is pinned directly in
    `TwemojiExtension.cs` and is not listed in `package.json`. Bump it there when a new
    Twemoji release is desired.

## Updating a version manually

1. Change the version in `/package.json`.
2. Update the matching CDN URL(s) from the table above.
3. Rebuild and smoke-test the affected feature (syntax highlighting, diagrams, math,
   lightbox) before merging.
