namespace Netdocs.Cli;

/// <summary>
/// <c>netdocs new</c> — scaffolds an annotated <c>appsettings.json</c> with every common option,
/// sane defaults, and inline links back to the docs site. Comments and trailing commas are legal
/// because <see cref="Netdocs.Core.Configuration.JsonConfigLoader"/> parses with
/// <c>JsonCommentHandling.Skip</c> / <c>AllowTrailingCommas</c>.
/// </summary>
internal static class NewCommand
{
    private const string DocsBase = "https://xtremeownage.github.io/Netdocs";

    public static async Task<int> RunAsync(string[] args)
    {
        // netdocs new [path/to/appsettings.json] [--force]
        var target = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'))
            ?? Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        target = Path.GetFullPath(target);
        var force = args.Contains("--force");

        if (File.Exists(target) && !force)
        {
            Console.Error.WriteLine($"Refusing to overwrite existing {target}. Pass --force to replace it.");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, Template);
        Console.WriteLine($"Wrote annotated config -> {target}");
        Console.WriteLine("Edit the values (siteName, siteUrl, repoUrl…), add your docs/ pages, then run: netdocs build");
        return 0;
    }

    private static readonly string Template = $$"""
{
  // Netdocs configuration. This file is JSONC: // comments and trailing commas are allowed.
  // Full reference: {{DocsBase}}/reference/configuration/
  "Logging": {
    "LogLevel": { "Default": "Information" }
  },

  "Netdocs": {
    // --- Site identity ---------------------------------------------------------------
    "siteName": "My Project",
    "siteUrl": "https://example.github.io/my-project/", // used for absolute URLs, sitemap, feeds
    "siteAuthor": "Your Name",
    "siteDescription": "Documentation for My Project.",
    "copyright": "© 2025 Your Name",

    // Repo links power the header icon and the per-page Edit/View buttons.
    "repoUrl": "https://github.com/you/my-project",
    "repoName": "you/my-project",
    "editUri": "edit/main/docs/", // appended to repoUrl for the "Edit this page" button

    "docsDir": "docs", // source markdown folder
    "siteDir": "site", // build output folder

    // --- Theme -----------------------------------------------------------------------
    // Reference: {{DocsBase}}/reference/theme/
    "theme": {
      "name": "material",
      "language": "en",
      // "logo": "assets/logo.svg",
      // "favicon": "assets/favicon.png",
      // "customDir": "overrides", // drop-in template/partial overrides (Scriban)
      "highlight": "highlightjs", // "highlightjs" | "none" | custom (bring your own)
      "features": [
        "navigation.instant",
        "navigation.tabs",
        "navigation.sections",
        "navigation.top",
        "navigation.footer",
        "navigation.indexes",
        // "navigation.path",        // breadcrumbs
        "toc.follow",
        "content.code.copy",
        "content.code.annotate",
        "content.tooltips",
        "content.footnote.tooltips",
        "search.highlight",
        "search.suggest",
        "content.action.edit",       // requires editUri above
        "content.action.view"
      ],
      // Light/dark palette with a header toggle.
      "palette": [
        { "media": "(prefers-color-scheme: light)", "scheme": "default", "primary": "indigo", "accent": "indigo",
          "toggleIcon": "material/brightness-7", "toggleName": "Switch to dark mode" },
        { "media": "(prefers-color-scheme: dark)", "scheme": "slate", "primary": "indigo", "accent": "indigo",
          "toggleIcon": "material/brightness-4", "toggleName": "Switch to light mode" }
      ]
    },

    // --- Navigation ------------------------------------------------------------------
    // Omit `nav` to auto-generate from the docs folder structure.
    "nav": [
      { "path": "index.md" },
      {
        "title": "Getting started",
        "children": [
          { "path": "getting-started/installation.md" },
          { "path": "getting-started/quickstart.md" }
        ]
      }
    ],

    // --- Markdown extensions ---------------------------------------------------------
    // Reference: {{DocsBase}}/reference/markdown-extensions/
    "markdownExtensions": [
      { "name": "admonition" },
      { "name": "attr_list" },
      { "name": "md_in_html" },
      { "name": "footnotes" },
      { "name": "toc", "options": { "permalink": true } },
      { "name": "pymdownx.details" },
      { "name": "pymdownx.superfences" },
      { "name": "pymdownx.tabbed", "options": { "alternate_style": true } },
      { "name": "pymdownx.highlight", "options": { "line_spans": "__span" } },
      { "name": "pymdownx.tasklist", "options": { "custom_checkbox": true } },
      { "name": "pymdownx.emoji" },
      { "name": "pymdownx.keys" },
      { "name": "pymdownx.critic" }
    ],

    // --- Plugins ---------------------------------------------------------------------
    // Reference: {{DocsBase}}/plugins/  — remove any you don't need.
    "plugins": [
      { "name": "search", "options": { "lang": "en" } },
      { "name": "meta" },
      { "name": "tags", "options": { "export": true } },
      // { "name": "blog" },                 // enable if you have a docs/blog/ folder
      // { "name": "rss", "options": { "atom": true, "social_icon": true } },
      // { "name": "git-revision-date-localized" },
      // { "name": "redirects" },
      // { "name": "glightbox" },
      // { "name": "macros" },               // fileuri()/button()/download() macros
      // { "name": "table-reader" },         // read_csv()/read_table()
      // { "name": "arithmatex" },           // LaTeX math
      // { "name": "social" }                // OG cards; best behind `--prod`
    ],

    // --- Extra: social links, custom vars --------------------------------------------
    "extra": {
      "social": [
        { "icon": "fontawesome/brands/github", "link": "https://github.com/you/my-project" }
      ]
    },

    // --- URL slugs -------------------------------------------------------------------
    "slugify": { "case": "lower", "separator": "-", "ascii": false },

    // --- Output optimization ---------------------------------------------------------
    "optimize": {
      "minifyHtml": false,
      "minifyCss": false,
      "minifyJs": false,
      "convertImagesToWebp": false,
      "webpQuality": 80,
      // Self-host every CDN asset (highlight.js, Mermaid, fonts, emoji) so the built
      // site runs from file://. Requires network access at build time. Omit for the
      // default (self-host on production builds only); set true/false to force it.
      "offline": null
    },

    // --- Build-time validation -------------------------------------------------------
    // Reference: {{DocsBase}}/reference/validation/  — pair with `netdocs build --strict`.
    "validation": {
      "links": false,       // broken internal links
      "anchors": false,     // broken #fragment anchors (requires links)
      "unusedImages": false,
      "orphanPages": false
    },

    // --- Deploy (optional) -----------------------------------------------------------
    // target: "none" | "filesystem" | "git" | "s3"  — run `netdocs deploy`.
    "deploy": {
      "target": "none"
      // "target": "git", "branch": "gh-pages", "remote": "origin"
    }
  }
}

""";
}
