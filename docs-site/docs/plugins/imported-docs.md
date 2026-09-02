---
title: Imported Docs
---

# Imported Docs

The **Imported Docs** plugin enables **federated documentation** — external repositories can contribute documentation to your main docs site while keeping their docs at source. This plugin supports both **push-based** (external repos push to you) and **pull-based** (you pull from external repos) approaches.

It implements `IImportHook.OnImportAsync`, which runs after content discovery and before navigation filtering. That gives imported pages the same downstream processing as native docs: navigation, preprocessing, rendering, and templates all see the imported pages as if they lived in the site tree from the start.

## Quick Start

### Minimal Configuration

Enable the plugin in your `appsettings.json`, and configure it under `importedDocs` in the
same `Netdocs` section:

```json
{
  "Netdocs": {
    "plugins": [
      { "name": "imported-docs" }
    ],
    "importedDocs": {
      "pushedDocsDir": "imported"
    }
  }
}
```

This enables push-based imports. External repos can push documentation to the `/imported` directory.

`importedDocs` is read by the JSON config loader, so a site still running from `mkdocs.yml`
needs to move to `appsettings.json` to use this plugin.

## Import hook behavior

`imported-docs` is the built-in plugin that implements `IImportHook.OnImportAsync`.

- It runs after source docs are discovered.
- It can add pages directly into `site.Pages`.
- Imported pages then flow through the normal build pipeline.
- Use it for push-based staging, Git pull imports, or S3 pull imports.

## Configuration Reference

### Top-Level Settings

