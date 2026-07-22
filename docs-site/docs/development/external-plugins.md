# External plugins

Netdocs ships with a set of built-in plugins, but you can extend a build with your
own plugins compiled into a .NET assembly and dropped next to your site — no rebuild
of Netdocs required.

## How loading works

At the start of every `build` and `serve`, Netdocs looks for a `plugins/` directory in
your project root (the folder that contains `appsettings.json`) and loads every
`*.dll` it finds there:

```
my-site/
├── appsettings.json
├── docs/
└── plugins/
    └── MyPlugin.dll        ← discovered automatically
```

Each assembly is loaded into its own isolated
[`AssemblyLoadContext`](https://learn.microsoft.com/dotnet/core/dependency-loading/understanding-assemblyloadcontext),
so a plugin can bring its own dependencies without clashing with the host. The shared
`Netdocs.Abstractions` contract assembly is always resolved from the host, which keeps
plugin types compatible with the engine's interfaces.

Discovering an assembly only makes its plugins **available**. As with any plugin, you
still enable it by name in configuration:

```json
{
  "Netdocs": {
    "plugins": [
      { "name": "my-plugin", "options": { "greeting": "hello" } }
    ]
  }
}
```

The `name` must match the plugin's `Name` property. Built-in plugins take precedence:
an external plugin that reuses a built-in name is ignored with a warning.

## Writing a plugin

Reference `Netdocs.Abstractions` and implement `IPlugin`. Opt into build stages by
also implementing any of the hook interfaces:

| Interface | Purpose |
| --- | --- |
| `IPlugin` | Required. `Configure` registers assets, scripts, and services. |
| `IMarkdownPreprocessor` | Transform raw Markdown before parsing (ordered via `Order`). |
| `IMarkdigContributor` | Add Markdig extensions to the shared pipeline. |
| `IContentGenerator` | Emit virtual pages (listings, archives, tag pages). |
| `IBuildHook` | Run at build start, per rendered page, and at build completion. |
| `INavigationFilter` | Include or prune discovered pages. |

A minimal plugin:

```csharp
using Netdocs.Abstractions;

namespace MyPlugin;

public sealed class GreetingPlugin : IPlugin
{
    public string Name => "my-plugin";

    public void Configure(IPluginContext ctx)
    {
        var greeting = ctx.PluginOptions.TryGetValue("greeting", out var g) ? g?.ToString() : "hi";
        ctx.AddInlineScript($"console.log({System.Text.Json.JsonSerializer.Serialize(greeting)});");
    }
}
```

Build it against the same target framework as Netdocs and copy the resulting DLL into
your site's `plugins/` folder. Set `<Private>false</Private>` /
`<ExcludeAssets>runtime</ExcludeAssets>` on the `Netdocs.Abstractions` reference so you
don't ship a duplicate copy of the contract assembly.

## Troubleshooting

- **Plugin not loaded** — confirm the DLL is directly inside `plugins/` and that the
  plugin is listed under `plugins` in `appsettings.json`.
- **Type load errors** — run with `--verbose`; Netdocs logs the assembly and reason
  when it skips a DLL or type.
- **Name clash** — rename your plugin's `Name`; built-in names always win.
