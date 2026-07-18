---
title: macros
---

# macros

A minimal port of [mkdocs-macros]. The macros plugin lets you inject dynamic content into
your Markdown *before* it is parsed. It ships three example function macros (`fileuri`,
`button` and `download`), lets you define your own **variables** in config, and — for
anything richer — documents how to add a macro by writing a small plugin.

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

### `download("file"[, "text"][, "mode"])`

Resolves a doc or shipped asset (same lookup as `fileuri`) and renders a **download link** — a
Material button with a download icon and the HTML5 `download` attribute, so the browser saves the
file instead of navigating to it. This pairs naturally with a code block that *shows* a script while
offering a one-click download of the same file.

```markdown
````markdown
```bash title="setup.sh"
--8<-- "scripts/setup.sh"
```

{{ download("scripts/setup.sh") }}
````
```

- The optional second argument overrides the link text (default: `Download <filename>`).
- The optional third argument is the URI **mode**, identical to
  [`fileuri`'s modes](#choosing-the-uri-form): `absolute` (default), `path`, or `relative`.

```markdown
{{ download("scripts/setup.sh", "Grab the script", "relative") }}
```

If the file can't be found, an HTML comment is emitted instead of a broken link.

### `version("x.y.z"[, "url"])`, `flag("name"[, "text"])`, `badge("icon", "text")`

Render mkdocs-material style **reference badges** — small pills that mark when a feature landed
or its support status. They're handy at the top of a reference page.

```markdown
{{ version("1.2.0") }} {{ flag("experimental") }}
```

renders a version pill (tag icon + `1.2.0`) next to an "Experimental" status pill.

- **`version`** shows a tag icon + version. Pass a second argument to link it (e.g. to a changelog
  entry): `{{ version("1.2.0", "../about/changelog/") }}`.
- **`flag`** shows a status pill. Known names (`experimental`, `required`, `default`, `optional`,
  `beta`, `new`, `deprecated`) get a matching icon and label; a second argument overrides the text.
- **`badge`** is the generic form — the first argument is any
  [curated icon name](../reference/theme.md#icons) and the second is the label:
  `{{ badge("shield-check", "Stable") }}`.

Badges are styled by the theme (`.nd-badge`) and adapt to light/dark automatically.

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

For macros that need logic — reading files, calling helpers, generating HTML — subclass
`UserDefinedMacro` and register named handlers. No regex or Markdown parsing required:
function macros are called as `{{ name(...) }}`, variables as `{{ name }}`, and unknown
tokens are left untouched.

```csharp
using Netdocs.Abstractions;

public sealed class SiteMacros : UserDefinedMacro
{
    public override string Name => "site-macros";

    protected override void DefineMacros(IMacroBuilder macros) => macros
        // {{ year() }} -> current year
        .Add("year", () => DateTime.UtcNow.Year.ToString())
        // {{ badge("stable") }} -> receives the quoted args in order
        .Add("badge", args => $"<span class=\"badge\">{args[0]}</span>")
        // {{ source() }} -> full access to the page and site
        .Add("source", inv => $"[Edit]({inv.Site.Config.RepoUrl}/edit/main/docs/{inv.Page.RelativePath})")
        // {{ product }} -> a bare-token variable
        .Variable("product", "Netdocs");
}
```

```markdown
© {{ year() }} {{ product }} — {{ badge("stable") }}
```

A page opts out with front matter `ignore_macros: true` or `render_macros: false`, exactly
like the built-in macros plugin. Override `Order` (default `30`) to run before/after other
preprocessors, and override `Configure(ctx)` to read plugin options.

!!! note "Advanced: raw preprocessor"
    `UserDefinedMacro` is a thin helper over `IMarkdownPreprocessor`. If you need total
    control over the transform (e.g. a syntax that isn't `{{ … }}`), implement
    `IPlugin, IMarkdownPreprocessor` directly and run any regex/replacement you like in
    `ProcessAsync` — this is how the built-in `fileuri`/`button` macros are implemented.

Build your plugin into a DLL and load it with the
[external-plugin loader](../development/external-plugins.md). See
[Events & callbacks](../development/events-and-callbacks.md) for the full plugin interface
list and where each hook runs in the build.

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
