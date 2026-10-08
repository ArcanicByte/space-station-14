using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._Starlight.Language;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Paper;

public sealed partial class PaperLanguageSystem
{
    // Buttons, not text. They don't belong to a language. Escaped ones are just text
    [GeneratedRegex(@"(?<=(?:^|[^\\])(?:\\\\)*)\[(?:form|check|signature|datetime)\]")]
    private static partial Regex InteractiveTagRegex();

    private PaperMergeResult MergeEdit(
        Entity<PaperComponent> paper,
        PaperLanguageStateComponent state,
        EntityUid editor,
        ProtoId<LanguagePrototype> writingLanguage,
        string submitted)
    {
        var timer = Stopwatch.GetTimestamp();
        var hidden = state.Viewers.GetValueOrDefault(editor)?.HiddenSections;
        var result = new List<(ProtoId<LanguagePrototype>, string)>();

        // Sections on the paper, counted to restore each one only once. Includes writable ones,
        // a section sent locked still has to come back after the editor learns its language
        var onPaper = new Dictionary<(ProtoId<LanguagePrototype>, string), int>();
        foreach (var existing in GetSections(paper, state))
        {
            if (string.IsNullOrWhiteSpace(existing.Text))
                continue;

            var key = (existing.Language ?? DefaultLanguage, SplitWhitespace(existing.Text).Core);
            onPaper[key] = onPaper.GetValueOrDefault(key) + 1;
        }

        var convertedFrom = new HashSet<ProtoId<LanguagePrototype>>();

        foreach (var section in ParseSections(submitted))
        {
            if (section.Id is { } id)
            {
                if (TryRestoreHiddenSection(editor, hidden, onPaper, id, section) is { } restored)
                    result.Add(restored);

                continue;
            }

            var text = section.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                result.Add((writingLanguage, text));
                continue;
            }

            var language = section.Language ?? DefaultLanguage;
            if (CanWrite(editor, language))
            {
                result.Add((language, text));
                continue;
            }

            // Unchanged text in a language they lost stays as is
            if (TryTakeSection(onPaper, (language, SplitWhitespace(text).Core)))
            {
                result.Add((language, text));
                continue;
            }

            result.Add((writingLanguage, text));

            // Tags come from the client, only report real languages
            if (_prototype.HasIndex(language))
                convertedFrom.Add(language);
        }

        // Leftover sections they can't write were changed or removed
        var removed = 0;
        foreach (var ((language, _), count) in onPaper)
        {
            if (!CanWrite(editor, language))
                removed += count;
        }

        Logger.GetSawmill("paper.lang").Info($"MergeEdit took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return new PaperMergeResult(Serialize(result), [..convertedFrom], removed);
    }

    private (ProtoId<LanguagePrototype>, string)? TryRestoreHiddenSection(
        EntityUid editor,
        Dictionary<int, HiddenPaperSection>? hidden,
        Dictionary<(ProtoId<LanguagePrototype>, string), int> onPaper,
        int id,
        PaperSection section)
    {
        if (hidden == null
            || !hidden.TryGetValue(id, out var original)
            || original.Rendered != section.Text)
            return null;

        // Someone else may have removed it since
        if (!TryTakeSection(onPaper, (original.Language, original.Original)))
            return null;

        // Translate changes the language of sections they can read, the text itself can't change
        var language = section.Language ?? DefaultLanguage;
        var translated = language != original.Language && CanRead(editor, original.Language) && CanWrite(editor, language);
        return (translated ? language : original.Language, original.Original);
    }

    /// <summary>
    /// Uses up one copy of a section on the paper, false if none are left.
    /// </summary>
    private static bool TryTakeSection(Dictionary<(ProtoId<LanguagePrototype>, string), int> onPaper, (ProtoId<LanguagePrototype>, string) key)
    {
        if (!onPaper.TryGetValue(key, out var count) || count == 0)
            return false;

        onPaper[key] = count - 1;
        return true;
    }

    /// <summary>
    /// Builds paper content from sections.
    /// </summary>
    private static string Serialize(IEnumerable<(ProtoId<LanguagePrototype> Language, string Text)> sections)
    {
        var timer = Stopwatch.GetTimestamp();
        var builder = new StringBuilder();
        var run = new StringBuilder();
        var whitespace = new StringBuilder();
        var whitespaceHasNewline = false;
        ProtoId<LanguagePrototype>? runLanguage = null;

        foreach (var (language, text) in sections)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                whitespace.Append(text);
                whitespaceHasNewline |= text.Contains('\n');
                continue;
            }

            // Every line gets its own tag
            if (runLanguage == language && !whitespaceHasNewline)
            {
                run.Append(whitespace).Append(text);
                whitespace.Clear();
                continue;
            }

            AppendRun(builder, runLanguage, run);
            builder.Append(whitespace);
            whitespace.Clear();
            whitespaceHasNewline = false;

            runLanguage = language;
            run.Append(text);
        }

        AppendRun(builder, runLanguage, run);
        builder.Append(whitespace);
        Logger.GetSawmill("paper.lang").Info($"Serialize took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return builder.ToString();
    }

    private static void AppendRun(StringBuilder builder, ProtoId<LanguagePrototype>? language, StringBuilder run)
    {
        if (language is not { } runLanguage)
            return;

        var text = run.ToString();
        var position = 0;
        foreach (Match tag in InteractiveTagRegex().Matches(text))
        {
            AppendSection(builder, runLanguage, text[position..tag.Index]);
            builder.Append(tag.Value);
            position = tag.Index + tag.Length;
        }

        AppendSection(builder, runLanguage, text[position..]);
        run.Clear();
    }
}

/// <summary>
/// Result of saving paper text, including what was lost.
/// </summary>
public readonly record struct PaperMergeResult(
    string Content,
    List<ProtoId<LanguagePrototype>> ConvertedFrom,
    int RemovedSections);
