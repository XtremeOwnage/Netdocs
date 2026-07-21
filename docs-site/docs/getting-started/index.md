---
title: Getting started
---

# Getting started

Netdocs turns a folder of Markdown into a Material-styled static website. This section
walks you through installing the CLI, scaffolding a project, and running your first
build.

1. **[Installation](installation.md)** — build from source or install a packaged CLI.
2. **[Quick start](quickstart.md)** — create `appsettings.json`, add content, build and serve.

## The mental model

A Netdocs project is a directory that contains:

- an **`appsettings.json`** with a `Netdocs` section (site metadata, theme, nav, plugins);
- a **`docs/`** directory of Markdown content (configurable via `docsDir`);
- an output **`site/`** directory that Netdocs writes (configurable via `siteDir`).

The build pipeline discovers content, runs Markdown through Markdig with the configured
extensions, applies plugins, renders each page through the Scriban Material theme, and
writes HTML plus a search index, sitemap, and any plugin outputs.
