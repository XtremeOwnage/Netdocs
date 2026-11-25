using Netdocs.Abstractions;

namespace Netdocs.Core.Content;

/// <summary>
/// Recognises draft pages: front matter with <c>draft: true</c> (also <c>"true"</c>, <c>yes</c>,
/// <c>on</c> or <c>1</c>, since hand-written YAML spells booleans many ways). Generated pages are
/// never drafts.
/// </summary>
public static class DraftPages
{
    public static bool IsDraft(Page page) =>
        !page.IsGenerated
        && page.FrontMatter.TryGetValue("draft", out var value)
        && IsTruthy(value);

    private static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        string s => s.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1",
        _ => false,
    };
}
