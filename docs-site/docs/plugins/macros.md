---
title: macros
---

# macros

A minimal port of [mkdocs-macros]. The macros plugin lets you inject dynamic content into
your Markdown *before* it is parsed. It ships two example function macros (`fileuri` and
`button`), lets you define your own **variables** in config, and — for anything richer —
documents how to add a macro by writing a small plugin.

Macros are expanded as a Markdown **preprocessor**, so their output is parsed as normal
Markdown/HTML afterwards.

[mkdocs-macros]: https://mkdocs-macros-plugin.readthedocs.io/

## Why a macros plugin?

Static Markdown can't do "compute this value once and reuse it everywhere". Macros close
that gap: define a value (a version string, a support email, a base URL) in one place and
reference it as `{{ token }}` throughout your docs, or call a helper macro that expands to
a chunk of HTML. The whole point of the plugin is to be **extensible** — the built-ins are
just examples of the pattern.

## Built-in macros

### `fileuri("name")`

Resolves a file to its published URL, prefixed with `site_url`. A page in the **same
directory** as the current page is preferred; otherwise the first file with a matching
name (any page, then any static asset under the docs directory) wins.

```markdown
See the [installation guide]({{ fileuri("install.md") }}).
```

If the file cannot be found, an HTML comment is emitted in its place so the build does
not fail.

#### Choosing the URI form

Pass an optional second argument to control how the URL is written:

| Mode | Example output | When to use |
|---|---|---|
| *(omitted)* / `absolute` / `url` | `https://site/guides/install/` (or `/guides/install/` when no `site_url`) | Default. Feeds, sitemaps, canonical/absolute links. |
| `path` / `root` | `/guides/install/` | A root-absolute path that never bakes in the host. |
| `relative` / `rel` | `../guides/install/` | A page-relative link that stays correct when the site is served under a base path (e.g. GitHub project Pages at `/Repo/`). |

```markdown
Absolute: {{ fileuri("install.md") }}
Root path: {{ fileuri("install.md", "path") }}
Relative: {{ fileuri("install.md", "relative") }}
```

### `button("text", "url")`

Renders a Material-styled call-to-action button (`.md-button`) linking to the given URL.

```markdown
{{ button("Get started", "getting-started/") }}
```

## Writing your own macros

### Option 1 — Variables (no code)

Define `variables` in the plugin config. Each key becomes a `{{ key }}` token that expands
to its value anywhere macros run. This is the simplest way to add your own macros:

```json title="appsettings.json"
{
  "name": "macros",
  "options": {
    "variables": {
      "product": "Netdocs",
      "latest_version": "1.2.3",
      "support_email": "help@example.com"
    }
  }
}
```

```markdown
Welcome to {{ product }} {{ latest_version }} — questions? {{ support_email }}.
```

Tokens with no matching variable are left untouched (so an accidental `{{ typo }}` shows up
literally rather than silently vanishing). Variable tokens are plain identifiers
(`{{ name }}`); function-style macros like `fileuri(...)`/`button(...)` are never treated as
variables.

### Option 2 — A custom plugin (full power)

For macros that need logic — reading files, calling helpers, generating HTML — write a tiny
`IMarkdownPreprocessor` plugin. This is exactly how the built-in `fileuri`/`button` macros
are implemented. A macro is just a regex replacement over the Markdown:

```csharp
using System.Text.RegularExpressions;
using Netdocs.Abstractions;

public sealed partial class YearMacroPlugin : IPlugin, IMarkdownPreprocessor
{
    public string Name => "year-macro";
    public int Order => 30; // run after the built-in macros plugin (25)

    public void Configure(IPluginContext ctx) { }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        var result = YearRegex().Replace(markdown, _ => DateTime.UtcNow.Year.ToString());
        return Task.FromResult(result);
    }

    [GeneratedRegex(@"\{\{\s*year\s*\(\s*\)\s*\}\}")]
    private static partial Regex YearRegex();
}
```

```markdown
© {{ year() }} Example Corp.
```

Build it into a DLL and load it with the [external-plugin loader](../development/external-plugins.md).
See [Events & callbacks](../development/events-and-callbacks.md) for the full plugin
interface list and where each hook runs in the build.

## Controlling where macros run

Mirroring mkdocs-macros, macro expansion is gated:

| Setting | Effect |
|---|---|
| `render_by_default: true` *(default)* | Every page renders macros… |
| front matter `render_macros: false` | …unless a page opts out. |
| front matter `ignore_macros: true` | Skip a page entirely. |
| `render_by_default: false` | Only pages with `render_macros: true` render. |

```json
{ "name": "macros", "options": { "render_by_default": true } }
```

```yaml
---
render_macros: true
---
```

## Enabling the plugin

```json
{ "name": "macros" }
```

## Attribution

Behavior is modeled on [mkdocs-macros-plugin](https://github.com/fralau/mkdocs-macros-plugin) by @fralau (MIT). See [Attributions](../about/attributions.md).
