using System.Diagnostics;
using System.Linq;
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

    // Stripped from answers, they could add markup or break the surrounding tags
    private static readonly char[] _answerBannedChars = ['[', ']', '\\', '\n', '\r'];

    /// <summary>
    /// Marks a [check] tag can be filled in with.
    /// </summary>
    private const string CheckMarks = "☐✔✖";

    [SubscribeLocalEvent]
    private void OnFormFill(Entity<PaperComponent> paper, ref PaperFormFillMessage args)
    {
        var timer = Stopwatch.GetTimestamp();
        var actor = args.Actor;
        if (args.Text.Length > paper.Comp.ContentSize)
            return;

        var answer = CleanAnswer(args.Text);
        if (answer.Length == 0 || !CanFill(paper, actor) || !TryStartFill(paper, actor, out var writing))
            return;

        if (FindCurrentTag(paper, actor, FormTag, args.ContentVersion, args.Index) is not { } position)
            return;

        if (FillAt(paper, actor, position, FormTag.Length, writing, answer) is not { } filled)
            return;

        ApplyFill(paper, filled);
        _adminLogger.Add(LogType.Chat,
            LogImpact.Low,
            $"{ToPrettyString(actor):player} has filled in a form on {ToPrettyString(paper):entity} in {writing}: {answer}");
        Logger.GetSawmill("paper.lang").Info($"OnFormFill took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    [SubscribeLocalEvent]
    private void OnCheckFill(Entity<PaperComponent> paper, ref PaperCheckFillMessage args)
    {
        var timer = Stopwatch.GetTimestamp();
        var actor = args.Actor;
        if (!CheckMarks.Contains(args.Mark) || !CanFill(paper, actor) || IsOnSaveCooldown(actor))
            return;

        StartCooldown(paper, actor);
        if (FindCurrentTag(paper, actor, CheckTag, args.ContentVersion, args.Index) is not { } position)
            return;

        var content = paper.Comp.Content;
        ApplyFill(paper, content[..position] + args.Mark + content[(position + CheckTag.Length)..]);
        _adminLogger.Add(LogType.Chat,
            LogImpact.Low,
            $"{ToPrettyString(actor):player} has marked a check box on {ToPrettyString(paper):entity} with {args.Mark}");
        Logger.GetSawmill("paper.lang").Info($"OnCheckFill took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    private void ApplyFill(Entity<PaperComponent> paper, string content)
    {
        _paper.SetContent(paper, content);
        _meta.SetEntityDescription(paper, "");
        _audio.PlayPvs(paper.Comp.Sound, paper);
    }

    /// <summary>
    /// Raises the write attempt. False with a popup if something stops it.
    /// </summary>
    private bool CanFill(Entity<PaperComponent> paper, EntityUid actor)
    {
        var attempt = new PaperWriteAttemptEvent(paper.Owner, actor);
        RaiseLocalEvent(actor, ref attempt);
        RaiseLocalEvent(paper.Owner, ref attempt);
        if (!attempt.Cancelled)
            return true;

        if (attempt.FailReason is { } reason)
            _popup.PopupEntity(reason, actor, actor);

        return false;
    }

    /// <summary>
    /// Position of the Nth tag, or null if the paper changed since the player saw it. Then their view is resent.
    /// </summary>
    private int? FindCurrentTag(Entity<PaperComponent> paper, EntityUid actor, string tag, int version, int index)
    {
        var state = EnsureComp<PaperLanguageStateComponent>(paper);
        SyncContent(paper, state);
        if (version == state.ContentVersion && FindNthTag(paper.Comp.Content, tag, index) is { } position)
            return position;

        _popup.PopupEntity(Loc.GetString("paper-form-changed"), actor, actor);
        SendView(paper, actor, force: true);
        return null;
    }

    /// <summary>
    /// Fills a [signature] or [datetime] in the player's writing language, like a form answer.
    /// </summary>
    public override string? FillTag(Entity<PaperComponent> paper, EntityUid actor, string tag, int index, string text)
    {
        var timer = Stopwatch.GetTimestamp();
        if (!TryStartFill(paper, actor, out var language))
            return null;

        if (FindNthTag(paper.Comp.Content, tag, index) is not { } position)
            return null;

        var filled = FillAt(paper, actor, position, tag.Length, language, CleanAnswer(text));
        Logger.GetSawmill("paper.lang").Info($"FillTag took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return filled;
    }

    /// <summary>
    /// Paper text with the tag at a position replaced by the answer, or null if it doesn't fit.
    /// </summary>
    private string? FillAt(Entity<PaperComponent> paper, EntityUid actor, int position, int tagLength, ProtoId<LanguagePrototype> language, string answer)
    {
        var content = paper.Comp.Content;
        var filled = content[..position] + TagAnswer(language, answer) + content[(position + tagLength)..];
        if (filled.Length <= paper.Comp.ContentSize)
            return filled;

        _popup.PopupEntity(Loc.GetString("paper-full"), actor, actor);
        return null;
    }

    /// <summary>
    /// Starts the save cooldown and gets the language to fill in with. False if on cooldown or the player has no language.
    /// </summary>
    private bool TryStartFill(Entity<PaperComponent> paper, EntityUid actor, out ProtoId<LanguagePrototype> language)
    {
        language = default;
        if (IsOnSaveCooldown(actor))
            return false;

        StartCooldown(paper, actor);
        if (GetWritingLanguage(EnsureComp<PaperLanguageStateComponent>(paper), actor) is not { } writing)
        {
            _popup.PopupEntity(Loc.GetString("paper-form-no-language"), actor, actor);
            return false;
        }

        language = writing;
        return true;
    }

    private static string CleanAnswer(string text) => string.Concat(text.Where(ch => !_answerBannedChars.Contains(ch))).Trim();

    /// <summary>
    /// Position of the Nth tag. Escaped tags aren't counted.
    /// </summary>
    private static int? FindNthTag(string text, string tag, int index)
    {
        if (index < 0)
            return null;

        var count = 0;
        for (var position = text.IndexOf(tag, StringComparison.Ordinal); position != -1; position = text.IndexOf(tag, position + tag.Length, StringComparison.Ordinal))
        {
            // An escaped tag isn't a button
            if ((position == 0 || text[position - 1] != '\\') && count++ == index)
                return position;
        }

        return null;
    }

    /// <summary>
    /// The answer tagged with its language. Saves keep buttons outside sections, so it's always in Common.
    /// </summary>
    private static string TagAnswer(ProtoId<LanguagePrototype> language, string answer) =>
        language == DefaultLanguage ? answer : OpeningTag(language) + answer + ClosingTag;
}