```json
{
  "Netdocs": {
    "importedDocs": {
      "pushedDocsDir": "imported",
      "pullSources": [
        // ... pull source definitions
      ]
    }
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `pushedDocsDir` | string | `"imported"` | Directory where external repos push docs. Created as a subdirectory in your project root. |
| `pullSources` | array | `[]` | List of external repositories to pull from (see below). |
| `s3Sources` | array | `[]` | List of S3 buckets to pull documentation from (see below). |

### Pull Source Configuration

Each pull source is a repository to pull documentation from:

```json
{
  "repository": "https://github.com/owner/repo.git",
  "reference": "main",
  "sourcePath": "docs",
  "destinationPath": "external/my-project",
  "authTokenEnvVar": "GITHUB_TOKEN",
  "includeSourceMarker": true,
  "exclude": ["draft/**", "*.tmp"],
  "frontMatterDefaults": {
    "nav_title": "My Project Docs",
    "hide": false
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `repository` | string | _(required)_ | Git repository URL (https or ssh). |
| `reference` | string | _(optional)_ | Branch, tag, or commit SHA to checkout. If omitted, uses default branch. |
| `sourcePath` | string | `"docs"` | Subdirectory within the repo containing markdown files. |
| `destinationPath` | string | _(required)_ | Path on main site where imported docs appear (e.g., `"products/api"` → `/products/api/`). |
| `authTokenEnvVar` | string | _(optional)_ | Environment variable containing auth token for private repos. |
| `includeSourceMarker` | bool | `false` | If `true`, adds `import_source` and `import_url` metadata to imported pages. Useful for displaying "view source" links. |
| `exclude` | array | `[]` | Glob patterns for files to exclude (e.g., `["draft/**", "INTERNAL-*.md"]`). Supports `*` (segment) and `**` (any dirs). |
| `frontMatterDefaults` | object | `{}` | Front-matter key-value pairs to apply as fallback for imported pages. Extracted values take precedence. |

### S3 Source Configuration

Pull documentation directly from S3 buckets (no git clone overhead):

```json
{
  "bucket": "my-docs-bucket",
  "prefix": "docs/",
  "region": "us-east-1",
  "destinationPath": "products/external",
  "credentialsEnvVar": "AWS_CREDENTIALS",
  "includeSourceMarker": true,
  "exclude": ["*.draft.md", "private/**"],
  "frontMatterDefaults": {
    "nav_title": "External Docs"
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `bucket` | string | _(required)_ | S3 bucket name (e.g., `"my-docs-bucket"`). |
| `prefix` | string | _(required)_ | S3 object prefix/folder (e.g., `"docs/"` or `"external-docs/api/"`). Only objects under this prefix are imported. |
| `region` | string | _(required)_ | AWS region (e.g., `"us-east-1"`, `"eu-west-1"`). |
| `destinationPath` | string | _(optional)_ | Path on main site where imported docs appear (e.g., `"products/api"`). |
| `credentialsEnvVar` | string | _(optional)_ | Environment variable containing AWS credentials in format `"ACCESS_KEY:SECRET_KEY"`. If omitted, uses default AWS credential chain (IAM role, `~/.aws/credentials`, env vars). |
| `includeSourceMarker` | bool | `false` | If `true`, adds `import_source` with S3 URL and `import_url` metadata. |
| `exclude` | array | `[]` | Glob patterns for files to exclude. |
| `frontMatterDefaults` | object | `{}` | Front-matter key-value pairs to apply as fallback. |

## Use Cases

### Push-Based: External Repo Workflow

**Scenario**: Your organization has multiple repositories. Each repo maintains its own documentation, and you want it to appear on your main docs site.

**Flow**:
1. External repo has docs in `./docs/` directory
2. External repo GitHub Action triggers on docs changes
3. Action clones main docs repo, copies files to `/imported/{project-name}/`, commits and pushes
4. Next build of main docs site picks up the changes

**External Repo Workflow Example** (`.github/workflows/push-docs.yml`):

```yaml
name: Push docs to main site

on:
  push:
    branches: [main]
    paths:
      - 'docs/**'

env:
  GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

jobs:
  push-docs:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout this repo
        uses: actions/checkout@v4

      - name: Checkout docs repo
        uses: actions/checkout@v4
        with:
          repository: myorg/main-docs
          token: ${{ secrets.GITHUB_TOKEN }}
          path: ./docs-site

      - name: Copy docs
        run: |
          mkdir -p ./docs-site/imported/my-project
          cp -r ./docs/* ./docs-site/imported/my-project/

      - name: Push to docs repo
        working-directory: ./docs-site
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"
          git add imported/
          git commit -m "docs: push docs from my-project"
          git push
```

### Pull-Based: Scheduled Sync

**Scenario**: You want to automatically pull documentation from multiple external repositories on a schedule, without requiring changes to those repos.

**Flow**:
1. Configure `pullSources` in main docs `appsettings.json`
2. Main docs repo has scheduled GitHub Action
3. Action runs build, which pulls from configured external repos
4. Imported docs integrated into main site

**Main Docs Repo Workflow** (`.github/workflows/scheduled-build.yml`):

```yaml
name: Scheduled build with external docs

on:
  schedule:
    # Daily build at 2 AM UTC
    - cron: '0 2 * * *'
  workflow_dispatch:  # Manual trigger

env:
  GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'

      - name: Build with imported docs
        run: |
          dotnet build -c Release
          dotnet run --project src/Netdocs.Cli -- build

      - name: Deploy
        run: |
          # Your deployment logic here
          echo "Deploying built site..."
```

**Main Docs Config** (`appsettings.json`):

```json
{
  "Netdocs": {
    "plugins": [
      { "name": "imported-docs" }
    ],
    "importedDocs": {
      "pushedDocsDir": "imported",
      "pullSources": [
        {
          "repository": "https://github.com/myorg/api-repo.git",
          "sourcePath": "docs",
          "destinationPath": "products/api",
          "exclude": ["README.md", "CONTRIBUTING.md"]
        },
        {
          "repository": "https://github.com/myorg/cli-repo.git",
          "sourcePath": "docs",
          "destinationPath": "products/cli",
          "reference": "v2.x"
        }
      ]
    }
  }
}
```

### S3-Based: Direct S3 Bucket Import

**Scenario**: You want to pull documentation directly from S3 buckets without requiring git operations. Faster and simpler for large documentation sets.

**Flow**:
1. External repos upload markdown to a shared S3 bucket (via CI/CD or manually)
2. Configure S3 bucket paths in main docs `appsettings.json`
3. Main docs build downloads files directly from S3
4. Imported docs integrated into main site

**Main Docs Config** (`appsettings.json`):

```json
{
  "Netdocs": {
    "plugins": [
      { "name": "imported-docs" }
    ],
    "importedDocs": {
      "s3Sources": [
        {
          "bucket": "shared-docs",
          "prefix": "api-docs/",
          "region": "us-east-1",
          "destinationPath": "products/api",
          "exclude": ["draft/**", "*.internal.md"]
        },
        {
          "bucket": "shared-docs",
          "prefix": "sdk-docs/",
          "region": "us-east-1",
          "destinationPath": "products/sdk"
        }
      ]
    }
  }
}
```

**Example External Repo Workflow** (to upload to S3):

```yaml
name: Upload docs to S3

on:
  push:
    branches: [main]
    paths:
      - 'docs/**'

env:
  AWS_REGION: us-east-1
  S3_BUCKET: shared-docs
  S3_PREFIX: api-docs/

jobs:
  upload:
    runs-on: ubuntu-latest
    permissions:
      id-token: write
    steps:
      - uses: actions/checkout@v4

      - name: Configure AWS credentials
        uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: arn:aws:iam::ACCOUNT_ID:role/GithubActionsRole
          aws-region: ${{ env.AWS_REGION }}

      - name: Upload to S3
        run: |
          aws s3 sync ./docs s3://${{ env.S3_BUCKET }}/${{ env.S3_PREFIX }} \
            --delete \
            --exclude ".git/*" \
            --exclude "node_modules/*"
```

## Authentication

### Public Repositories

No authentication needed. Simply omit `authTokenEnvVar`:

```json
{
  "repository": "https://github.com/public/repo.git",
  "sourcePath": "docs",
  "destinationPath": "products/repo"
}
```

### Private Repositories

Use GitHub tokens (or other git credentials) via environment variables.

#### GitHub Token (Recommended)

In your GitHub Actions workflow, use the default `GITHUB_TOKEN`:

```yaml
env:
  GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

In config:

```json
{
  "repository": "https://github.com/private/repo.git",
  "authTokenEnvVar": "GITHUB_TOKEN",
  "sourcePath": "docs",
  "destinationPath": "products/repo"
}
```

The plugin uses the token as OAuth2 credentials (username="oauth2", password=token).

#### Personal Access Token

Create a Personal Access Token (PAT) with `repo` scope in GitHub Settings → Developer settings → Personal access tokens.

Store as repository secret (e.g., `DOCS_PAT`):

```yaml
env:
  GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  DOCS_PAT: ${{ secrets.DOCS_PAT }}
```

Config:

```json
{
  "repository": "https://github.com/private/repo.git",
  "authTokenEnvVar": "DOCS_PAT",
  "sourcePath": "docs",
  "destinationPath": "products/repo"
}
```

#### SSH Keys

For SSH-based authentication, ensure your CI/CD environment has the SSH private key configured (e.g., via GitHub Secrets + ssh-agent):

```yaml
- name: Setup SSH
  uses: webfactory/ssh-agent@v0.5.4
  with:
    ssh-private-key: ${{ secrets.DOCS_SSH_KEY }}
```

Use SSH repository URL:

```json
{
  "repository": "git@github.com:private/repo.git",
  "sourcePath": "docs",
  "destinationPath": "products/repo"
}
```

No `authTokenEnvVar` needed — git will use SSH agent.

### S3 Bucket Authentication

For S3-based imports, credentials can come from multiple sources (in order of precedence):

1. **Environment variable** (format: `ACCESS_KEY:SECRET_KEY`)
2. **IAM role** (automatic on AWS EC2, Lambda, or GitHub Actions OIDC)
3. **`~/.aws/credentials`** file
4. **Environment variables** (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`)

#### Using Environment Variables (Simple)

Store credentials as a GitHub secret and pass via environment:

```yaml
env:
  AWS_CREDENTIALS: ${{ secrets.AWS_CREDENTIALS }}
```

In config:

```json
{
  "bucket": "shared-docs",
  "prefix": "docs/",
  "region": "us-east-1",
  "credentialsEnvVar": "AWS_CREDENTIALS",
  "destinationPath": "products/docs"
}
```

#### Using IAM Role (Recommended)

For AWS-hosted or OIDC-enabled workflows, use IAM roles instead of credentials:

```yaml
permissions:
  id-token: write

steps:
  - name: Configure AWS credentials
    uses: aws-actions/configure-aws-credentials@v4
    with:
      role-to-assume: arn:aws:iam::ACCOUNT_ID:role/GithubActionsRole
      aws-region: us-east-1
```

Then omit `credentialsEnvVar` in config and the plugin uses the assumed role automatically.

## Front-Matter Overrides

The plugin supports merging front-matter from two sources:

1. **Extracted from imported markdown files** (highest priority)
2. **Config `frontMatterDefaults`** (fallback)

This allows your main docs site to set default values for imported documentation without overriding values the external repo explicitly set.

### Example

**External repo's `docs/guide.md`**:

```markdown
---
title: Getting Started
nav_sort: 10
---

# Getting Started

...
```

**Main docs config**:

```json
{
  "repository": "https://github.com/external/repo.git",
  "sourcePath": "docs",
  "destinationPath": "products/external",
  "frontMatterDefaults": {
    "nav_title": "External Product",
    "hide": false,
    "nav_sort": 999
  }
}
```

**Result**: The imported page will have:
- `title: "Getting Started"` ← from external markdown
- `nav_title: "External Product"` ← from config default (external didn't specify)
- `hide: false` ← from config default
- `nav_sort: 10` ← from external markdown (not overridden)

## File Exclusion

Use glob patterns to exclude files from import. Patterns match relative to the source docs path.

**Pattern Syntax**:
- `*` matches any characters within a path segment (doesn't cross `/`)
- `**` matches any characters across multiple segments
- `?` matches a single character
- `{a,b}` matches either `a` or `b`

**Examples**:

```json
{
  "exclude": [
    "draft/**",           // Exclude entire draft/ directory
    "*.tmp",              // Exclude .tmp files
    "INTERNAL-*.md",      // Exclude files starting with INTERNAL-
    "**/todo.md",         // Exclude todo.md in any directory
    "{private,secret}/**" // Exclude private/ and secret/ directories
  ]
}
```

Files matching any pattern are skipped during import.

## Source Markers

If `includeSourceMarker` is `true`, the plugin adds metadata to each imported page:

```yaml
---
import_source: "https://github.com/external/repo/blob/main/docs/guide.md"
import_url: "/products/external/guide/"
---
```

This metadata is available in your templates for displaying "View on GitHub" or similar links:

```html
{% if page.frontmatter.import_source %}
<a href="{{ page.frontmatter.import_source }}">View source</a>
{% endif %}
```

## URL Mapping

Imported files are mapped to URLs with the same rules discovered pages use, so an imported
tree keeps its shape and its relative cross-links keep resolving.

| Source file | `destinationPath` | URL |
|---|---|---|
| `guide.md` | `products/api` | `/products/api/guide/` |
| `integrations/citrix.md` | `products/api` | `/products/api/integrations/citrix/` |
| `index.md` | `products/api` | `/products/api/` |
| `integrations/index.md` | `products/api` | `/products/api/integrations/` |
| `guide.md` | _(omitted)_ | `/guide/` |

Behavior:

- The `.md` extension is removed and a trailing slash is added.
- Directories below the source path are preserved beneath `destinationPath`.
- `index.md` and `README.md` collapse onto their containing directory.
- When the site sets `slugify.urls`, imported segments are slugified too.

## Build Pipeline Integration

The Imported Docs plugin runs at **Stage 2** of the build pipeline — after initial content discovery but before navigation filters and rendering. This ensures:

- Imported docs are discovered early
- All existing plugins can process imported pages
- Navigation generation includes imported docs
- No special handling needed elsewhere

See [Build lifecycle](../development/lifecycle.md) for the full pipeline diagram.

## Troubleshooting

### Pull source fails to clone

**Error**: `Failed to clone repository`

**Causes**:
- Repository URL is incorrect
- Repository is private and `authTokenEnvVar` is missing or invalid
- Git credentials not configured (SSH key not loaded)
- Network connectivity issue

**Solution**:
- Verify repository URL
- For private repos, set `authTokenEnvVar` and ensure env var is populated
- For SSH, verify SSH key is available in CI/CD environment
- Check build logs for details

### Files not appearing

**Causes**:
- `sourcePath` doesn't exist in the repository
- All files match `exclude` patterns
- Reference (branch/tag) doesn't exist

**Solution**:
- Verify `sourcePath` exists in the repository
- Check `exclude` patterns with glob tester
- Verify `reference` exists on remote

### Build succeeds but no docs appear

**Check**:
1. Is the plugin enabled in `plugins` array?
2. Does `importedDocs` section exist in config?
3. Check build logs for import activity (look for "Imported X pages" messages)
4. Verify `destinationPath` is correct and doesn't conflict with existing content

## Performance

- **Push-based**: No performance impact. Only processes files pushed to staging directory.
- **Pull-based (Git)**: Clones repositories to temp directory on each build. For large repos or frequent builds, consider:
  - Using scheduled workflows (fewer builds)
  - Shallow clones with specific branches/tags
  - Filtering large repos with `exclude` patterns
- **Pull-based (S3)**: Direct download from S3 bucket without git operations. Generally faster than git clones, especially for large doc sets:
  - Parallel object listing for efficient bucket scanning
  - Configurable credentials via IAM role or environment variables
  - No temporary directory cleanup overhead (streaming downloads)
  - Suitable for frequently-updated doc sources

## See Also

- [Build lifecycle](../development/lifecycle.md) — where the import hook runs
- [Events & callbacks](../development/events-and-callbacks.md) — full hook reference
- [External plugins](../development/external-plugins.md) — building custom import plugins
