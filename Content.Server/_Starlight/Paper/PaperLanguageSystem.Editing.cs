using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._Starlight.Language;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Paper;

public sealed partial class PaperLanguageSystem
{
    // Check boxes can be ticked through a save, even in sections the player can't read. Forms have their own message
    private static readonly Regex _checkTagRegex = new(@"\[check\]", RegexOptions.Compiled);

    // Buttons rather than text, so they don't belong to any language
    private static readonly Regex _interactiveTagRegex = new(@"\[form\]|\[check\]|\[signature\]|\[datetime\]", RegexOptions.Compiled);

    /// <summary>
    /// Marks a [check] tag can be filled in with.
    /// </summary>
    private static readonly string[] CheckMarks = ["☐", "✔", "✖"];

    private const string CheckTag = "[check]";

    private PaperMergeResult MergeEdit(
        Entity<PaperComponent> paper,
        PaperLanguageStateComponent state,
        EntityUid editor,
        string submitted)
    {
        var writingLanguage = state.WritingLanguages.TryGetValue(editor, out var requested) && CanWrite(editor, requested)
            ? requested
            : GetDefaultWritingLanguage(editor);

        state.HiddenSections.TryGetValue(editor, out var hidden);
        var usedIds = new HashSet<int>();
        var result = new List<(ProtoId<LanguagePrototype>, string)>();

        // Locked sections on the paper, counted so each is only restored once
        var locked = new Dictionary<(ProtoId<LanguagePrototype>, string), int>();
        var lockedLeft = 0;
        foreach (var existing in GetSections(paper, state))
        {
            var language = existing.Language ?? DefaultLanguage;
            if (string.IsNullOrWhiteSpace(existing.Text) || CanWrite(editor, language))
                continue;

            var key = (language, SplitWhitespace(existing.Text).Core);
            locked[key] = locked.GetValueOrDefault(key) + 1;
            lockedLeft++;
        }

        var convertedFrom = new HashSet<ProtoId<LanguagePrototype>>();
        var droppedText = false;
        var tamperedSections = 0;

        foreach (var section in ParseSections(submitted))
        {
            if (section.Id is { } id)
            {
                if (TryRestoreHiddenSection(hidden, usedIds, locked, id, section) is { } restored)
                {
                    result.Add(restored);
                    lockedLeft--;
                }
                else
                    tamperedSections++;

                continue;
            }

            var text = section.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                result.Add((writingLanguage ?? DefaultLanguage, text));
                continue;
            }

            var language = section.Language ?? DefaultLanguage;
            if (CanWrite(editor, language))
            {
                result.Add((language, text));
                continue;
            }

            // Unchanged text in a language they lost stays as is, instead of being rewritten readable
            var key = (language, SplitWhitespace(text).Core);
            if (locked.TryGetValue(key, out var count) && count > 0)
            {
                locked[key] = count - 1;
                lockedLeft--;
                result.Add((language, text));
                continue;
            }

            if (writingLanguage is { } writing)
            {
                result.Add((writing, text));

                // Tags come from the client, so only report real languages
                if (_prototype.HasIndex(language))
                    convertedFrom.Add(language);
            }
            else
            {
                droppedText = true;
            }
        }

