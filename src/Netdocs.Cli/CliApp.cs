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

        // Commands that do not require an existing appsettings.json.
        switch (command)
        {
            case "import":
                return await ImportAsync(args);
            case "new":
                return await NewCommand.RunAsync(args);
            case "--help" or "-h" or "help":
                return PrintHelp();
            case "--version" or "-V" or "version":
                Console.WriteLine(GetVersion());
                return 0;
        }

        var configPath = ResolveConfigPath(opts.ConfigPath);
        if (configPath is null)
        {
            Console.Error.WriteLine("Could not find appsettings.json. Use --config <path> or run from the site directory.");
            var mkdocs = Path.Combine(Directory.GetCurrentDirectory(), "mkdocs.yml");
            if (File.Exists(mkdocs))
                Console.Error.WriteLine("Found mkdocs.yml — run 'netdocs import' to convert it to appsettings.json.");
            return 1;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(configPath)!)
            .AddJsonFile(Path.GetFileName(configPath), optional: false, reloadOnChange: false)
            .AddEnvironmentVariables("NETDOCS_")
            .Build();

        var warnings = new WarningCounter();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
            builder.SetMinimumLevel(opts.Verbose ? LogLevel.Trace : LogLevel.Information);
            if (opts.Verbose) builder.AddFilter(null, LogLevel.Trace);
            builder.AddProvider(new WarningCounterProvider(warnings));
        });
        var log = loggerFactory.CreateLogger("netdocs");
        log.LogDebug("Using config file {ConfigPath}", configPath);

        try
        {
            return command switch
            {
                "build" => await BuildAsync(configPath, opts, loggerFactory, warnings),
                "profile" => await ProfileAsync(configPath, opts, loggerFactory),
                "deploy" => await DeployAsync(configPath, opts, loggerFactory, warnings),
                "serve" => await ServeAsync(configPath, opts, loggerFactory),
                "watch" => await WatchAsync(configPath, opts, loggerFactory),
                _ => Unknown(log, command),
            };
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Build failed");
            return 1;
        }
    }

    private static async Task<int> BuildAsync(string configPath, CliOptions opts, ILoggerFactory loggerFactory, WarningCounter warnings)
    {
        var (config, buildOptions) = LoadConfig(configPath, opts, serve: false);
        var engine = new BuildEngine(config, buildOptions, BuildRegistry(), loggerFactory);
        await engine.BuildAsync();
        var log = loggerFactory.CreateLogger("netdocs");
        if (buildOptions.Strict && warnings.Count > 0)
        {
            log.LogError("Aborting build: {Count} warning(s) treated as errors (strict mode).", warnings.Count);
            return 1;
        }
        log.LogInformation("Output: {Dir}", config.AbsoluteSiteDir);

        // Optional post-build deploy when requested with --deploy (or a non-'none' deploy target and the deploy command).
        if (opts.Deploy && !config.Deploy.Target.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var deployer = new Netdocs.Core.Deploy.Deployer(config, loggerFactory.CreateLogger("deploy"));
            return await deployer.DeployAsync();
        }
        return 0;
    }

    private static async Task<int> ProfileAsync(string configPath, CliOptions opts, ILoggerFactory loggerFactory)
    {
        // A profiling build always re-renders (no cache) so per-phase timings reflect real work.
        opts.NoCache = true;
        var (config, buildOptions) = LoadConfig(configPath, opts, serve: false);
        var profiler = new Netdocs.Core.Diagnostics.BuildProfiler();
        var engine = new BuildEngine(config, buildOptions, BuildRegistry(), loggerFactory, profiler);
        await engine.BuildAsync();

        Console.WriteLine();
        Console.WriteLine(profiler.Render());
        return 0;
    }

    private static async Task<int> DeployAsync(string configPath, CliOptions opts, ILoggerFactory loggerFactory, WarningCounter warnings)
    {
        // 'deploy' always builds first, then publishes to the configured target.
        var (config, buildOptions) = LoadConfig(configPath, opts, serve: false);
        var log = loggerFactory.CreateLogger("netdocs");
        if (config.Deploy.Target.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            log.LogError("No deploy target configured. Set 'deploy.target' to 'filesystem' or 'git' in appsettings.json.");
            return 1;
        }
        var engine = new BuildEngine(config, buildOptions, BuildRegistry(), loggerFactory);
        await engine.BuildAsync();
        if (buildOptions.Strict && warnings.Count > 0)
        {
            log.LogError("Aborting deploy: {Count} warning(s) treated as errors (strict mode).", warnings.Count);
            return 1;
        }
        var deployer = new Netdocs.Core.Deploy.Deployer(config, loggerFactory.CreateLogger("deploy"));
        return await deployer.DeployAsync();
    }

    private static async Task<int> ServeAsync(string configPath, CliOptions opts, ILoggerFactory loggerFactory)
    {
        var (config, buildOptions) = LoadConfig(configPath, opts, serve: true);
        var server = new DevServer(configPath, config, buildOptions, BuildRegistry(), loggerFactory, opts.Port);
        await server.RunAsync();
        return 0;
    }

    private static async Task<int> WatchAsync(string configPath, CliOptions opts, ILoggerFactory loggerFactory)
    {
        // A publish daemon builds like a production/CI run (prod-only plugins on unless --prod is omitted),
        // but always writes a full, cache-accelerated build in place.
        var (config, buildOptions) = LoadConfig(configPath, opts, serve: false);
        var daemon = new GitSyncDaemon(configPath, config, buildOptions, BuildRegistry, loggerFactory,
            opts.Remote, opts.Branch, opts.Interval);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return await daemon.RunAsync(opts.Once, cts.Token);
    }

    private static (SiteConfig, BuildOptions) LoadConfig(string configPath, CliOptions opts, bool serve)
    {
        var config = JsonConfigLoader.Load(configPath);

        var isCi = IsTruthy(System.Environment.GetEnvironmentVariable("CI"));
        var isLocal = serve || (!opts.Production && !isCi);

        // Publish standard build flags to the process environment so YAML `!ENV [VAR, default]`
        // tags and plugins observe a single, consistent view (mirrors mkdocs-material's flags).
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MKDOCS_PROD_BUILD"] = opts.Production ? "true" : "false",
            ["IS_LOCAL_BUILD"] = isLocal ? "true" : "false",
            ["CI"] = isCi ? "true" : null,
        };
        foreach (var (k, v) in env)
            if (v is not null) System.Environment.SetEnvironmentVariable(k, v);

        var buildOptions = new BuildOptions
        {
            IsProduction = opts.Production,
            IsServe = serve,
            Strict = opts.Strict || IsTruthy(System.Environment.GetEnvironmentVariable("MKDOCS_STRICT")),
            Clean = opts.Clean || !serve,
            NoCache = opts.NoCache,
            Environment = env,
        };
        return (config, buildOptions);
    }

    private static bool IsTruthy(string? value) =>
        value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static async Task<int> ImportAsync(string[] args)
    {
        // netdocs import [path/to/mkdocs.yml] [--out appsettings.json] [--force]
        var source = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-')) ?? "mkdocs.yml";
        source = Path.GetFullPath(source);
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"mkdocs config not found: {source}");
            Console.Error.WriteLine("Usage: netdocs import [path/to/mkdocs.yml] [--out appsettings.json] [--force]");
            return 1;
        }

        var outPath = FlagValue(args, "--out")
            ?? Path.Combine(Path.GetDirectoryName(source)!, "appsettings.json");
        outPath = Path.GetFullPath(outPath);
        var force = args.Contains("--force");
        if (File.Exists(outPath) && !force)
        {
            Console.Error.WriteLine($"Refusing to overwrite existing {outPath}. Pass --force to replace it.");
            return 1;
        }

        try
        {
            var json = Netdocs.Core.Configuration.MkDocsImporter.ConvertToJson(source);
            await File.WriteAllTextAsync(outPath, json);
            Console.WriteLine($"Converted {Path.GetFileName(source)} -> {outPath}");
            Console.WriteLine("Review the generated appsettings.json, then run: netdocs build");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Import failed: {ex.Message}");
            return 1;
        }
    }

    private static string? FlagValue(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
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
        .Register<LinkNotesPlugin>("link-notes", "affiliate-links")
        .Register<Base64EmbedPlugin>("b64", "pymdownx.b64")
        .Register<ArithmatexPlugin>("arithmatex", "pymdownx.arithmatex")
        .Register<CalculatorPlugin>("calculator", "calc")
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
                case "--no-cache": opts.NoCache = true; break;
                case "--remote" when i + 1 < args.Length: opts.Remote = args[++i]; break;
                case "--branch" when i + 1 < args.Length: opts.Branch = args[++i]; break;
                case "--interval" when i + 1 < args.Length && int.TryParse(args[i + 1], out var iv): opts.Interval = iv; i++; break;
                case "--once": opts.Once = true; break;
                case "--deploy": opts.Deploy = true; break;
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
              netdocs profile [options]   Build once and print a per-phase/plugin timing tree
              netdocs deploy [options]    Build, then publish to the configured deploy target
              netdocs serve [options]     Serve with live reload
              netdocs watch [options]     Publish daemon: poll a git remote and rebuild on push
              netdocs new [path]           Scaffold an annotated appsettings.json
              netdocs import [mkdocs.yml]  Convert an mkdocs.yml to appsettings.json
              netdocs --version            Print the Netdocs version and exit

            New / import options:
                  --out <path>      Output appsettings.json path (import; default next to mkdocs.yml)
                  --force           Overwrite an existing appsettings.json

            Options:
              -f, --config <path>   Path to appsettings.json (default ./appsettings.json)
              -p, --port <port>     Dev server port (default 8000)
                  --clean           Remove existing output before building
                  --no-cache        Ignore the incremental render cache (full re-render)
                  --strict          Treat warnings (and plugin/template errors) as failures
                  --prod            Production build (enables prod-only plugins)
                  --deploy          After 'build', publish to the configured deploy target
                  --remote <name>   Git remote to watch (watch; default origin)
                  --branch <name>   Git branch to track (watch; default current branch)
                  --interval <sec>  Poll interval in seconds (watch; default 30)
                  --once            Run a single watch check and exit (watch)
              -v, --verbose         Verbose (Trace) logging
            """);
        return 0;
    }

    private static string GetVersion()
    {
        var asm = typeof(CliApp).Assembly;
        var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
        if (info.Length > 0)
        {
            var v = ((System.Reflection.AssemblyInformationalVersionAttribute)info[0]).InformationalVersion;
            // Strip the build metadata (e.g. "+<commit sha>") appended by the SDK.
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
        return asm.GetName().Version?.ToString() ?? "0.0.0";
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
    public bool NoCache { get; set; }
    public string Remote { get; set; } = "origin";
    public string? Branch { get; set; }
    public int Interval { get; set; } = 30;
    public bool Once { get; set; }
    public bool Deploy { get; set; }
    public bool Strict { get; set; }
    public bool Production { get; set; }
    public bool Verbose { get; set; }
}

/// <summary>Thread-safe counter of warning/error log entries, used to enforce strict mode.</summary>
internal sealed class WarningCounter
{
    private int _count;
    public void Increment() => Interlocked.Increment(ref _count);
    public int Count => Volatile.Read(ref _count);
}

/// <summary>Logger provider that tallies <see cref="LogLevel.Warning"/>+ messages into a <see cref="WarningCounter"/>.</summary>
internal sealed class WarningCounterProvider(WarningCounter counter) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CountingLogger(counter);
    public void Dispose() { }

    private sealed class CountingLogger(WarningCounter counter) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) counter.Increment();
        }
    }
}
