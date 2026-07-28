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

Every release publishes an immutable `vX.Y.Z` git tag and an immutable `:X.Y.Z` container image,
and every image build also gets an immutable `:sha-<commit>` tag. A few convenience tags **move**
over time:

| Tag | Where | Points at | Stability |
| --- | --- | --- | --- |
| `latest` | container image | The newest stable `vX.Y.Z` release. | Safe default. |
| `1` / `1.2` | container image | The newest release within that major / minor line. | Follows a release line. |
| `main` | container image | The tip of `main` — every merged PR. | Bleeding edge, not release-tested. |
| `v1` | git tag (Action ref) | The newest `1.x` release, used by `XtremeOwnage/Netdocs@v1`. | Tracks the current major. |

There is no `dev`, `stable`, `unstable`, or `edge` tag: `:latest` is the newest release and
`:main` is the bleeding edge. For a pinned, reproducible build, use the full `:X.Y.Z` image, the
`vX.Y.Z` git tag, or a `:sha-<commit>` image.

## Release checklist

1. Ensure `main` is green (build + tests + `dotnet format --verify-no-changes`).
2. Update the changelog / release notes.
3. Tag `vX.Y.Z` and push the tag — CI publishes the release binaries/packages and moves the
   container `:X.Y.Z`, `:X.Y`, `:X`, and `:latest` tags onto it.
4. Move the `v1` alias tag onto the new release so `XtremeOwnage/Netdocs@v1` picks it up.

## Suggestions welcome

This scheme is intentionally simple and open to change — if you have a better idea for
tags or cadence, open an issue or a discussion.
