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

Every release gets an immutable `vX.Y.Z` git tag, and every image build gets an immutable
`:sha-<commit>` tag. The only tags that **move** are:

| Tag | Where | Points at | Stability |
| --- | --- | --- | --- |
| `latest` / `main` | container image | The latest build of `main` — every merged PR. | Bleeding edge, not release-tested. |
| `v1` | git tag (Action ref) | The newest `1.x` release, used by `XtremeOwnage/Netdocs@v1`. | Tracks the current major. |

There is no separate `stable`, `dev`, or `edge` tag. `latest` and `main` are the same
bleeding-edge image today, and a specific release is only reachable by its exact version.

For a pinned, reproducible build, use the full `vX.Y.Z` git tag, or the `:vX.Y.Z` / `:sha-<commit>`
image tag.

## Release checklist

1. Ensure `main` is green (build + tests + `dotnet format --verify-no-changes`).
2. Update the changelog / release notes.
3. Tag `vX.Y.Z` and push the tag — CI publishes the release binaries/packages and the
   `:vX.Y.Z` container image.
4. Move the `v1` alias tag onto the new release so `XtremeOwnage/Netdocs@v1` picks it up.

## Suggestions welcome

This scheme is intentionally simple and open to change — if you have a better idea for
tags or cadence, open an issue or a discussion.
