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
        var sections = new List<PaperSection>();
        ProtoId<LanguagePrototype>? language = null;
        int? id = null;
        var position = 0;

        foreach (Match match in _languageTagRegex.Matches(content))
        {
            AddSection(sections, language, content[position..match.Index], id);
            position = match.Index + match.Length;

            if (match.Groups["lang"].Success)
            {
                language = match.Groups["lang"].Value;
                id = match.Groups["id"].Success && int.TryParse(match.Groups["id"].Value, out var parsed) ? parsed : null;
            }
            else
            {
                language = null;
                id = null;
            }
        }

        AddSection(sections, language, content[position..], id);
        return sections;
    }

    private static void AddSection(List<PaperSection> sections, ProtoId<LanguagePrototype>? language, string text, int? id)
    {
        // Hidden sections are kept even when empty, so they can still be matched up on save
        if (text.Length == 0 && id == null)
            return;

        sections.Add(new PaperSection(language, text, id));
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
        ProtoId<LanguagePrototype>? language = null;
        var locked = false;
        foreach (Match match in _languageTagRegex.Matches(text))
        {
            if (match.Index + match.Length > position)
                break;

            language = match.Groups["lang"].Success ? match.Groups["lang"].Value : null;
            locked = match.Groups["id"].Success;
        }

        return (language, locked);
    }

    public static string OpeningTag(ProtoId<LanguagePrototype> language) => $"[lang=\"{language}\"]";
    public const string ClosingTag = "[/lang]";
    public static string StripLanguageTags(string content) => _languageTagRegex.Replace(content, string.Empty);

    /// <summary>
    /// Changes every section that isn't locked to the given language.
    /// </summary>
    public static string TranslateUnlockedSections(string text, ProtoId<LanguagePrototype> language)
    {
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

        return builder.ToString();
    }
}
