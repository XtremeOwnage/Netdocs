# Events & callbacks reference

Every Netdocs plugin implements `IPlugin` and then **opts in** to the parts of the build
it cares about by implementing one or more hook interfaces. This page documents each
interface, its exact signature, when it fires, and a short example.

For the *order* in which these fire relative to each other, see the
[Build lifecycle](lifecycle.md).

All contracts live in the `Netdocs.Abstractions` assembly
([`Plugins.cs`](https://github.com/XtremeOwnage/Netdocs/blob/main/src/Netdocs.Abstractions/Plugins.cs)).

---

## `IPlugin`

The base contract. Required by every plugin.

```csharp
public interface IPlugin
{
    string Name { get; }
    void Configure(IPluginContext ctx);
}
```

- **`Name`** — the id used to enable the plugin in `appsettings.json`
  (`"plugins": [{ "name": "my-plugin" }]`). Built-in names always win over external ones.
- **`Configure`** — called **once** at build start (lifecycle stage 3), in the order
  plugins are listed in config. Register assets, scripts, and services, and read your
  options here.

```csharp
public sealed class MyPlugin : IPlugin
{
    public string Name => "my-plugin";

    public void Configure(IPluginContext ctx)
    {
        if (ctx.PluginOptions.TryGetValue("cdn", out var cdn) && cdn is string url)
            ctx.AddStylesheet(url);
    }
}
```

---

## `IPluginContext`

The services handed to `Configure`. Not a hook you implement — it's what you *receive*.

```csharp
public interface IPluginContext
{
    SiteConfig Config { get; }
    BuildOptions Options { get; }
    ILogger Logger { get; }
    IServiceCollection Services { get; }
    IReadOnlyDictionary<string, object?> PluginOptions { get; }

    void AddStylesheet(string href);
    void AddScript(string src, bool defer = true);
    void AddInlineScript(string javascript);
    void AddAsset(string sourcePath, string destRelative);
}
```

| Member | Use |
| --- | --- |
| `Config` | The resolved `SiteConfig` (site name, theme, nav, extra, paths). |
| `Options` | Build flags: `IsProduction`, `IsServe`, `Strict`, `Clean`, `NoCache`. |
| `Logger` | Category logger for this plugin; honors `--verbose` and log config. |
| `Services` | DI collection — register services other plugins/hooks can resolve. |
| `PluginOptions` | Your plugin's `options` block from `appsettings.json`. |
| `AddStylesheet(href)` | Inject a `<link rel="stylesheet">` into every page head. |
| `AddScript(src, defer)` | Inject a `<script src>` before `</body>`. |
| `AddInlineScript(js)` | Emit a raw inline `<script>` before `</body>`. |
| `AddAsset(src, dest)` | Copy a file into the output at `dest` (lifecycle stage 14). |

---

## `IMarkdownPreprocessor`

Transform **raw Markdown text** before it is parsed. Fires at lifecycle stage 8 for every
page, ordered by `Order` (ascending).

```csharp
public interface IMarkdownPreprocessor
{
    int Order { get; }
    Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct);
}
```

- **`Order`** — lower runs first. Built-ins: snippets `10`, abbreviations/table-reader
  `20`, macros `25`. Choose a value relative to these.
- Return the transformed markdown. This is a pure text→text stage; the string you return
  becomes the input to the next preprocessor, and finally to the parser.

```csharp
public sealed class ShoutPlugin : IPlugin, IMarkdownPreprocessor
{
    public string Name => "shout";
    public int Order => 30;              // after macros
    public void Configure(IPluginContext ctx) { }

    public Task<string> ProcessAsync(Page page, string md, SiteContext site, CancellationToken ct)
        => Task.FromResult(md.Replace("{{shout}}", "**LISTEN UP**"));
}
```

---

## `IMarkdigContributor`

Add [Markdig](https://github.com/xoofx/markdig) extensions to the shared parse pipeline.
`Extend` is called once while the pipeline is built (lifecycle stage 9).

```csharp
public interface IMarkdigContributor
{
    void Extend(MarkdownPipelineBuilder builder, SiteContext site);
}
```

```csharp
public sealed class TypesetPlugin : IPlugin, IMarkdigContributor
{
    public string Name => "typeset";
    public void Configure(IPluginContext ctx) { }

    public void Extend(MarkdownPipelineBuilder builder, SiteContext site)
        => builder.UseSmartyPants();
}
```

!!! note
    A new pipeline is built per render thread, so keep `Extend` free of shared mutable
    state. Changing the set of contributors also invalidates the render cache.

---

## `IContentGenerator`

Produce **virtual pages** that were not authored in `docs/` — blog indexes, tag pages,
archives. Fires at lifecycle stage 7; generated pages are added to `site.Pages` and then
flow through preprocessing, rendering, and templating like any other page.

```csharp
public interface IContentGenerator
{
    IAsyncEnumerable<Page> GenerateAsync(SiteContext site, CancellationToken ct);
}
```

```csharp
public sealed class HelloGenerator : IPlugin, IContentGenerator
{
    public string Name => "hello-gen";
    public void Configure(IPluginContext ctx) { }

    public async IAsyncEnumerable<Page> GenerateAsync(
        SiteContext site, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new Page
        {
            SourcePath = "",
            RelativePath = "hello/index.md",
            Url = "hello/",
            Title = "Hello",
            RawMarkdown = "# Hello\n\nGenerated at build time.",
        };
        await Task.CompletedTask;
    }
}
```

---

## `IBuildHook`

Lifecycle callbacks across the whole build. All three methods have default (no-op)
implementations, so implement only the ones you need.

```csharp
public interface IBuildHook
{
    Task OnBuildStartAsync(SiteContext site, CancellationToken ct) => Task.CompletedTask;
    Task OnPageRenderedAsync(Page page, SiteContext site, CancellationToken ct) => Task.CompletedTask;
    Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct) => Task.CompletedTask;
}
```

| Callback | Fires at | Typical use |
| --- | --- | --- |
| `OnBuildStartAsync` | Stage 6 — after discovery + filtering | Seed state, validate config, prepare output dirs. |
| `OnPageRenderedAsync` | Stage 13 — once per page, after HTML is written | Collect search documents, per-page side outputs. |
| `OnBuildCompleteAsync` | Stage 15 — once, near the end | Emit whole-site artifacts: `search_index.json`, RSS, `tags.json`, social cards. |

```csharp
public sealed class WordCountPlugin : IPlugin, IBuildHook
{
    private int _words;
    public string Name => "wordcount";
    public void Configure(IPluginContext ctx) { }

    public Task OnPageRenderedAsync(Page page, SiteContext site, CancellationToken ct)
    {
        _words += page.HtmlContent.Split(' ').Length;
        return Task.CompletedTask;
    }

    public Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        var path = Path.Combine(site.Config.AbsoluteSiteDir, "wordcount.txt");
        return File.WriteAllTextAsync(path, _words.ToString(), ct);
    }
}
```

!!! tip
    `OnBuildCompleteAsync` is the right place to write files. Call `site.TrackOutput(path)`
    for anything you emit so the [prune step](lifecycle.md) does not delete it as stale.

---

## `INavigationFilter`

Decide whether a discovered page is included. Fires at lifecycle stage 5; a page survives
only if **every** filter returns `true`.

```csharp
public interface INavigationFilter
{
    bool ShouldInclude(Page page, SiteContext site);
}
```

```csharp
public sealed class DraftFilter : IPlugin, INavigationFilter
{
    public string Name => "no-drafts";
    public void Configure(IPluginContext ctx) { }

    public bool ShouldInclude(Page page, SiteContext site)
        => !(page.FrontMatter.TryGetValue("draft", out var d) && d is true);
}
```

---

## Combining interfaces

A plugin can implement several interfaces at once — the engine detects each one and wires
it into the matching stage. For example, the blog plugin is both an `IContentGenerator`
(tag/archive pages) and an `IBuildHook` (RSS at completion); the snippets plugin is an
`IMarkdownPreprocessor`. Implement exactly the stages you need and leave the rest.
