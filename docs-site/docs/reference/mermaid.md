# Mermaid diagrams

Netdocs renders [Mermaid](https://mermaid.js.org/) diagrams from fenced code blocks. A fence
tagged `mermaid` is emitted server-side as a `<pre class="mermaid">` container; the theme lazy-loads
the Mermaid runtime in the browser (only when a diagram is present) and re-renders on every page
load, including Material's instant-navigation swaps.

## How it works

Write a fence with the `mermaid` info string and put a valid Mermaid definition inside:

````markdown
```mermaid
graph LR
  A[Markdown] --> B[Netdocs] --> C[HTML]
```
````

Renders as:

```mermaid
graph LR
  A[Markdown] --> B[Netdocs] --> C[HTML]
```

There is nothing to install and no plugin to enable — Mermaid support is built into the fence
parser. The runtime is fetched from a pinned jsDelivr CDN (`mermaid@11`) the first time a page
contains a diagram, so pages without diagrams load nothing extra.

!!! tip "Offline / air-gapped builds"
    The runtime loads from a CDN by default. If you need fully offline pages, vendor the Mermaid
    ESM bundle and point the theme's `partials/mermaid.html` import at your local copy.

## Diagram types

The following are the most common Mermaid diagram types. Each shows the Markdown source and the
live result. See the [Mermaid docs](https://mermaid.js.org/intro/) for the full syntax of each.

### Flowchart

````markdown
```mermaid
flowchart TD
  Start([Start]) --> Read[Read Markdown]
  Read --> Cache{Cached?}
  Cache -- yes --> Reuse[Reuse HTML]
  Cache -- no --> Render[Render + cache]
  Reuse --> Write[Write page]
  Render --> Write
  Write --> Done([Done])
```
````

Renders as:

```mermaid
flowchart TD
  Start([Start]) --> Read[Read Markdown]
  Read --> Cache{Cached?}
  Cache -- yes --> Reuse[Reuse HTML]
  Cache -- no --> Render[Render + cache]
  Reuse --> Write[Write page]
  Render --> Write
  Write --> Done([Done])
```

### Sequence diagram

````markdown
```mermaid
sequenceDiagram
  participant CLI
  participant Engine
  participant Plugin
  CLI->>Engine: build
  Engine->>Plugin: OnPageRendering(page)
  Plugin-->>Engine: modified HTML
  Engine-->>CLI: site written
```
````

Renders as:

```mermaid
sequenceDiagram
  participant CLI
  participant Engine
  participant Plugin
  CLI->>Engine: build
  Engine->>Plugin: OnPageRendering(page)
  Plugin-->>Engine: modified HTML
  Engine-->>CLI: site written
```

### Class diagram

````markdown
```mermaid
classDiagram
  class Page {
    +string SourcePath
    +string RelativePath
    +string Url
    +string HtmlContent
  }
  class Plugin {
    <<interface>>
    +OnPageRendering(Page)
  }
  Plugin ..> Page : transforms
```
````

Renders as:

```mermaid
classDiagram
  class Page {
    +string SourcePath
    +string RelativePath
    +string Url
    +string HtmlContent
  }
  class Plugin {
    <<interface>>
    +OnPageRendering(Page)
  }
  Plugin ..> Page : transforms
```

### State diagram

````markdown
```mermaid
stateDiagram-v2
  [*] --> Discovered
  Discovered --> Preprocessed
  Preprocessed --> Rendered
  Rendered --> Written
  Written --> [*]
```
````

Renders as:

```mermaid
stateDiagram-v2
  [*] --> Discovered
  Discovered --> Preprocessed
  Preprocessed --> Rendered
  Rendered --> Written
  Written --> [*]
```

### Entity relationship diagram

````markdown
```mermaid
erDiagram
  SITE ||--o{ PAGE : contains
  PAGE ||--o{ TOC_ENTRY : has
  PAGE }o--o{ TAG : "tagged with"
```
````

Renders as:

```mermaid
erDiagram
  SITE ||--o{ PAGE : contains
  PAGE ||--o{ TOC_ENTRY : has
  PAGE }o--o{ TAG : "tagged with"
```

### Pie chart

````markdown
```mermaid
pie title Build time by stage
  "Discovery" : 10
  "Markdown" : 55
  "Templating" : 25
  "Assets" : 10
```
````

Renders as:

```mermaid
pie title Build time by stage
  "Discovery" : 10
  "Markdown" : 55
  "Templating" : 25
  "Assets" : 10
```

### Gantt chart

````markdown
```mermaid
gantt
  title Docs migration
  dateFormat YYYY-MM-DD
  section Content
  Import from MkDocs   :done,    imp, 2024-01-01, 3d
  Fix parity           :active,  par, after imp, 5d
  section Polish
  Theme tweaks         :         thm, after par, 4d
```
````

Renders as:

```mermaid
gantt
  title Docs migration
  dateFormat YYYY-MM-DD
  section Content
  Import from MkDocs   :done,    imp, 2024-01-01, 3d
  Fix parity           :active,  par, after imp, 5d
  section Polish
  Theme tweaks         :         thm, after par, 4d
```

## Tips

- **Keep definitions valid.** If a diagram fails to parse, Mermaid silently leaves the source
  visible rather than breaking the page.
- **Indentation matters** for some diagram types (e.g. `pie`, `gantt`); use spaces, not tabs.
- **Theming.** Mermaid initializes with `startOnLoad: false` and Netdocs drives rendering itself.
  To customize colors, edit `partials/mermaid.html` and pass a `theme`/`themeVariables` object to
  `mermaid.initialize(...)`.

See also the [Code blocks](code-blocks.md) reference for the full list of fence options.