        // Leftover locked sections were changed or removed. Duplicates can fail too, so cap it
        var erased = Math.Min(tamperedSections, lockedLeft);
        return new PaperMergeResult(Serialize(result), [..convertedFrom], writingLanguage, droppedText, erased, lockedLeft - erased);
    }

    private static (ProtoId<LanguagePrototype>, string)? TryRestoreHiddenSection(
        Dictionary<int, HiddenPaperSection>? hidden,
        HashSet<int> usedIds,
        Dictionary<(ProtoId<LanguagePrototype>, string), int> locked,
        int id,
        PaperSection section)
    {
        if (hidden == null
            || !hidden.TryGetValue(id, out var original)
            || section.Language != original.Language
            || !usedIds.Add(id)
            || !TryMatchFills(original.Rendered, section.Text, out var fills))
            return null;

        // Someone else may have removed it since
        var key = (original.Language, original.Original);
        if (!locked.TryGetValue(key, out var count) || count == 0)
            return null;

        locked[key] = count - 1;

        return (original.Language, ApplyFills(original.Original, fills));
    }

    private static Regex? BuildFillPattern(string text)
    {
        var pattern = new StringBuilder("^");
        var position = 0;
        foreach (Match tag in _checkTagRegex.Matches(text))
        {
            pattern.Append(Regex.Escape(text[position..tag.Index]));
            pattern.Append($@"(\[check\]|[{string.Concat(CheckMarks)}])");
            position = tag.Index + tag.Length;
        }

        if (position == 0)
            return null;

        pattern.Append(Regex.Escape(text[position..]));
        pattern.Append('$');

        return new Regex(pattern.ToString());
    }

    /// <summary>
    /// Whether the text is unchanged apart from ticked check boxes.
    /// </summary>
    private static bool TryMatchFills(string rendered, string submitted, out List<string?> fills)
    {
        fills = [];
        if (rendered == submitted)
            return true;

        if (BuildFillPattern(rendered) is not { } pattern)
            return false;

        var match = pattern.Match(submitted);
        if (!match.Success)
            return false;

        for (var i = 1; i < match.Groups.Count; i++)
        {
            var value = match.Groups[i].Value;
            fills.Add(value == CheckTag ? null : value);
        }

        return true;
    }

    private static string ApplyFills(string original, List<string?> fills)
    {
        var builder = new StringBuilder();
        var position = 0;
        var index = 0;

        foreach (Match tag in _checkTagRegex.Matches(original))
        {
            builder.Append(original[position..tag.Index]);
            builder.Append(index < fills.Count && fills[index] is { } fill ? fill : tag.Value);
            position = tag.Index + tag.Length;
            index++;
        }

        builder.Append(original[position..]);
        return builder.ToString();
    }

    /// <summary>
    /// Builds paper content from sections.
    /// </summary>
    private static string Serialize(IEnumerable<(ProtoId<LanguagePrototype> Language, string Text)> sections)
    {
        var builder = new StringBuilder();
        var run = new StringBuilder();
        var whitespace = new StringBuilder();
        var whitespaceHasNewline = false;
        ProtoId<LanguagePrototype>? runLanguage = null;

        foreach (var (language, text) in sections)
        {
            if (text.Length == 0)
                continue;

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
        return builder.ToString();
    }

    private static void AppendRun(StringBuilder builder, ProtoId<LanguagePrototype>? language, StringBuilder run)
    {
        if (language is not { } runLanguage || run.Length == 0)
            return;

        if (runLanguage == DefaultLanguage)
            builder.Append(run);
        else
            AppendLanguageRun(builder, runLanguage, run.ToString());

        run.Clear();
    }

    private static void AppendLanguageRun(StringBuilder builder, ProtoId<LanguagePrototype> language, string text)
    {
        var position = 0;
        foreach (Match tag in _interactiveTagRegex.Matches(text))
        {
            AppendLanguagePart(builder, language, text[position..tag.Index]);
            builder.Append(tag.Value);
            position = tag.Index + tag.Length;
        }

        AppendLanguagePart(builder, language, text[position..]);
    }

    private static void AppendLanguagePart(StringBuilder builder, ProtoId<LanguagePrototype> language, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            builder.Append(text);
        else
            AppendSection(builder, language, null, text);
    }
}

/// <summary>
/// Result of saving paper text, so the editor can be told what was lost.
/// </summary>
public readonly record struct PaperMergeResult(
    string Content,
    List<ProtoId<LanguagePrototype>> ConvertedFrom,
    ProtoId<LanguagePrototype>? ConvertedTo,
    bool DroppedText,
    int ErasedSections,
    int RemovedSections);
