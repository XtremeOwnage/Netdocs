using System.Text.RegularExpressions;

namespace Netdocs.Abstractions;

/// <summary>
/// Context passed to a macro handler: the page being rendered, the site, and the parsed
/// (quoted) arguments of the macro call, in order.
/// </summary>
public sealed class MacroInvocation
{
    public required Page Page { get; init; }
    public required SiteContext Site { get; init; }
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>Returns the argument at <paramref name="index"/>, or <paramref name="fallback"/> if absent.</summary>
    public string Arg(int index, string fallback = "") => index >= 0 && index < Args.Count ? Args[index] : fallback;
}

/// <summary>Fluent registrar used by <see cref="UserDefinedMacro.DefineMacros"/>.</summary>
public interface IMacroBuilder
{
    /// <summary>Registers a no-argument function macro invoked as <c>{{ name() }}</c>.</summary>
    IMacroBuilder Add(string name, Func<string> render);

    /// <summary>Registers a function macro invoked as <c>{{ name("a", "b") }}</c>; receives the parsed args.</summary>
    IMacroBuilder Add(string name, Func<IReadOnlyList<string>, string> render);

    /// <summary>Registers a function macro with full access to the page, site, and args.</summary>
    IMacroBuilder Add(string name, Func<MacroInvocation, string> render);

    /// <summary>Registers a bare token <c>{{ name }}</c> that expands to a fixed value.</summary>
    IMacroBuilder Variable(string name, string value);

    /// <summary>Registers a bare token <c>{{ name }}</c> whose value is computed on each expansion.</summary>
    IMacroBuilder Variable(string name, Func<string> value);
}

/// <summary>
/// Base class that makes writing custom macros easy: subclass it, override
/// <see cref="DefineMacros"/>, and register named handlers with an <see cref="IMacroBuilder"/> —
/// no regex or Markdown parsing required. Function macros are called as <c>{{ name(...) }}</c>
/// and variables as <c>{{ name }}</c>. Unknown tokens are left untouched so other plugins
/// (or a literal <c>{{ … }}</c>) still work. A page can opt out with front matter
/// <c>ignore_macros: true</c> or <c>render_macros: false</c>.
/// </summary>
public abstract class UserDefinedMacro : IPlugin, IMarkdownPreprocessor
{
    private static readonly Regex FunctionCall =
        new(@"\{\{\s*(?<name>[A-Za-z_]\w*)\s*\((?<args>[^)]*)\)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex VariableToken =
        new(@"\{\{\s*(?<name>[A-Za-z_][\w.]*)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex QuotedArg =
        new("""["']([^"']*)["']""", RegexOptions.Compiled);

    private readonly object _gate = new();
    private Registrar? _registrar;

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <summary>Preprocessor order. Defaults to 30 so it runs after the built-in macros plugin (25).</summary>
    public virtual int Order => 30;

    /// <inheritdoc/>
    public virtual void Configure(IPluginContext ctx) { }

    /// <summary>Register your macros here. Called once, lazily, on the first page processed.</summary>
    protected abstract void DefineMacros(IMacroBuilder macros);

    /// <inheritdoc/>
    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (!ShouldRender(page) || !markdown.Contains("{{", StringComparison.Ordinal))
            return Task.FromResult(markdown);

        var registrar = EnsureRegistrar();
        if (registrar.IsEmpty) return Task.FromResult(markdown);

        var result = markdown;

        if (registrar.HasFunctions)
        {
            result = FunctionCall.Replace(result, m =>
            {
                if (!registrar.Functions.TryGetValue(m.Groups["name"].Value, out var handler))
                    return m.Value; // leave unknown {{ x(...) }} untouched
                var args = ParseArgs(m.Groups["args"].Value);
                return handler(new MacroInvocation { Page = page, Site = site, Args = args });
            });
        }

        if (registrar.HasVariables)
        {
            result = VariableToken.Replace(result, m =>
                registrar.Variables.TryGetValue(m.Groups["name"].Value, out var value) ? value() : m.Value);
        }

        return Task.FromResult(result);
    }

    /// <summary>Skip pages that opt out of macros via front matter.</summary>
    private static bool ShouldRender(Page page)
    {
        if (page.FrontMatter.TryGetValue("ignore_macros", out var ignore) && ignore is true) return false;
        if (page.FrontMatter.TryGetValue("render_macros", out var render) && render is false) return false;
        return true;
    }

    private Registrar EnsureRegistrar()
    {
        if (_registrar is not null) return _registrar;
        lock (_gate)
        {
            if (_registrar is null)
            {
                var registrar = new Registrar();
                DefineMacros(registrar);
                _registrar = registrar;
            }
        }
        return _registrar;
    }

    private static IReadOnlyList<string> ParseArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var quoted = QuotedArg.Matches(raw);
        if (quoted.Count > 0)
            return quoted.Select(m => m.Groups[1].Value).ToList();
        // Fall back to bare, comma-separated args (e.g. numbers) when nothing is quoted.
        return raw.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
    }

    private sealed class Registrar : IMacroBuilder
    {
        public Dictionary<string, Func<MacroInvocation, string>> Functions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Func<string>> Variables { get; } = new(StringComparer.Ordinal);

        public bool HasFunctions => Functions.Count > 0;
        public bool HasVariables => Variables.Count > 0;
        public bool IsEmpty => Functions.Count == 0 && Variables.Count == 0;

        public IMacroBuilder Add(string name, Func<string> render)
        {
            Functions[name] = _ => render();
            return this;
        }

        public IMacroBuilder Add(string name, Func<IReadOnlyList<string>, string> render)
        {
            Functions[name] = inv => render(inv.Args);
            return this;
        }

        public IMacroBuilder Add(string name, Func<MacroInvocation, string> render)
        {
            Functions[name] = render;
            return this;
        }

        public IMacroBuilder Variable(string name, string value)
        {
            Variables[name] = () => value;
            return this;
        }

        public IMacroBuilder Variable(string name, Func<string> value)
        {
            Variables[name] = value;
            return this;
        }
    }
}
