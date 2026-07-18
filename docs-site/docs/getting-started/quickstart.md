---
title: Quick start
---

# Quick start

This guide scaffolds a minimal Netdocs project and builds it.

## 1. Create the project layout

```text
my-site/
├─ appsettings.json
└─ docs/
   └─ index.md
```

## 2. Write `appsettings.json`

!!! tip "Scaffold it instead"
    Run **`netdocs new`** to generate a fully-annotated `appsettings.json` with every common
    option, sane defaults, and links back to these docs — then edit to taste. See the
    [`netdocs new`](../reference/cli.md#netdocs-new) reference.

The site is configured under the `Netdocs` section:

```json
{
  "Netdocs": {
    "siteName": "My Site",
    "siteUrl": "https://example.com/",
    "docsDir": "docs",
    "siteDir": "site",
    "theme": {
      "name": "material",
      "palette": [
        { "scheme": "default", "primary": "indigo", "accent": "indigo" }
      ],
      "features": [ "navigation.tabs", "content.code.copy" ]
    },
    "nav": [
      { "title": "Home", "path": "index.md" }
    ],
    "plugins": [
      { "name": "search" }
    ],
    "markdownExtensions": [
      { "name": "admonition" },
      { "name": "toc", "options": { "permalink": true } }
    ]
  }
}
```

See **[Configuration](../reference/configuration.md)** for the full schema.

## 3. Add content

```markdown
# Welcome

This is my first **Netdocs** page.

!!! tip
    Admonitions, tabs, code highlighting, and search all work out of the box.
```

## 4. Build

```pwsh
cd my-site
netdocs build
```

The rendered site is written to `site/`. Open `site/index.html` in a browser, or serve
it locally.

## 5. Serve with live reload

```pwsh
netdocs serve --port 8000
```

Netdocs starts a Kestrel dev server, watches the `docs/` directory, and reloads the
browser over a WebSocket whenever a file changes. It automatically picks a free port if
the requested one is taken.

!!! tip "Set up a smoother authoring loop"
    See the **[Authoring environment](authoring-environment.md)** guide for editor setup, putting
    `netdocs` on your `PATH`, and pre-commit checks.

## Next steps

- Explore the **[CLI reference](../reference/cli.md)**.
- Add a **[blog](../plugins/blog.md)**, **[tags](../plugins/tags.md)**, or
  **[social cards](../plugins/social.md)**.
- **[Publish to GitHub Pages](../setup/publishing.md)**.
