---
title: Plugins
---

# Plugins

Netdocs has a first-class C# plugin system. Plugins are enabled and configured in the
`plugins` array of `appsettings.json`, and run in the order listed.

```json
"plugins": [
  { "name": "search", "options": { "lang": "en" } },
  { "name": "tags", "options": { "export": true } },
  { "name": "blog", "options": { "blog_dir": "blog/" } }
]
```

## Built-in plugins

| Plugin | Purpose |
|---|---|
| [search](search.md) | Material-compatible `search_index.json` (page + per-section docs). |
| [social](social.md) | 1200×630 Open Graph social cards. |
| [blog](blog.md) | Posts, paginated index, categories, archive, post metadata. |
| [tags](tags.md) | Hierarchical tag index, shadow tags, `tags.json` export. |
| [meta](meta.md) | Per-directory `.meta.yml` front-matter defaults. |
| [snippets](snippets.md) | `--8<--` file includes. |
| [abbreviations](abbreviations.md) | Shared `<abbr>` definition files. |
| [git-revision-date](git-revision-date.md) | Real created/updated dates from git. |
| [redirects](redirects.md) | Client-side redirect map. |
| [glightbox](glightbox.md) | Image lightbox. |
| [rss](rss.md) | RSS feed of blog posts. |
| [file-filter](file-filter.md) | Env-driven label include/exclude. |
| [macros](macros.md) | `fileuri()` / `button()` Markdown macros (mkdocs-macros subset). |
| [b64](b64.md) | Embed local images as inline `data:` URIs (pymdownx.b64). |

## How plugins hook into the build

A plugin implements `IPlugin` (with `Configure`) plus any of these opt-in interfaces:

| Interface | Hook |
|---|---|
| `IMarkdownPreprocessor` | Transform raw Markdown before parsing. |
| `IMarkdigContributor` | Contribute Markdig extensions to the pipeline. |
| `IContentGenerator` | Emit virtual pages (blog lists, tag pages, archives). |
| `IBuildHook` | `OnBuildStart` / `OnPageRendered` / `OnBuildComplete`. |
| `INavigationFilter` | Decide whether a discovered page is included. |

See the source under `src/Netdocs.Plugins/` for reference implementations.
