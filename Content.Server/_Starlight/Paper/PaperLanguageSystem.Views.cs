using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._Starlight.Language;
using Content.Shared._Starlight.Language.Systems;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Paper;

public sealed partial class PaperLanguageSystem
{
    [Dependency] private SharedLanguageSystem _language = default!;

    /// <summary>
    /// Stands in for each letter of a section the editor can't read.
    /// </summary>
    private const char Placeholder = '■';

    // Any markup tag, removed before turning hidden text into placeholders
    private static readonly Regex _markupTagRegex = new(@"\[[^\[\]]*\]", RegexOptions.Compiled);

    // Markup tags and check marks, kept as is when obfuscating. Escaped tags keep their backslash so they stay escaped
    private static readonly Regex _preservedRegex = new(@"\\?\[[^\[\]]*\]|[☐✔✖]", RegexOptions.Compiled);

    protected override List<PaperSection> GetSections(Entity<PaperComponent> paper) =>
        TryComp<PaperLanguageStateComponent>(paper, out var state) ? GetSections(paper, state) : ParseSections(paper.Comp.Content);

    /// <summary>
    /// The paper's sections, cached until the text changes.
    /// </summary>
    private static List<PaperSection> GetSections(Entity<PaperComponent> paper, PaperLanguageStateComponent state)
    {
        var content = paper.Comp.Content;
        if (ReferenceEquals(state.ScrambledContent, content))
            return state.Sections;

        // Saves make a new string even when the text is the same
        if (state.ScrambledContent != content)
        {
            state.Sections = ParseSections(content);
            state.Scrambled.Clear();
        }

        state.ScrambledContent = content;
        return state.Sections;
    }

    /// <summary>
    /// Scrambles a section the first time someone can't read it, then reuses that for everyone.
    /// </summary>
    private string GetScrambled(PaperLanguageStateComponent state, ProtoId<LanguagePrototype> language, string core)
    {
        if (!state.Scrambled.TryGetValue((language, core), out var scrambled))
            state.Scrambled[(language, core)] = scrambled = Obfuscate(core, language);

        return scrambled;
    }

    public override string GetStyledView(Entity<PaperComponent> paper, EntityUid viewer)
    {
        var state = EnsureComp<PaperLanguageStateComponent>(paper);

        var builder = new StringBuilder();
        foreach (var section in GetSections(paper, state))
        {
            var language = section.Language ?? DefaultLanguage;
            var (leading, core, trailing) = SplitWhitespace(section.Text);
            var text = CanRead(viewer, language) ? core : GetScrambled(state, language, core);

            builder.Append(leading);
            if (language == DefaultLanguage || text.Length == 0)
                builder.Append(text);
            else
                AppendTagged(builder, language, null, text);
            builder.Append(trailing);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Paper text for the viewer's paper UI, with locked sections tagged with an id.
    /// </summary>
    private string GetUiView(Entity<PaperComponent> paper, PaperLanguageStateComponent state, EntityUid viewer, bool editing)
    {
        if (!state.HiddenSections.TryGetValue(viewer, out var hidden))
        {
            hidden = [];
            state.HiddenSections[viewer] = hidden;
        }

        // Reuse ids the viewer already has, so text they're editing still matches
        var existingIds = new Dictionary<HiddenPaperSection, Queue<int>>();
        foreach (var (existingId, existing) in hidden.OrderBy(x => x.Key))
        {
            if (!existingIds.TryGetValue(existing, out var ids))
                existingIds[existing] = ids = new Queue<int>();

            ids.Enqueue(existingId);
        }

        var builder = new StringBuilder();
        foreach (var section in GetSections(paper, state))
        {
            var language = section.Language ?? DefaultLanguage;

            if (string.IsNullOrWhiteSpace(section.Text))
            {
                builder.Append(section.Text);
                continue;
            }

            var (leading, core, trailing) = SplitWhitespace(section.Text);
            builder.Append(leading);

            if (CanWrite(viewer, language))
            {
                if (language == DefaultLanguage)
                    builder.Append(core);
                else
                    AppendTagged(builder, language, null, core);
            }
            else
            {
                var rendered = RenderLocked(state, viewer, language, core, editing);
                var id = GetHiddenSectionId(hidden, existingIds, new HiddenPaperSection(language, core, rendered));
                AppendTagged(builder, language, id, rendered);
            }

            builder.Append(trailing);
        }

        return builder.ToString();
    }

    private static int GetHiddenSectionId(
        Dictionary<int, HiddenPaperSection> hidden,
        Dictionary<HiddenPaperSection, Queue<int>> existingIds,
        HiddenPaperSection section)
    {
        if (existingIds.TryGetValue(section, out var ids) && ids.TryDequeue(out var existingId))
            return existingId;

        // Ids are only ever added, so they run from 1 to the count
        var id = hidden.Count + 1;
        hidden[id] = section;
        return id;
    }

    private string RenderLocked(PaperLanguageStateComponent state, EntityUid viewer, ProtoId<LanguagePrototype> language, string text, bool editing) => CanRead(viewer, language) ? text : editing ? ToPlaceholder(text) : GetScrambled(state, language, text);

    private static string ToPlaceholder(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in _markupTagRegex.Replace(text, string.Empty))
            builder.Append(char.IsWhiteSpace(ch) ? ch : Placeholder);

        return builder.ToString();
    }

    private string Obfuscate(string text, ProtoId<LanguagePrototype> language)
    {
        if (!_prototype.TryIndex(language, out var proto))
            return text;

        var builder = new StringBuilder();
        var position = 0;
        foreach (Match tag in _preservedRegex.Matches(text))
        {
            AppendObfuscated(builder, text[position..tag.Index], proto);
            builder.Append(tag.Value);
            position = tag.Index + tag.Length;
        }

        AppendObfuscated(builder, text[position..], proto);
        return builder.ToString();
    }

    private void AppendObfuscated(StringBuilder builder, string text, LanguagePrototype language)
    {
        // Line by line, so the layout survives
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                builder.Append('\n');

            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                builder.Append(line);
                continue;
            }

            builder.Append(line[..line.IndexOf(trimmed[0])]);
            builder.Append(_language.ObfuscateSpeech(trimmed, language));
            builder.Append(line[(line.LastIndexOf(trimmed[^1]) + 1)..]);
        }
    }
}
