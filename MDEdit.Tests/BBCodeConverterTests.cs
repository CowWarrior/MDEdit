using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

// BBCodeConverter — Markdown to World Anvil BBCode for Edit → Copy as BBCode (Requirements.md §2).
// Expected output is World Anvil's documented syntax, plus the owner's checkbox tags.
public class BBCodeConverterTests
{
    private static string Convert(string markdown) => BBCodeConverter.Convert(markdown);

    // ---- Inline ----

    [Theory]
    [InlineData("**b**",      "[b]b[/b]")]
    [InlineData("__b__",      "[b]b[/b]")]
    [InlineData("*i*",        "[i]i[/i]")]
    [InlineData("_i_",        "[i]i[/i]")]
    [InlineData("***bi***",   "[b][i]bi[/i][/b]")]
    [InlineData("~~s~~",      "[s]s[/s]")]
    [InlineData("==h==",      "[mark]h[/mark]")]
    [InlineData("X^2^",       "X[sup]2[/sup]")]
    [InlineData("H~2~O",      "H[sub]2[/sub]O")]
    [InlineData("<u>u</u>",   "[u]u[/u]")]
    [InlineData("`x`",        "[code]x[/code]")]
    public void Inline_EachConstruct_MapsToItsTag(string markdown, string expected)
    {
        Assert.Equal(expected, Convert(markdown));
    }

    [Fact]
    public void Inline_MixedNesting_KeepsBothTags()
    {
        Assert.Equal("[b][i]x[/i][/b]", Convert("**_x_**"));
        Assert.Equal("[s][b]x[/b][/s]", Convert("~~**x**~~"));
        Assert.Equal("[u][b]x[/b][/u]", Convert("<u>**x**</u>"));
    }

    [Fact]
    public void Inline_LinkInsideBold_KeepsTheLink()
    {
        // The editor's flat scanners treat this as one opaque bold run; the converter recurses, so the
        // URL isn't lost on export.
        Assert.Equal("[b][url:https://a.com]t[/url][/b]", Convert("**[t](https://a.com)**"));
    }

    [Fact]
    public void Inline_CodeContent_IsLiteral()
    {
        Assert.Equal("[code]**not bold** [x][/code]", Convert("`**not bold** [x]`"));
    }

    [Fact]
    public void Link_UsesWorldAnvilColonForm()
    {
        Assert.Equal("see [url:https://a.com]here[/url].", Convert("see [here](https://a.com)."));
    }

    [Fact]
    public void Link_TitleIsDropped()
    {
        Assert.Equal("[url:https://a.com]t[/url]", Convert("[t](https://a.com \"Title\")"));
    }

    [Fact]
    public void Link_FormattedLabel_IsConverted()
    {
        Assert.Equal("[url:https://a.com][b]t[/b][/url]", Convert("[**t**](https://a.com)"));
    }

    [Fact]
    public void ReferenceLink_KeepsOnlyItsLabel()
    {
        Assert.Equal("text", Convert("[text][ref]"));
    }

    [Fact]
    public void RemoteImage_BecomesImgTag()
    {
        Assert.Equal("[img:https://a.com/x.png]", Convert("![alt](https://a.com/x.png)"));
    }

    [Fact]
    public void LocalImage_BecomesVisiblePlaceholder()
    {
        Assert.Equal("[b]IMAGE: images/diagram.png (The diagram)[/b]", Convert("![The diagram](images/diagram.png)"));
    }

    // ---- Emoji → icons (World Anvil refuses to save emoji) ----

    [Fact]
    public void Emoji_Shortcode_BecomesFontAwesomeSection()
    {
        // The space between the tags is required by World Anvil, or the icon doesn't display.
        Assert.Equal("hi [section:fa-solid fa-face-grin-tears] [/section]", Convert("hi :joy:"));
    }

    [Fact]
    public void Emoji_Character_ExportsLikeItsShortcode()
    {
        Assert.Equal("hi [section:fa-solid fa-face-grin-tears] [/section]", Convert("hi 😂"));
    }

    [Fact]
    public void Emoji_Alias_FindsTheIconOfTheFirstListedName()
    {
        Assert.Equal("[section:fa-solid fa-thumbs-up] [/section]", Convert(":+1:"));
    }

    [Theory]
    [InlineData(":white_check_mark:", "[c]")]
    [InlineData(":x:",                "[x]")]
    [InlineData(":heavy_plus_sign:",  "[+]")]
    [InlineData(":heavy_minus_sign:", "[-]")]
    public void Emoji_ChecklistMappings_EmitTheTagLiterally(string markdown, string expected)
    {
        Assert.Equal(expected, Convert(markdown));
    }

    [Fact]
    public void Emoji_VariationSelectorForm_IsOneIcon()
    {
        // ❤️ is ❤ plus U+FE0F; longest match must win, or the selector is left stranded.
        Assert.Equal("[section:fa-solid fa-heart] [/section]", Convert("❤️"));
    }

