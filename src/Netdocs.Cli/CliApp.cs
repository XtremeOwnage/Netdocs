using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core;
using Netdocs.Core.Configuration;
using Netdocs.Core.Plugins;
using Netdocs.Plugins;

namespace Netdocs.Cli;

/// <summary>Command-line entry point: <c>netdocs build|serve</c>.</summary>
public static class CliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault() ?? "build";
        var opts = ParseOptions(args);

        var configPath = ResolveConfigPath(opts.ConfigPath);
        if (configPath is null)
        {
            Console.Error.WriteLine("Could not find appsettings.json. Use --config <path> or run from the site directory.");
            return 1;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(configPath)!)
            .AddJsonFile(Path.GetFileName(configPath), optional: false, reloadOnChange: false)
            .AddEnvironmentVariables("NETDOCS_")
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
            builder.SetMinimumLevel(opts.Verbose ? LogLevel.Trace : LogLevel.Information);
            if (opts.Verbose) builder.AddFilter(null, LogLevel.Trace);
        });
        var log = loggerFactory.CreateLogger("netdocs");
        log.LogDebug("Using config file {ConfigPath}", configPath);

        try
        {
            return command switch
            {
                "build" => await BuildAsync(configPath, opts, loggerFactory),
                "serve" => await ServeAsync(configPath, opts, loggerFactory),
                "--help" or "-h" or "help" => PrintHelp(),
                _ => Unknown(log, command),
            };
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Build failed");
            return 1;
        }
    }

    private static async Task<int> BuildAsync(string configPath, CliOptions opts, ILoggerFactory loggerFactory)
    {
        var (config, buildOptions) = LoadConfig(configPath, opts, serve: false);
        var engine = new BuildEngine(config, buildOptions, BuildRegistry(), loggerFactory);
        await engine.BuildAsync();
        loggerFactory.CreateLogger("netdocs").LogInformation("Output: {Dir}", config.AbsoluteSiteDir);
        return 0;
    }

    private static async Task<int> ServeAsync(string configPath, CliOptions opts, ILoggerFactory loggerFactory)
    {
        var (config, buildOptions) = LoadConfig(configPath, opts, serve: true);
        var server = new DevServer(configPath, config, buildOptions, BuildRegistry(), loggerFactory, opts.Port);
        await server.RunAsync();
        return 0;
    }

    private static (SiteConfig, BuildOptions) LoadConfig(string configPath, CliOptions opts, bool serve)
    {
        var config = JsonConfigLoader.Load(configPath);
        var buildOptions = new BuildOptions
        {
            IsProduction = opts.Production,
            IsServe = serve,
            Strict = opts.Strict,
            Clean = opts.Clean || !serve,
        };
        if (opts.Production) Environment.SetEnvironmentVariable("MKDOCS_PROD_BUILD", "true");
        return (config, buildOptions);
    }

    private static PluginRegistry BuildRegistry() => new PluginRegistry()
        .Register<SnippetsPlugin>("snippets", "pymdownx.snippets")
        .Register<AbbreviationsPlugin>("abbreviations")
        .Register<SearchPlugin>("search")
        .Register<TagsPlugin>("tags")
        .Register<BlogPlugin>("blog")
        .Register<MetaPlugin>("meta")
        .Register<RssPlugin>("rss")
        .Register<GlightboxPlugin>("glightbox")
        .Register<RedirectsPlugin>("redirects")
        .Register<GitRevisionDatePlugin>("git-revision-date-localized")
        .Register<FileFilterPlugin>("file-filter")
        .Register<SocialPlugin>("social")
        .Register<TypesetPlugin>("typeset")
        .Register<TableReaderPlugin>("table-reader")
        .Register<MacrosPlugin>("macros");

    private static string? ResolveConfigPath(string? provided)
    {
        if (provided is not null) return File.Exists(provided) ? Path.GetFullPath(provided) : null;
        var candidate = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        return File.Exists(candidate) ? candidate : null;
    }

    private static CliOptions ParseOptions(string[] args)
    {
        var opts = new CliOptions();
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" or "-f" when i + 1 < args.Length: opts.ConfigPath = args[++i]; break;
                case "--port" or "-p" when i + 1 < args.Length && int.TryParse(args[i + 1], out var port): opts.Port = port; i++; break;
                case "--clean": opts.Clean = true; break;
                case "--strict": opts.Strict = true; break;
                case "--prod" or "--production": opts.Production = true; break;
                case "--verbose" or "-v": opts.Verbose = true; break;
            }
        }
        return opts;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
            netdocs - static site generator

            Usage:
              netdocs build [options]     Build the site to the output directory
              netdocs serve [options]     Serve with live reload

            Options:
              -f, --config <path>   Path to appsettings.json (default ./appsettings.json)
              -p, --port <port>     Dev server port (default 8000)
                  --clean           Remove existing output before building
                  --strict          Fail on plugin/template errors
                  --prod            Production build (enables prod-only plugins)
              -v, --verbose         Verbose (Trace) logging
            """);
        return 0;
    }

    private static int Unknown(ILogger log, string command)
    {
        log.LogError("Unknown command '{Command}'. Try 'netdocs --help'.", command);
        return 1;
    }
}

internal sealed class CliOptions
{
    public string? ConfigPath { get; set; }
    public int Port { get; set; } = 8000;
    public bool Clean { get; set; }
    public bool Strict { get; set; }
    public bool Production { get; set; }
    public bool Verbose { get; set; }
}
