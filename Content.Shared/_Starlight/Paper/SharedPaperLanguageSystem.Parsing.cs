using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._Starlight.Language;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Paper;

public abstract partial class SharedPaperLanguageSystem
{
    public const string ClosingTag = "[/lang]";
    public const string FormTag = "[form]";
    public const string CheckTag = "[check]";
    public const string SignatureTag = "[signature]";
    public const string DateTimeTag = "[datetime]";

    /// <summary>
    /// Tags filled in by clicking them in read mode, written in the selected language.
    /// </summary>
    public static readonly string[] FillableTags = [FormTag, SignatureTag, DateTimeTag];

    public readonly record struct PaperSection(ProtoId<LanguagePrototype>? Language, string Text, int? Id);

    public static List<PaperSection> ParseSections(string content)
    {
        var timer = Stopwatch.GetTimestamp();
        var sections = new List<PaperSection>();
        var open = new Stack<(ProtoId<LanguagePrototype>? Language, int? Id)>();
        var position = 0;

        foreach (Match match in _languageTagRegex.Matches(content))
        {
            open.TryPeek(out var current);
            AddSection(sections, current.Language, content[position..match.Index], current.Id);
            position = match.Index + match.Length;
            ApplyTag(match, open);
        }

        open.TryPeek(out var last);
        AddSection(sections, last.Language, content[position..], last.Id);
        Logger.GetSawmill("paper.lang").Info($"ParseSections took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return sections;
    }

    private static void AddSection(List<PaperSection> sections, ProtoId<LanguagePrototype>? language, string text, int? id)
    {
        // Hidden sections are kept even when empty, save still has to match them up
        if (text.Length == 0 && id == null)
            return;

        sections.Add(new PaperSection(language, text, id));
    }

    /// <summary>
    /// Opens or closes a section. Tags nest, closing one returns to the outer language.
    /// </summary>
    private static void ApplyTag(Match tag, Stack<(ProtoId<LanguagePrototype>? Language, int? Id)> open)
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
    /// Appends text in a language with its outer whitespace outside the tag. Galactic Common is left untagged.
    /// </summary>
    protected static void AppendSection(StringBuilder builder, ProtoId<LanguagePrototype> language, string text)
    {
        var (leading, core, trailing) = SplitWhitespace(text);
        core = EscapeTrailingBackslash(EscapeFakeLanguageTags(core));
        builder.Append(leading);
        if (language == DefaultLanguage || core.Length == 0)
            builder.Append(core);
        else
            AppendTagged(builder, language, null, core);
        builder.Append(trailing);
    }

    /// <summary>
    /// Doubles a lone backslash at the end, so it can't escape the tag after it. Still shows as one.
    /// </summary>
    public static string EscapeTrailingBackslash(string text) => IsEscaped(text, text.Length) ? text + '\\' : text;

    public static bool StartsLanguageTag(string text, int position) =>
        text.AsSpan(position).StartsWith(ClosingTag, StringComparison.Ordinal)
        || text.AsSpan(position).StartsWith("[lang=", StringComparison.Ordinal);

    /// <summary>
    /// Escapes lang tags in text so they show as text. Real ones were already taken out as sections.
    /// </summary>
    protected static string EscapeFakeLanguageTags(string text) =>
        _fakeLanguageTagRegex.Replace(text, match => IsEscaped(text, match.Index) ? match.Value : "\\" + match.Value);

    /// <summary>
    /// Escapes a lang tag cut off at the end of the text, so whatever is added after it can't finish it.
    /// </summary>
    protected static string EscapeCutOffLanguageTag(string text)
    {
        var open = text.LastIndexOf('[');
        if (open == -1 || text.IndexOf(']', open) != -1 || IsEscaped(text, open) || !_fakeLanguageTagRegex.IsMatch(text, open))
            return text;

        return text.Insert(open, "\\");
    }

    /// <summary>
    /// Whether the character at a position is escaped by an odd number of backslashes before it.
    /// </summary>
    public static bool IsEscaped(string text, int position)
    {
        var count = 0;
        while (position > count && text[position - count - 1] == '\\')
            count++;

        return count % 2 == 1;
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
        builder.Append(ClosingTag);
    }

    public static (ProtoId<LanguagePrototype>? Language, bool Locked) GetSectionAt(string text, int position)
    {
        var open = new Stack<(ProtoId<LanguagePrototype>? Language, int? Id)>();
        foreach (Match match in _languageTagRegex.Matches(text))
        {
            if (match.Index + match.Length > position)
                break;

            ApplyTag(match, open);
        }

        open.TryPeek(out var current);
        return (current.Language, current.Id != null);
    }

    public static string OpeningTag(ProtoId<LanguagePrototype> language) => $"[lang=\"{language}\"]";
    public static string StripLanguageTags(string content) => _languageTagRegex.Replace(content, string.Empty);

    /// <summary>
    /// Changes every section to the given language. Locked sections keep their id and only change if they can be read.
    /// </summary>
    public static string TranslateSections(string text, ProtoId<LanguagePrototype> language, Func<ProtoId<LanguagePrototype>, bool> canRead)
    {
        var timer = Stopwatch.GetTimestamp();
        var builder = new StringBuilder();
        foreach (var section in ParseSections(text))
        {
            if (section.Id == null)
            {
                AppendSection(builder, language, section.Text);
                continue;
            }

            var current = section.Language ?? DefaultLanguage;
            AppendTagged(builder, canRead(current) ? language : current, section.Id, section.Text);
        }

        Logger.GetSawmill("paper.lang").Info($"TranslateSections took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return builder.ToString();
    }
}
