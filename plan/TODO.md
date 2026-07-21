# Netdocs — Consolidated TODO

Single source of truth for remaining work, consolidated from everything under `plan/`
(`dotnet-ssg-plan.md`, `Deployment-Docs.md`, `Search.md`, `tags-page.md`, `Set-Remote.md`).

The core engine (config, discovery, Markdig pipeline, plugin host, Scriban Material
theme, build + serve) is implemented and builds the existing `Web/` blog with real
Material look, working lunr search, and social cards. Repo is on GitHub with CI.

---

## ✅ Done

**Engine & CLI**
- `netdocs build` / `serve` (Kestrel + WebSocket live reload + FileSystemWatcher; auto-picks a free port).
- Config from **`appsettings.json`** (`Netdocs` section) via `Microsoft.Extensions.Configuration`.
- Logging via the standard `Logging` section; `--verbose` = Trace; Debug/Trace across pipeline stages.
- Parallel render pipeline; `exclude` glob option; content-page discovery honoring `.mkdocsignore`.
- Relative `*.md` link rewriting to output URLs.
- Built-in **`sitemap.xml`**.
- F5 debug (VS Code `launch.json`/`tasks.json` + VS `launchSettings.json`).

**Theme (real Material, vendored MIT-attributed assets)**
- Ported base + partials (header/nav/toc/tabs/footer/search) to Material DOM with `#__config`.
- Vendored Material `main`/`palette` CSS, `bundle.js`, lunr search worker + languages.
- Client-side **syntax highlighting** (highlight.js) **with line numbers**.
- Page date footer; prev/next footer navigation; OG/twitter meta tags.

**Plugins**
- **search** — Material index schema (fields/pipeline/separator + per-section docs); lunr search verified.
- **social** — ImageSharp 1200×630 OG cards (palette-colored), cached, build/prod only.
- **blog** — post URL rewrite, paginated index, category + archive pages, RSS, post meta header (date · read-time · categories), folder-derived categories.
- **tags** — hierarchical index into `<!-- material/tags -->` marker, shadow-tag filtering, `tags.json` export.
- **abbreviations** — dedicated plugin appending abbreviation files (`<abbr>` tooltips).
- **snippets** — `--8<--` includes + base paths.
- **git-revision-date** — real dates via single-pass LibGit2Sharp walk (filesystem fallback; skipped on serve).
- **redirects** — client-side redirect map (fixes `/discord`).
- **meta** — per-directory `.meta.yml` defaults.
- **glightbox** — image lightbox (assets + init).

**Repo / infra**
- Pushed to `git@github.com:XtremeOwnage/Netdocs.git` (SSH key `~/.ssh/xo`).
- MIT `LICENSE` + full credit to squidfunk; `THIRD_PARTY_LICENSES/`; README with attribution + AI-derivative disclaimer.
- `dependabot.yml` (nuget + github-actions); **CI** workflow (build + test).

---

## 🚧 Remaining work

### Documentation & distribution (from `Deployment-Docs.md`)
- [ ] **Usage docs** — how to install, configure (`appsettings.json` schema), run (`build`/`serve`), and theme/override.
- [ ] **Publish workflow** — example GitHub Actions workflow that builds the site and deploys to `gh-pages` (or artifact).
- [ ] **Packaging** — workflow to build **apt** (`.deb`) and **yum** (`.rpm`) packages of the `netdocs` CLI.
- [ ] **Docker** — workflow/Dockerfile to build a container image of the CLI.

### Tags page (from `tags-page.md`)
- [ ] Keep the current tags layout (liked). Add **per-page custom display name** on the tags page via metadata
      (e.g. a front-matter `tags_title` / `title` override) so authors control how a post shows in the tag list.

### Search (from `Search.md`)
- [ ] **Reconsider lunr** — user flagged lunr "might not be the best idea" (ref: squidfunk/mkdocs-material#6307).
      Evaluate alternatives / prebuilt index / server-side search; keep the Material UI either way.

### Content / theme features
- [ ] **Blog authors** — `.authors.yml`, author avatar/name on posts, author pages (currently 1 author, 0 post usage).
- [ ] **file-filter** — env-driven label include/exclude + nav pruning (currently pass-through).
- [ ] **table-reader** — embed CSV/data as tables.
- [ ] **typeset** — smart typography (currently no-op).
- [ ] **Jinja→Scriban override porting** — `docs/overrides/**` is Material Jinja and is auto-ignored; port to Scriban to re-enable `custom_dir`.
- [ ] **Emoji** → Twemoji SVG (currently Markdig unicode emoji).
- [ ] **Keys** (`++ctrl+c++`) and **Critic** (`{++ ++}`/`{-- --}`/`{~~ ~>~~}`) inline extensions (low usage).
- [ ] **Content tabs linked** (`content.tabs.link`).
- [ ] **Navigation** polish — breadcrumbs (`navigation.path`), section index pages (`navigation.indexes`), active-trail expansion; extend prev/next to blog posts.
- [ ] **Highlight extras** — anchor linenums / inline linenums parity (pymdownx options).
- [ ] **Macros** (`main.py` port) — `fileuri()` / `ebay()` via Scriban, honor `render_by_default:false` (currently 0 usage in content).

### Engine / infra
- [ ] **Incremental build cache** — content-hash sources/templates/plugin inputs; skip unchanged (`.cache/` manifest). Speeds warm builds (currently ~13s prod with git dates + social cards).
- [ ] **External plugin loading** — `./plugins/*.dll` via `AssemblyLoadContext`.
- [ ] **Config** — honor all env flags (`IS_LOCAL_BUILD`, `MKDOCS_*`), strict mode, `!!python/...` slugify mapping.
- [ ] **Scriban advisories** — review Scriban 6.2.1 GHSA advisories / pin a patched version (`NuGetAudit` currently disabled to reduce noise).
- [ ] **Tests** — golden-file Markdown extension tests + parity diff vs MkDocs output (search index, sitemap, RSS).

---

## Notes
- `.NET 11` (preview) pinned via `global.json`; solution is `.slnx`; output centralized under `artifacts/` (`UseArtifactsOutput`).
- Build ~2.2s (serve/dev) / ~13s (prod, git dates + social cards; both cached & build-only).
- `Web/appsettings.json` (in the separate `Web` repo) carries the site's plugin/theme config.
