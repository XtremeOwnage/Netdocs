using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Netdocs.Plugins;

/// <summary>
/// Shared scanner for preprocessors that replace a whole fenced code block with rendered HTML
/// (<see cref="CalculatorPlugin"/>, <see cref="TimelinePlugin"/>).
///
/// <para>Fence handling is subtler than it looks — a block has to be matched to its own closing
/// fence so that an outer <c>````</c> example containing an inner <c>```calc</c> is copied
/// through untouched rather than half-rendered — which is exactly why both plugins should share
/// one implementation instead of keeping their own copy in step by hand.</para>
/// </summary>
internal static partial class FencedBlocks
{
    /// <summary>
    /// Replaces every top-level fence whose info word is <paramref name="infoWord"/> with the
    /// output of <paramref name="render"/>, called with the block's body and its zero-based index
    /// among the matched blocks on this page (a stable handle for naming). Fences of any other
    /// kind — including ones that merely contain a matching fence — pass through verbatim.
    /// Returns the original string when nothing matched.
    /// </summary>
    public static string Rewrite(string markdown, string infoWord, Func<string, int, string> render)
    {
        // Cheap reject before the line-by-line scan. Case-insensitive to agree with the info-word
        // comparison below: a ```CALC fence matches there, so it must not be discarded here.
        if (markdown.IndexOf(infoWord, StringComparison.OrdinalIgnoreCase) < 0)
            return markdown;

        var lines = markdown.Split('\n');
        var sb = new StringBuilder(markdown.Length);
        var changed = false;
        var blockIndex = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var open = FenceOpen().Match(lines[i].TrimEnd('\r'));
            if (!open.Success)
            {
                sb.Append(lines[i]);
                if (i < lines.Length - 1) sb.Append('\n');
                continue;
            }

            var fence = open.Groups["fence"].Value;
            var marker = fence[0];
            // Closing fence: same character, at least as long, nothing but the marker on the line.
            var end = i + 1;
            for (; end < lines.Length; end++)
            {
                var t = lines[end].Trim().TrimEnd('\r');
                if (t.Length >= fence.Length && t.All(c => c == marker)) break;
            }

            if (open.Groups["info"].Value.Equals(infoWord, StringComparison.OrdinalIgnoreCase))
            {
                // Our block: render the body (the lines between the fences) to an HTML island.
                var body = end > i + 1 ? string.Join("\n", lines, i + 1, end - i - 1) : "";
                sb.Append('\n').Append(render(body, blockIndex++)).Append('\n');
                changed = true;
            }
            else
            {
                // Some other fence (e.g. a ```` markdown example that itself contains ```calc):
                // copy it through unchanged, including its closing line.
                for (var k = i; k <= end && k < lines.Length; k++)
                {
                    sb.Append(lines[k]);
                    if (k < lines.Length - 1) sb.Append('\n');
                }
            }
            i = end; // resume after the closing fence line
        }

        return changed ? sb.ToString() : markdown;
    }

    /// <summary>
    /// A stable DOM id for a control inside a rendered block, used to pair a &lt;label for&gt; with
    /// its input. Derived from the page, the block's position on it, and the control's name rather
    /// than a random GUID: a fresh id on every build made each page containing a block differ
    /// byte-for-byte from the last one, so <c>OutputWriter</c> rewrote it every time and the watch
    /// daemon republished it, defeating the incremental diff those exist to produce.
    /// </summary>
    public static string ElementId(string prefix, string pageKey, int blockIndex, string name)
    {
        var seed = $"{pageKey}|{blockIndex}|{name}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return prefix + "-" + Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    // Opening line of a fenced code block (```word, ~~~word, with optional attributes after the
    // info word). Up to three leading spaces, matching CommonMark.
    [GeneratedRegex(@"^(?<indent>[ \t]{0,3})(?<fence>`{3,}|~{3,})[ \t]*(?<info>[^`\s]*)[^\r\n]*$")]
    private static partial Regex FenceOpen();
}
