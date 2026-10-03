using System.Diagnostics;
using System.Linq;
using System.Text;
using Content.Server.Administration.Logs;
using Content.Shared._Starlight.Language;
using Content.Shared.Database;
using Content.Shared.Paper;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Server._Starlight.Paper;

public sealed partial class PaperLanguageSystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PaperSystem _paper = default!;

    private const int MaxViewHistory = 16;
    private const string FormTag = "[form]";

    // Removed from answers so they can't add markup or break the tags around them
    private static readonly char[] _answerBannedChars = ['[', ']', '\\', '\n', '\r'];

    [SubscribeLocalEvent]
    private void OnFormFill(Entity<PaperComponent> paper, ref PaperFormFillMessage args)
    {
        var timer = Stopwatch.GetTimestamp();
        var actor = args.Actor;
        if (args.Text.Length > paper.Comp.ContentSize)
            return;

        var answer = CleanAnswer(args.Text);
        if (answer.Length == 0)
            return;

        if (TryComp<PaperSaveCooldownComponent>(actor, out var cooldown) && _timing.CurTime < cooldown.NextSave)
        {
            _popup.PopupEntity(Loc.GetString("paper-save-cooldown"), actor, actor);
            return;
        }

        // Started even if the fill fails, so failed fills can't be spammed
        StartCooldown(paper, actor);

        var attempt = new PaperWriteAttemptEvent(paper.Owner, actor);
        RaiseLocalEvent(actor, ref attempt);
        RaiseLocalEvent(paper.Owner, ref attempt);
        if (attempt.Cancelled)
        {
            if (attempt.FailReason is { } reason)
                _popup.PopupEntity(reason, actor, actor);

            return;
        }

        if (GetFillLanguage(paper, actor) is not { } writing)
            return;

        var state = EnsureComp<PaperLanguageStateComponent>(paper);
        var content = paper.Comp.Content;
        if (FindForm(state, actor, content, args.View, args.Index) is not { } position)
        {
            _popup.PopupEntity(Loc.GetString("paper-form-changed"), actor, actor);
            SendView(paper, actor, force: true);
            return;
        }

        var filled = content[..position] + TagAnswer(content, position, writing, answer) + content[(position + FormTag.Length)..];
        if (filled.Length > paper.Comp.ContentSize)
        {
            _popup.PopupEntity(Loc.GetString("paper-full"), actor, actor);
            return;
        }

        _paper.SetContent(paper, filled);
        _meta.SetEntityDescription(paper, "");
        _audio.PlayPvs(paper.Comp.Sound, paper);

        _adminLogger.Add(LogType.Chat,
            LogImpact.Low,
            $"{ToPrettyString(actor):player} has filled in a form on {ToPrettyString(paper):entity} in {writing}: {answer}");
        Logger.GetSawmill("paper.lang").Info($"OnFormFill took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    /// <summary>
    /// Fills a [signature] or [datetime] in the player's writing language, like a form answer.
    /// </summary>
    public override string? FillTag(Entity<PaperComponent> paper, EntityUid actor, string tag, int index, string text)
    {
        var timer = Stopwatch.GetTimestamp();
        if (TryComp<PaperSaveCooldownComponent>(actor, out var cooldown) && _timing.CurTime < cooldown.NextSave)
        {
            _popup.PopupEntity(Loc.GetString("paper-save-cooldown"), actor, actor);
            return null;
        }

        // Started even if the fill fails, so failed fills can't be spammed
        StartCooldown(paper, actor);

        if (GetFillLanguage(paper, actor) is not { } language)
            return null;

        var content = paper.Comp.Content;
        if (FindNthTag(content, tag, index) is not { } position)
            return null;

        var filled = content[..position] + TagAnswer(content, position, language, CleanAnswer(text)) + content[(position + tag.Length)..];
        if (filled.Length > paper.Comp.ContentSize)
        {
            _popup.PopupEntity(Loc.GetString("paper-full"), actor, actor);
            return null;
        }

        Logger.GetSawmill("paper.lang").Info($"FillTag took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return filled;
    }

    /// <summary>
    /// The language a player fills things in with. Without one, they get a popup and null.
    /// </summary>
    private ProtoId<LanguagePrototype>? GetFillLanguage(Entity<PaperComponent> paper, EntityUid actor)
    {
        var state = EnsureComp<PaperLanguageStateComponent>(paper);
        var language = state.WritingLanguages.TryGetValue(actor, out var selected) && CanWrite(actor, selected)
            ? selected
            : GetDefaultWritingLanguage(actor);

        if (language == null)
            _popup.PopupEntity(Loc.GetString("paper-form-no-language"), actor, actor);

        return language;
    }

    private static string CleanAnswer(string text) => string.Concat(text.Where(ch => !_answerBannedChars.Contains(ch))).Trim();

    /// <summary>
    /// Numbers a view sent to a player and remembers the text it showed.
    /// </summary>
    /// <param name="content">The paper text the view was built from, or null if it showed other text.</param>
    private static int AddToHistory(PaperLanguageStateComponent state, EntityUid actor, string? content)
    {
        if (!state.ViewHistory.TryGetValue(actor, out var history))
            state.ViewHistory[actor] = history = [];

        var number = history.Count > 0 ? history[^1].View + 1 : 1;
        history.Add((number, content));

        // The fill dialog can stay open over a few saves, so keep some old views
        if (history.Count > MaxViewHistory)
            history.RemoveAt(0);

        return number;
    }

    /// <summary>
    /// Where a form the player clicked is in the current text, or null if that form changed since they saw it.
    /// </summary>
    private static int? FindForm(PaperLanguageStateComponent state, EntityUid actor, string content, int view, int index)
    {
        var timer = Stopwatch.GetTimestamp();
        if (!state.ViewHistory.TryGetValue(actor, out var history)
            || history.Find(entry => entry.View == view).Content is not { } seen
            || FindNthTag(seen, FormTag, index) is not { } form)
            return null;

        // Text that's the same at the start and end of both versions. A form there is the same form
        var max = Math.Min(seen.Length, content.Length);
        var start = 0;
        while (start < max && seen[start] == content[start])
            start++;

        var end = 0;
        while (end < max - start && seen[^(end + 1)] == content[^(end + 1)])
            end++;

        int position;
        if (form + FormTag.Length <= start)
            position = form;
        else if (form >= seen.Length - end)
            position = form + content.Length - seen.Length;
        else
            return null;

        // The text before it may have changed, like a new backslash escaping it
        Logger.GetSawmill("paper.lang").Info($"FindForm took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return IsTagAt(content, FormTag, position) ? position : null;
    }

    /// <summary>
    /// Where the Nth tag is. Escaped tags aren't buttons, so they aren't counted.
    /// </summary>
    private static int? FindNthTag(string text, string tag, int index)
    {
        if (index < 0)
            return null;

        var count = 0;
        for (var position = text.IndexOf(tag, StringComparison.Ordinal); position != -1; position = text.IndexOf(tag, position + tag.Length, StringComparison.Ordinal))
        {
            if (IsTagAt(text, tag, position) && count++ == index)
                return position;
        }

        return null;
    }

    private static bool IsTagAt(string text, string tag, int position) =>
        string.CompareOrdinal(text, position, tag, 0, tag.Length) == 0 && (position == 0 || text[position - 1] != '\\');

    /// <summary>
    /// The answer tagged with its language. If the form is in a section of another language, that section is closed around it.
    /// </summary>
    private static string TagAnswer(string content, int position, ProtoId<LanguagePrototype> language, string answer)
    {
        var section = GetSectionAt(content, position).Language ?? DefaultLanguage;
        if (section == language)
            return answer;

        var builder = new StringBuilder();
        if (section != DefaultLanguage)
            builder.Append(ClosingTag);

        AppendTagged(builder, language, null, answer);

        if (section != DefaultLanguage)
            builder.Append(OpeningTag(section));

        return builder.ToString();
    }
}