    [Fact]
    public void Emoji_Unmapped_KeepsTheShortcodeAsWritten()
    {
        Assert.Equal("an :ok: and :ok:", Convert("an :ok: and 🆗"));
    }

    [Fact]
    public void Emoji_InsideCode_BecomesShortcode_NotIcon()
    {
        Assert.Equal("[code]x :joy:[/code]", Convert("`x 😂`"));
        Assert.Equal("[code]y :fire:[/code]", Convert("```\ny 🔥\n```"));
    }

    [Fact]
    public void Emoji_NotInCatalogue_IsStripped()
    {
        // 🫠 isn't catalogued: no icon, no shortcode to keep — dropped so the article still saves.
        Assert.Equal("melt  away", Convert("melt 🫠 away"));
    }

    [Fact]
    public void Emoji_InsideFormattingAndTables_StillBecomesIcon()
    {
        Assert.Equal("[b][section:fa-solid fa-fire] [/section][/b]", Convert("**:fire:**"));
        Assert.Contains("[td][section:fa-solid fa-fire] [/section][/td]", Convert("| a |\n| --- |\n| 🔥 |"));
    }

    [Fact]
    public void EveryCatalogueEmoji_ExportsWithNothingWorldAnvilRejects()
    {
        foreach (var (shortcode, emoji) in EmojiCatalog.All)
        {
            foreach (var output in new[] { Convert($":{shortcode}:"), Convert(emoji) })
            {
                Assert.DoesNotContain(output, c => char.IsSurrogate(c) || c is '️' or '‍');
            }
        }
    }

    [Fact]
    public void IconMap_KeysAreCatalogueShortcodes_ThatAreFirstListed()
    {
        // A typo'd key, or one under an alias, would never be looked up — the icon would silently go unused.
        Assert.True(WorldAnvilIcons.All.Count > 200);
        foreach (var (shortcode, _) in WorldAnvilIcons.All)
        {
            Assert.True(EmojiCatalog.TryGet(shortcode, out var emoji), $"'{shortcode}' is not in Emoji.txt");
            Assert.True(EmojiCatalog.TryGetShortcode(emoji, out var first) && first == shortcode,
                $"'{shortcode}' is an alias; map '{first}' instead");
        }
    }

    [Fact]
    public void IconMap_ValuesAreIconClassesOrChecklistTags()
    {
        foreach (var (shortcode, value) in WorldAnvilIcons.All)
        {
            bool iconClass = value.StartsWith("fa-") || value.StartsWith("ra ");
            bool tag = value is "[c]" or "[x]" or "[+]" or "[-]" or "[r]" or "[l]" or "[]";
            Assert.True(iconClass || tag, $"'{shortcode}' maps to unrecognized '{value}'");
        }
    }

    [Fact]
    public void Emoji_UnknownShortcode_StaysText()
    {
        Assert.Equal("at 10:30:45", Convert("at 10:30:45"));
    }

    [Fact]
    public void Escape_EmitsTheLiteralCharacter()
    {
        Assert.Equal("*not italic*", Convert(@"\*not italic\*"));
    }

    [Fact]
    public void PlainBracket_IsWrappedInNoparse()
    {
        // "[c]" in prose would otherwise render as a checked box on World Anvil.
        Assert.Equal("[noparse]see [c] above[/noparse]", Convert("see [c] above"));
    }

    [Fact]
    public void PlainTextWithoutBrackets_IsNotWrapped()
    {
        Assert.Equal("just text", Convert("just text"));
    }

    [Fact]
    public void HtmlComment_BecomesHiddenComment()
    {
        Assert.Equal("a /* note */ b", Convert("a <!-- note --> b"));
    }

    // ---- Blocks ----

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Heading_EveryLevelPassesThrough(int level)
    {
        Assert.Equal($"[h{level}]Title [b]x[/b][/h{level}]", Convert(new string('#', level) + " Title **x**"));
    }

    [Fact]
    public void HorizontalRule_BecomesHr()
    {
        Assert.Equal("a\n\n[hr]\n\nb", Convert("a\n\n---\n\nb"));
    }

    [Fact]
    public void Paragraphs_KeepTheirBlankLines()
    {
        Assert.Equal("one\ntwo\n\nthree", Convert("one\ntwo\n\nthree"));
    }

    [Fact]
    public void HardBreak_TwoTrailingSpaces_BecomesBr()
    {
        Assert.Equal("one[br]\ntwo", Convert("one  \ntwo"));
    }

    [Fact]
    public void HardBreak_TrailingBackslash_BecomesBr()
    {
        Assert.Equal("one[br]\ntwo", Convert("one\\\ntwo"));
    }

