using System.IO;
using System.Text;

namespace MDEdit.Editing;

/// <summary>
/// Emoji shortcode → World Anvil icon, loaded once from the embedded <c>Resources/WorldAnvilIcons.txt</c>.
/// Only <see cref="BBCodeConverter"/> uses it: World Anvil refuses to save an article containing emoji,
/// so Copy as BBCode exports each one as an icon instead (Requirements.md §2).
/// </summary>
/// <remarks>
/// Kept apart from <see cref="EmojiCatalog"/> on purpose: that catalogue serves the editor and knows
/// nothing of World Anvil. Values are returned raw — wrapping a class in its <c>[section]</c> tag is the
/// converter's job, since every tag MDEdit emits lives there. As with the emoji catalogue, a missing
/// resource yields an empty map rather than throwing, which degrades to ":shortcode:" text.
/// </remarks>
internal static class WorldAnvilIcons
{
    private static readonly Lazy<Dictionary<string, string>> Map = new(Load);

    /// <summary>Every mapped shortcode and its raw replacement. Exposed for tests.</summary>
    public static IReadOnlyDictionary<string, string> All => Map.Value;

    /// <summary>
    /// The replacement for a shortcode: either an icon class ("fa-solid fa-fire") or, when it starts
    /// with '[', a literal World Anvil tag.
    /// </summary>
    public static bool TryGet(string shortcode, out string replacement)
        => Map.Value.TryGetValue(shortcode, out replacement!);

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        using var stream = typeof(WorldAnvilIcons).Assembly
            .GetManifestResourceStream("MDEdit.Resources.WorldAnvilIcons.txt");
        if (stream is null) return map;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            // Split on the first whitespace run; the rest of the line is the value, since a class
            // ("fa-solid fa-fire") contains a space of its own.
            var split = trimmed.IndexOfAny([' ', '\t']);
            if (split <= 0) continue;

            var value = trimmed[(split + 1)..].Trim();
            if (value.Length > 0) map[trimmed[..split]] = value;
        }
        return map;
    }
}
