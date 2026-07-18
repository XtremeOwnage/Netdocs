---
title: Netdocs vs MkDocs
---

# Netdocs vs Material for MkDocs

Netdocs is a .NET static site generator built to replace a **Material for MkDocs** project with
minimal friction: it reads the same Markdown, mirrors the Material theme, ships equivalents for the
common plugins, and can [import your `mkdocs.yml`](../setup/migrating-from-mkdocs.md) automatically.
This page is an honest, side-by-side comparison — what's the same, what's better, and what isn't
implemented yet.

If you're migrating, read this alongside the [migration guide](../setup/migrating-from-mkdocs.md).

## At a glance

| Area | Material for MkDocs | Netdocs |
|---|---|---|
| Runtime | Python | .NET (single self-contained binary or Docker image) |
| Config format | `mkdocs.yml` (YAML) | `appsettings.json` (JSON) — plus `netdocs import` for your YAML |
| Theme | Material for MkDocs | Vendored Material theme (same CSS/JS bundle) |
| Markdown engine | Python-Markdown + PyMdown | [Markdig](https://github.com/xoofx/markdig) |
| Plugin language | Python | C# (compiled, or external DLLs) |
| Extensibility | Python hooks / plugins | Typed hook interfaces (see [events & callbacks](../development/events-and-callbacks.md)) |
| Distribution | `pip install` | Release binary, `dotnet tool`, or GHCR container |

## What stays the same

- **Your Markdown.** Admonitions, tabbed content, footnotes, code fences (titles, line numbers,
  `hl_lines`), emoji, keyboard keys, CriticMarkup, abbreviations, and Mermaid all work with the same
  syntax you already use.
- **The look.** Netdocs vendors the actual Material theme bundle, so the header, nav, search,
  palette toggle, and typography match. See the [theme reference](../reference/theme.md).
- **Nav & structure.** Nested `nav`, section indexes, and `docs/` layout are preserved by the
  importer.
- **Common plugins.** search, tags, blog, social cards, RSS, redirects, macros, snippets,
  git-revision-date, glightbox, meta, table-reader, and more have Netdocs equivalents — see the
  [plugins reference](../plugins/index.md).

## What's different (and why)

| Topic | Difference |
|---|---|
| **Config is JSON, not YAML** | `appsettings.json` under a `Netdocs` section. `netdocs import mkdocs.yml` converts your existing config, including nested nav, plugins, extensions, and `extra`. |
| **Plugins are opt-in** | No plugin runs unless it's listed. There is no implicit default set, which keeps builds predictable and fast. The [plugins index](../plugins/index.md) suggests a recommended set. |
| **Prod-only behavior via `--prod`** | MkDocs `MKDOCS_*` env toggles become explicit config plus the `netdocs build --prod` flag. |
| **Compiled plugins** | Plugins are C# implementing typed hook interfaces instead of Python. This gives compile-time checking and speed, at the cost of writing C# (or loading a prebuilt DLL). |
| **Single binary / container** | No Python environment to manage; ship a self-contained executable or use the GHCR image. |
| **Base-path-safe links** | Netdocs rewrites internal links to be safe under a Pages sub-path automatically. |

## What's better in Netdocs

- **Speed.** Compiled pipeline + a render cache keyed on content — see the
  [benchmarks](benchmarks.md).
- **Typed, discoverable extensibility.** Hook interfaces are documented with signatures and firing
  order in [events & callbacks](../development/events-and-callbacks.md), and the
  [build lifecycle diagram](../development/lifecycle.md) shows exactly where each runs.
- **Built-in deploy targets.** `filesystem`, `git` (gh-pages), and `s3` publishing are part of the
  build config — no separate `gh-deploy` step required. See [publishing](../setup/publishing.md).
- **Output optimization.** Optional HTML/CSS/JS minification and WebP image conversion are
  first-class build toggles.
- **A calculator plugin.** Interactive `calc` fences render as live forms — no MkDocs equivalent.
- **Config importer.** `netdocs import` gets you from `mkdocs.yml` to `appsettings.json` in one step.

## Known gaps / not implemented yet

Netdocs targets parity with a real-world Material for MkDocs blog, but it is not a 100% superset.
Notable things that are **not** implemented (or only partially) today:

| Feature | Status | Notes |
|---|---|---|
| Arbitrary PyMdown extensions | Partial | The commonly-used set is built in; niche PyMdown extensions have no equivalent. |
| Python `hooks:` / plugin ecosystem | Not applicable | Netdocs plugins are C#. Port logic to a C# plugin or an [external DLL](../development/external-plugins.md). |
| `mkdocs serve` live-reload dev server | Not yet | Use `netdocs build` (fast, cached); a watch mode is on the roadmap. |
| Versioned docs (mike) | Not yet | No built-in multi-version switcher. |
| Instant prefetch / progressive rendering knobs | Partial | Instant navigation works; not every Material `navigation.*` toggle is wired. |
| i18n / static site search in non-Latin languages | Partial | Search supports configured `lang`; full CJK tokenization is limited. |
| Offline bundle (fully CDN-free) | Partial | Most assets are vendored; Mermaid/Twemoji load from a CDN by default. |

If you rely on something that isn't here, it's usually straightforward to add as a plugin — the
[writing a plugin](../development/events-and-callbacks.md) reference and
[external plugins](../development/external-plugins.md) guide are the place to start. Upstream
contributions are welcome.

## Deciding

Choose **Netdocs** if you want a fast, single-binary/container build, typed extensibility, and
built-in deploy/optimization, and your Markdown + plugin needs are covered above. Stay on
**Material for MkDocs** if you depend on a niche PyMdown extension, the Python plugin ecosystem, or
`mkdocs serve`/mike features that aren't implemented yet — or run both and
[diff the output](../setup/migrating-from-mkdocs.md#5-verify-parity) before switching.
