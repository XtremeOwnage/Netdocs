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

## Built-in deploy targets

Instead of wiring up a workflow, `netdocs` can publish the build itself. Add a `deploy`
section to the `Netdocs` block of `appsettings.json` and run `netdocs deploy` (which builds
first, then publishes), or `netdocs build --deploy`.

### Filesystem

Copy the built site to a local (or mounted) directory — handy for nginx web roots or a
synced folder:

```json
{
  "Netdocs": {
    "deploy": {
      "target": "filesystem",
      "path": "/var/www/docs",
      "clean": true
    }
  }
}
```

- `path` — destination directory (absolute, or relative to the project root).
- `clean` — when `true` (default), files at the destination that the build no longer
  produces are pruned.

### Git branch

Publish the output to a branch (e.g. `gh-pages`) using a temporary git worktree, so your
main working tree is never touched:

```json
{
  "Netdocs": {
    "deploy": {
      "target": "git",
      "branch": "gh-pages",
      "remote": "origin",
      "message": "Deploy docs",
      "push": true
    }
  }
}
```

- The branch is created (as an orphan) on first deploy if it does not exist.
- Set `push` to `false` to commit locally without pushing.
- Requires `git` on `PATH` and the project to be inside a git repository.

```bash
# Build and publish in one step
netdocs deploy --config appsettings.json
```

### AWS S3

Sync the output to an S3 bucket for an AWS-hosted static site. This shells out to the
[AWS CLI](https://docs.aws.amazon.com/cli/) (`aws s3 sync`), so credentials and region are
resolved the standard AWS way (environment variables, `~/.aws/config`, or an instance role):

```json
{
  "Netdocs": {
    "deploy": {
      "target": "s3",
      "bucket": "my-docs-bucket",
      "prefix": "docs",
      "region": "us-east-1",
      "clean": true
    }
  }
}
```

- `bucket` is required; `prefix` (optional) publishes under a sub-path of the bucket.
- `clean: true` passes `--delete` so objects no longer produced by the build are removed.
- `region` is optional — omit it to use the AWS CLI's configured default.
- Requires the AWS CLI (`aws`) on `PATH`.

## Optimization

Enable HTML minification to shrink emitted pages (whitespace collapse + comment removal,
preserving `pre`/`code`/`script`/`style`), plus optional CSS and JavaScript minification of
copied assets:

```json
{
  "Netdocs": {
    "optimize": {
      "minifyHtml": true,
      "minifyCss": true,
      "minifyJs": true,
      "convertImagesToWebp": true,
      "webpQuality": 80
    }
  }
}
```

`minifyCss`/`minifyJs` strip comments and collapse whitespace in `.css`/`.js` assets as they
are copied into the output. The minifier is conservative — it never renames identifiers or
reorders rules, and it preserves string, template, and URL contents verbatim. Files that are
already minified (`*.min.css`, `*.min.js`) are copied through untouched.

### WebP image conversion

`convertImagesToWebp` emits a `.webp` sibling next to every copied `.png`/`.jpg`/`.jpeg`
image and rewrites local `<img>` references into a `<picture>` element:

```html
<picture>
  <source srcset="images/diagram.webp" type="image/webp">
  <img src="images/diagram.png" alt="Diagram">
</picture>
```

This is **non-destructive** — the original raster file is kept and referenced as the
`<img>` fallback, so browsers without WebP support still render correctly while modern
browsers download the smaller WebP. `webpQuality` (1–100, default `80`) controls the
encoder quality. Conversions are cached by source content hash under `.cache/webp/`, so
unchanged images are not re-encoded on subsequent builds. Remote images (`http://`,
`https://`, `//`, `data:`), SVGs, and images already in WebP format are left untouched.

## Other hosts

The `site/` output is static HTML/CSS/JS. Upload it to any static host (Netlify, S3 +
CloudFront, nginx, etc.). Point the host at the build output directory.
