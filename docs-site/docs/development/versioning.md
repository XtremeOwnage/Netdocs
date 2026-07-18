# Versioning & release tags

Netdocs follows [Semantic Versioning](https://semver.org/). Because it targets near
drop-in compatibility with an existing Material for MkDocs site, "breaking" is judged from
the perspective of **your `appsettings.json` and `docs/`** — not just the C# API.

## Semantic versions

| Bump | Example | When |
| --- | --- | --- |
| **Major** | `1.0.0` → `2.0.0` | Breaking changes: a config shape change, removed behavior, or anything that could break an existing site. Pin to a major version for stability. |
| **Minor** | `1.0.0` → `1.1.0` | New, backward-compatible features (a new plugin, a new flag). |
| **Patch** | `1.1.0` → `1.1.1` | Bug fixes and minor tweaks with no new surface area. |

If you want stability, **bind to a specific major version** (e.g. the `1` container tag or
a `1.x` package range) so a future major can't surprise you.

## Moving tags

In addition to immutable `vX.Y.Z` release tags, these convenience tags move over time:

| Tag | Points at | Stability |
| --- | --- | --- |
| `latest` / `stable` | The most recent stable release. | Safe default. |
| `dev` | The latest build off `main`. | Usually fine, not release-tested. |
| `main` / `unstable` | The tip of `main` — every merged PR. | May be unstable. |

For containers this maps to image tags (`ghcr.io/xtremeownage/netdocs:1`,
`:1.2.3`, `:latest`, `:dev`). For a pinned, reproducible build, always use the full
`vX.Y.Z` / `:1.2.3` form.

## Release checklist

1. Ensure `main` is green (build + tests + `dotnet format --verify-no-changes`).
2. Update the changelog / release notes.
3. Tag `vX.Y.Z` and push the tag — CI publishes artifacts and moves `latest`/`stable`.
4. `dev` continues to track `main`.

## Suggestions welcome

This scheme is intentionally simple and open to change — if you have a better idea for
tags or cadence, open an issue or a discussion.
