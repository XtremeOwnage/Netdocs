# Build lifecycle & hook order

This page is the visual reference for **where things hook into a Netdocs build**. If you
are writing a plugin or a theme override and you are not sure *when* your code runs — or
*where* in the pipeline you should hook in — start here.

Everything below is driven by
[`BuildEngine.BuildAsync`](https://github.com/XtremeOwnage/Netdocs/blob/main/src/Netdocs.Core/BuildEngine.cs),
which orchestrates one build from config to emitted HTML.

## The pipeline at a glance

```mermaid
flowchart TD
    A[Load appsettings.json to SiteConfig] --> B[Load external plugins<br/>plugins/*.dll]
    B --> C[Build plugin host<br/>IPlugin.Configure on every plugin]
    C --> D[Discover docs/**<br/>ContentDiscovery]
    D --> E[IImportHook.OnImportAsync<br/>federated docs]
    E --> F{INavigationFilter<br/>ShouldInclude}
    F -->|kept| G[IBuildHook.OnBuildStartAsync]
    G --> H[IContentGenerator.GenerateAsync<br/>virtual pages: blog, tags, archives]
    H --> I[IMarkdownPreprocessor.ProcessAsync<br/>ordered by Order — snippets, abbr, macros]
    I --> J[Parse + render Markdown<br/>Markdig pipeline + IMarkdigContributor.Extend<br/>parallel, render-cache aware]
    J --> K[Resolve navigation<br/>NavigationBuilder]
    K --> L[Copy assets<br/>theme + docs static + plugin-registered]
    L --> M[Template render<br/>Scriban + theme, parallel to HTML]
    M --> N[Emit 404.html]
    N --> O[IBuildHook.OnPageRenderedAsync<br/>per page]
    O --> P[IBuildHook.OnBuildCompleteAsync<br/>search index, rss, tags.json]
    P --> Q[Emit sitemap.xml]
    Q --> R[Prune stale output files]

    classDef hook fill:#e8f0fe,stroke:#3f51b5,color:#1a237e;
    class E,F,G,H,I,O,P hook;
```

The **blue** nodes are the extension points a plugin can implement. Everything else is
engine-owned.

## Ordered stages

| # | Stage | Extension point | Notes |
| --- | --- | --- | --- |
| 1 | Load configuration | — | `appsettings.json` becomes `SiteConfig`. |
| 2 | Load external plugins | *(discovery)* | `plugins/*.dll` are registered but not yet enabled. See [External plugins](external-plugins.md). |
| 3 | Configure plugins | `IPlugin.Configure` | Called **once** per enabled plugin. Register assets/scripts/services here. Runs in `appsettings.json` plugin order. |
| 4 | Discover content | — | Walks `docs/**`, honoring `.mkdocsignore` / `exclude`. |
| 5 | Import docs | `IImportHook.OnImportAsync` | Load imported pages before navigation filters so they participate in the full build. |
| 6 | Navigation filters | `INavigationFilter.ShouldInclude` | A page is kept only if **all** filters return `true`. |
| 7 | Build start | `IBuildHook.OnBuildStartAsync` | First async hook. The full (filtered) page set is on `site.Pages`. |
| 8 | Content generation | `IContentGenerator.GenerateAsync` | Emit virtual pages (blog lists, tag pages). Generated pages join `site.Pages`. |
| 9 | Preprocess Markdown | `IMarkdownPreprocessor.ProcessAsync` | Runs **in ascending `Order`** for every page (including generated). Text-in/text-out. |
| 10 | Parse + render | `IMarkdigContributor.Extend` | Markdig extensions are contributed once; pages render in parallel and results are cached by content hash. |
| 11 | Resolve navigation | — | Builds the nav tree used by templates. |
| 12 | Copy assets | `IPluginContext.AddAsset` | Theme assets, `docs/` static files, and plugin-registered assets are copied. Runs **before** rendering so the pages can reference what was produced — notably which images gained a `.webp` sibling. |
| 13 | Template render | *(theme templates)* | Scriban renders each page (parallel) to HTML. See [Template render order](#template-render-order). |
| 14 | 404 page | *(theme templates)* | `404.html` is rendered if the theme provides it. |
| 15 | Page rendered | `IBuildHook.OnPageRenderedAsync` | Called for every page after HTML is written. Good for indexing (search). |
| 16 | Build complete | `IBuildHook.OnBuildCompleteAsync` | Last hook. Emit whole-site artifacts (search index, RSS, tag exports, social cards). |
| 17 | Sitemap | — | Built-in `sitemap.xml`. |
| 18 | Prune | — | Removes output files this build did not (re)produce. |

### Preprocessor ordering

Preprocessors are sorted by their `Order` property (ascending). The built-ins use:

| Order | Preprocessor | Why |
| --- | --- | --- |
| 10 | `snippets` | Expand `--8<--` includes first so later steps see the full text. |
| 15 | `calculator` / `timeline` | Fence-replacing plugins that render their own block; run after includes, before table-reader. |
| 20 | `table-reader` / `abbreviations` | Operate on already-included content. |
| 25 | `macros` | Runs after snippets/table-reader so their output can contain macros. |

Pick an `Order` relative to these when you need your preprocessor to run before or after
a built-in.

#### Overriding order from config

You don't need to recompile a plugin to change where it runs: set an `order` on its
`plugins` entry to override its natural `Order`. Lower runs earlier; ties keep config
order. For example, to run `macros` *before* `table-reader`:

```json title="appsettings.json"
{ "name": "macros", "order": 15 }
```

Other hook types (Markdig contributors, content generators, build hooks, navigation
filters) run in the order their plugins are listed in `plugins`, so reordering that list
is all that's needed for them.

## Template render order

Stage 13 renders each page with [Scriban](https://github.com/scriban/scriban). The theme's
`main.html` is the root layout; it pulls in partials in roughly this order:

```mermaid
flowchart TD
    M[main.html] --> H[partials/header.html]
    M --> N[partials/nav.html]
    M --> T[partials/toc.html]
    M --> C[page.content — rendered Markdown]
    M --> PM[partials/post-meta.html<br/>blog posts]
    M --> BN[partials/blog-nav.html<br/>blog views]
    M --> F[partials/footer.html]
    M --> S[partials/search.html]
    M --> HL[partials/highlight.html]
    M --> ME[partials/mermaid.html]
    M --> TB[partials/tabs.html]
```

To override any of these, point `theme.custom_dir` at a folder of **Scriban** templates
that mirror the theme layout. (Material's Jinja2 overrides are detected and ignored.) See
[Theme reference](../reference/theme.md).

## Choosing where to hook

| I want to… | Implement | Runs at |
| --- | --- | --- |
| Register CSS/JS/assets or read options | `IPlugin.Configure` | Stage 3 |
| Import external docs | `IImportHook.OnImportAsync` | Stage 5 |
| Include/exclude pages | `INavigationFilter` | Stage 6 |
| Do setup once the page set is known | `IBuildHook.OnBuildStartAsync` | Stage 7 |
| Create new pages | `IContentGenerator` | Stage 8 |
| Rewrite Markdown text | `IMarkdownPreprocessor` | Stage 9 |
| Add Markdown syntax/extensions | `IMarkdigContributor` | Stage 10 |
| React to each rendered page | `IBuildHook.OnPageRenderedAsync` | Stage 15 |
| Emit a whole-site artifact | `IBuildHook.OnBuildCompleteAsync` | Stage 16 |

See the [Events & callbacks reference](events-and-callbacks.md) for the exact method
signatures and examples.
