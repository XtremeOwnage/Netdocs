---
title: Publishing
---

# Publishing to GitHub Pages

Netdocs builds a plain static site, so it deploys anywhere that serves files. The
repository ships a **GitHub Actions** workflow that builds this documentation site and
publishes it to GitHub Pages.

## The workflow

`.github/workflows/docs.yml` builds the CLI, runs `netdocs build` against
`docs-site/appsettings.json`, and uploads the output as a Pages artifact:

```yaml
name: Docs

on:
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: true

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0   # git-revision-date needs full history

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Build docs
        run: dotnet run --project src/Netdocs.Cli -c Release -- build --prod --config docs-site/appsettings.json

      - uses: actions/upload-pages-artifact@v3
        with:
          path: docs-site/site

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

## One-time setup

1. In the repository **Settings → Pages**, set **Source** to **GitHub Actions**.
2. Set `siteUrl` in `docs-site/appsettings.json` to your Pages URL
   (e.g. `https://<user>.github.io/<repo>/`).
3. Push to `main` — the workflow builds and deploys automatically. You can also trigger it
   manually via **Run workflow** (`workflow_dispatch`).

## Publishing your own site

Use the same pattern: point the `--config` at your site's `appsettings.json` and upload
its `siteDir`. For a project-page URL under a subpath, make sure `siteUrl` includes the
subpath so links, the sitemap, and social cards resolve correctly.

## Other hosts

The `site/` output is static HTML/CSS/JS. Upload it to any static host (Netlify, S3 +
CloudFront, nginx, etc.). Point the host at the build output directory.
