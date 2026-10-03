using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._Starlight.Language;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Paper;

public abstract partial class SharedPaperLanguageSystem
{
    public readonly record struct PaperSection(ProtoId<LanguagePrototype>? Language, string Text, int? Id);

    public static List<PaperSection> ParseSections(string content)
    {
        var timer = Stopwatch.GetTimestamp();
        var sections = new List<PaperSection>();
        var open = new Stack<(ProtoId<LanguagePrototype> Language, int? Id)>();
        var position = 0;

        foreach (Match match in _languageTagRegex.Matches(content))
        {
            var (language, id) = Top(open);
            AddSection(sections, language, content[position..match.Index], id);
            position = match.Index + match.Length;
            ApplyTag(match, open);
        }

        var (lastLanguage, lastId) = Top(open);
        AddSection(sections, lastLanguage, content[position..], lastId);
        Logger.GetSawmill("paper.lang").Info($"ParseSections took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return sections;
    }

    private static void AddSection(List<PaperSection> sections, ProtoId<LanguagePrototype>? language, string text, int? id)
    {
        // Hidden sections are kept even when empty, so they can still be matched up on save
        if (text.Length == 0 && id == null)
            return;

        sections.Add(new PaperSection(language, text, id));
    }

    /// <summary>
    /// Opens or closes a section. Tags nest, so closing one goes back to the language around it.
    /// </summary>
    private static void ApplyTag(Match tag, Stack<(ProtoId<LanguagePrototype> Language, int? Id)> open)
    {
        if (!tag.Groups["lang"].Success)
        {
            open.TryPop(out _);
            return;
        }

        int? id = tag.Groups["id"].Success && int.TryParse(tag.Groups["id"].Value, out var parsed) ? parsed : null;
        open.Push((tag.Groups["lang"].Value, id));
    }

    /// <summary>
    /// The innermost open section, or untagged text if none are open.
    /// </summary>
    private static (ProtoId<LanguagePrototype>? Language, int? Id) Top(Stack<(ProtoId<LanguagePrototype> Language, int? Id)> open)
    {
        if (open.TryPeek(out var top))
            return (top.Language, top.Id);

        return (null, null);
    }

    protected static void AppendSection(StringBuilder builder, ProtoId<LanguagePrototype> language, int? id, string text)
    {
        var (leading, core, trailing) = SplitWhitespace(text);
        builder.Append(leading);
        AppendTagged(builder, language, id, core);
        builder.Append(trailing);
    }

    protected static (string Leading, string Core, string Trailing) SplitWhitespace(string text)
    {
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
            start++;

        var end = text.Length;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;

        return (text[..start], text[start..end], text[end..]);
    }

    protected static void AppendTagged(StringBuilder builder, ProtoId<LanguagePrototype> language, int? id, string text)
    {
        builder.Append($"[lang=\"{language}\"");
        if (id != null)
            builder.Append($" id={id}");
        builder.Append(']');
        builder.Append(text);
        builder.Append("[/lang]");
    }

    /// <summary>
    /// Gets the language section at a position, and whether it's locked.
    /// </summary>
    public static (ProtoId<LanguagePrototype>? Language, bool Locked) GetSectionAt(string text, int position)
    {
        var open = new Stack<(ProtoId<LanguagePrototype> Language, int? Id)>();
        foreach (Match match in _languageTagRegex.Matches(text))
        {
            if (match.Index + match.Length > position)
                break;

            ApplyTag(match, open);
        }

        var (language, id) = Top(open);
        return (language, id != null);
    }

    public static string OpeningTag(ProtoId<LanguagePrototype> language) => $"[lang=\"{language}\"]";
    public const string ClosingTag = "[/lang]";
    public static string StripLanguageTags(string content) => _languageTagRegex.Replace(content, string.Empty);

    /// <summary>
    /// Changes every section that isn't locked to the given language.
    /// </summary>
    public static string TranslateUnlockedSections(string text, ProtoId<LanguagePrototype> language)
    {
        var timer = Stopwatch.GetTimestamp();
        var builder = new StringBuilder();
        foreach (var section in ParseSections(text))
        {
            if (section.Id != null)
                AppendTagged(builder, section.Language ?? DefaultLanguage, section.Id, section.Text);
            else if (language == DefaultLanguage || string.IsNullOrWhiteSpace(section.Text))
                builder.Append(section.Text);
            else
                AppendSection(builder, language, null, section.Text);
        }

        Logger.GetSawmill("paper.lang").Info($"TranslateUnlockedSections took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return builder.ToString();
    }
}
