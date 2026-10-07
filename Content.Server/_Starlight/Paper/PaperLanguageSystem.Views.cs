using System.Diagnostics;
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

    private const char Placeholder = '■';

    // Any markup tag, removed before turning hidden text into placeholders
    [GeneratedRegex(@"\[[^\[\]]*\]")]
    private static partial Regex MarkupTagRegex();

    // Markup tags and check marks, kept as is when obfuscating. Escaped tags keep their backslash
    [GeneratedRegex(@"\\?\[[^\[\]]*\]|[" + CheckMarks + "]")]
    private static partial Regex PreservedRegex();

    protected override List<PaperSection> GetSections(Entity<PaperComponent> paper)
    {
        if (TryComp<PaperLanguageStateComponent>(paper, out var state))
            return GetSections(paper, state);

        return base.GetSections(paper);
    }

    /// <summary>
    /// The paper's sections, cached until the text changes.
    /// </summary>
    private static List<PaperSection> GetSections(Entity<PaperComponent> paper, PaperLanguageStateComponent state)
    {
        SyncContent(paper, state);
        return state.Sections;
    }

    /// <summary>
    /// Re-parses the paper and bumps the content version if its text changed.
    /// </summary>
    private static void SyncContent(Entity<PaperComponent> paper, PaperLanguageStateComponent state)
    {
        var timer = Stopwatch.GetTimestamp();
        var content = paper.Comp.Content;
        if (ReferenceEquals(state.SectionsContent, content))
            return;

        // Saves make a new string even when the text is the same
        if (state.SectionsContent != content)
        {
            state.ContentVersion++;
            state.Sections = ParseSections(content);

            // Keep scrambles for text still on the paper, small edits shouldn't redo them all
            var onPaper = new HashSet<(ProtoId<LanguagePrototype>, string)>();
            foreach (var section in state.Sections)
                onPaper.Add((section.Language ?? DefaultLanguage, SplitWhitespace(section.Text).Core));

            foreach (var key in state.Scrambled.Keys)
            {
                if (!onPaper.Contains(key))
                    state.Scrambled.Remove(key);
            }
        }

        state.SectionsContent = content;
        Logger.GetSawmill("paper.lang").Info($"SyncContent took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    /// <summary>
    /// Scrambles a section the first time someone can't read it, then reuses that for everyone.
    /// </summary>
    private ScrambledText GetScrambled(PaperLanguageStateComponent state, ProtoId<LanguagePrototype> language, string core)
    {
        if (!state.Scrambled.TryGetValue((language, core), out var scrambled))
            state.Scrambled[(language, core)] = scrambled = new ScrambledText(Obfuscate(core, language));

        return scrambled;
    }

    // Cached so long papers with several editors don't rebuild placeholders per viewer
    // Squares follow the scrambled text, so the original word lengths stay hidden
    private string GetPlaceholder(PaperLanguageStateComponent state, ProtoId<LanguagePrototype> language, string core)
    {
        var scrambled = GetScrambled(state, language, core);
        if (scrambled.Placeholder != null)
            return scrambled.Placeholder;

        var builder = new StringBuilder();
        foreach (var ch in MarkupTagRegex().Replace(scrambled.Text, string.Empty))
            builder.Append(char.IsWhiteSpace(ch) ? ch : Placeholder);

        return scrambled.Placeholder = builder.ToString();
    }

    public override string GetStyledView(Entity<PaperComponent> paper, EntityUid viewer)
    {
        var timer = Stopwatch.GetTimestamp();
        var state = EnsureComp<PaperLanguageStateComponent>(paper);

        var builder = new StringBuilder();
        foreach (var section in GetSections(paper, state))
        {
            var language = section.Language ?? DefaultLanguage;
            if (CanRead(viewer, language))
            {
                AppendSection(builder, language, section.Text);
                continue;
            }

            var (leading, core, trailing) = SplitWhitespace(section.Text);
            builder.Append(leading);
            AppendSection(builder, language, GetScrambled(state, language, core).Text);
            builder.Append(trailing);
        }

        Logger.GetSawmill("paper.lang").Info($"GetStyledView took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return builder.ToString();
    }

    /// <summary>
    /// Paper text for the viewer's paper UI, with locked sections tagged with an id.
    /// </summary>
    private string GetUiView(Entity<PaperComponent> paper, PaperLanguageStateComponent state, EntityUid viewer, bool editing)
    {
        var timer = Stopwatch.GetTimestamp();
        var viewerState = GetViewer(state, viewer);
        var hidden = viewerState.HiddenSections;

        // Reuse the viewer's existing ids, text they're editing has to keep matching
        var existingIds = new Dictionary<HiddenPaperSection, Queue<int>>();
        foreach (var (existingId, existing) in hidden.OrderBy(x => x.Key))
        {
            if (!existingIds.TryGetValue(existing, out var ids))
                existingIds[existing] = ids = new Queue<int>();

            ids.Enqueue(existingId);
        }

        var onPaper = new HashSet<(ProtoId<LanguagePrototype>, string)>();
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
            onPaper.Add((language, core));
            builder.Append(leading);

            if (CanWrite(viewer, language))
            {
                AppendSection(builder, language, core);
            }
            else
            {
                var rendered = CanRead(viewer, language) ? core
                    : editing ? GetPlaceholder(state, language, core)
                    : GetScrambled(state, language, core).Text;
                var id = GetHiddenSectionId(state, viewerState, existingIds, new HiddenPaperSection(language, core, rendered));
                AppendTagged(builder, language, id, rendered);
            }

            builder.Append(trailing);
        }

        // Sections no longer on the paper can't be restored anyway
        foreach (var (id, section) in hidden)
        {
            if (!onPaper.Contains((section.Language, section.Original)))
                hidden.Remove(id);
        }

        Logger.GetSawmill("paper.lang").Info($"GetUiView took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return builder.ToString();
    }

    private static int GetHiddenSectionId(
        PaperLanguageStateComponent state,
        PaperViewerState viewerState,
        Dictionary<HiddenPaperSection, Queue<int>> existingIds,
        HiddenPaperSection section)
    {
        if (existingIds.TryGetValue(section, out var ids) && ids.TryDequeue(out var existingId))
            return existingId;

        // Ids are never reused, even after a save or reopen. An old tag must not match a different section
        var id = ++state.NextHiddenId;
        viewerState.HiddenSections[id] = section;
        return id;
    }

    private string Obfuscate(string text, ProtoId<LanguagePrototype> language)
    {
        if (!_prototype.TryIndex(language, out var proto))
            return text;

        var builder = new StringBuilder();
        var position = 0;
        foreach (Match tag in PreservedRegex().Matches(text))
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
        // Line by line to keep the layout
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                builder.Append('\n');

            var (leading, core, trailing) = SplitWhitespace(lines[i]);
            builder.Append(leading);
            if (core.Length > 0)
                builder.Append(_language.ObfuscateSpeech(core, language));
            builder.Append(trailing);
        }
    }
}