    [Fact]
    public void TrailingSpaces_OnLastLineOfParagraph_AreNotABreak()
    {
        Assert.Equal("one\n\ntwo", Convert("one  \n\ntwo"));
    }

    [Fact]
    public void BulletList_WrapsItemsInUl()
    {
        Assert.Equal("[ul]\n- a\n- [b]b[/b]\n[/ul]", Convert("- a\n* **b**"));
    }

    [Fact]
    public void NumberedList_WrapsItemsInOl_AndDropsNumbers()
    {
        Assert.Equal("[ol]\n- a\n- b\n[/ol]", Convert("1. a\n2. b"));
    }

    [Fact]
    public void NestedList_OpensAListBetweenItems()
    {
        Assert.Equal(
            "[ul]\n- a\n[ol]\n- sub\n[/ol]\n- b\n[/ul]",
            Convert("- a\n  1. sub\n- b"));
    }

    [Fact]
    public void NestedList_ClosesEveryLevelAtTheEnd()
    {
        Assert.Equal("[ul]\n- a\n[ul]\n- b\n[/ul]\n[/ul]\nafter", Convert("- a\n  - b\nafter"));
    }

    [Fact]
    public void ListTypeChangeAtSameDepth_StartsANewList()
    {
        Assert.Equal("[ul]\n- a\n[/ul]\n[ol]\n- b\n[/ol]", Convert("- a\n1. b"));
    }

    [Fact]
    public void BlankLineBetweenItems_KeepsOneList()
    {
        Assert.Equal("[ol]\n- a\n- b\n[/ol]", Convert("1. a\n\n2. b"));
    }

    [Fact]
    public void BlankLineAfterList_ClosesItAndIsKept()
    {
        Assert.Equal("[ul]\n- a\n[/ul]\n\ntext", Convert("- a\n\ntext"));
    }

    [Fact]
    public void TaskList_UsesWorldAnvilCheckboxes_InsideUl()
    {
        // [c] for checked, never [x], which World Anvil draws as a cross.
        Assert.Equal("[ul]\n- [] todo\n- [c] done\n- [c] DONE\n[/ul]", Convert("- [ ] todo\n- [x] done\n- [X] DONE"));
    }

    [Fact]
    public void Blockquote_BecomesQuote()
    {
        Assert.Equal("[quote]one\ntwo[/quote]", Convert("> one\n> two"));
    }

    [Fact]
    public void NestedBlockquote_NestsQuotes()
    {
        Assert.Equal("[quote]outer\n[quote]inner[/quote][/quote]", Convert("> outer\n> > inner"));
    }

    [Fact]
    public void Blockquote_ContentIsConverted()
    {
        Assert.Equal("[quote][ul]\n- [b]a[/b]\n[/ul][/quote]", Convert("> - **a**"));
    }

    [Fact]
    public void FencedCode_BecomesCode_Verbatim()
    {
        Assert.Equal("[code]var x = a[0];\n**y**[/code]", Convert("```csharp\nvar x = a[0];\n**y**\n```"));
    }

    [Fact]
    public void FencedCode_Unterminated_RunsToTheEnd()
    {
        Assert.Equal("[code]a\nb[/code]", Convert("```\na\nb"));
    }

    [Fact]
    public void Table_BecomesTableWithHeaderCells()
    {
        var markdown = "| A | **B** |\n|:---|---:|\n| 1 | a \\| b |";
        Assert.Equal(
            "[table]\n[tr]\n[th]A[/th]\n[th][b]B[/b][/th]\n[/tr]\n[tr]\n[td]1[/td]\n[td]a | b[/td]\n[/tr]\n[/table]",
            Convert(markdown));
    }

    [Fact]
    public void Empty_ConvertsToEmpty()
    {
        Assert.Equal("", Convert(""));
    }

    // ---- Line ranges (Copy as BBCode on a selection) ----

    [Fact]
    public void Range_ConvertsOnlyThoseLines()
    {
        var doc = new TextDocument("one\n**two**\nthree");
        Assert.Equal("[b]two[/b]", BBCodeConverter.Convert(doc, 2, 2));
    }

    [Fact]
    public void Range_StartingInsideAFence_WidensToTheWholeBlock()
    {
        var doc = new TextDocument("before\n```\ncode\n```\nafter");
        Assert.Equal("[code]code[/code]", BBCodeConverter.Convert(doc, 3, 3));
    }

    [Fact]
    public void Range_InsideATable_WidensToTheWholeTable()
    {
        var doc = new TextDocument("| A |\n|---|\n| 1 |\n| 2 |");
        Assert.Equal(
            "[table]\n[tr]\n[th]A[/th]\n[/tr]\n[tr]\n[td]1[/td]\n[/tr]\n[tr]\n[td]2[/td]\n[/tr]\n[/table]",
            BBCodeConverter.Convert(doc, 4, 4));
    }
}
