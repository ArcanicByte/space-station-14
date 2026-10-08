using System.Linq;
using Content.Shared._Starlight.Language;
using Content.Shared._Starlight.Language.Events;
using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.Shared.Paper.PaperComponent;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Content.Server._Starlight.Paper;

public sealed partial class PaperLanguageSystem : SharedPaperLanguageSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    [SubscribeLocalEvent]
    private void OnLanguagesUpdate(Entity<UserInterfaceUserComponent> ent, ref LanguagesUpdateEvent args)
    {
        var timer = Stopwatch.GetTimestamp();
        foreach (var (uiEntity, keys) in ent.Comp.OpenInterfaces)
        {
            if (keys.Contains(PaperUiKey.Key) && TryComp<PaperComponent>(uiEntity, out var paper))
                SendView((uiEntity, paper), ent.Owner, force: true);
        }

        Logger.GetSawmill("paper.lang").Info($"OnLanguagesUpdate took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    // Needed with the client's view request, either one can arrive too early and get dropped
    [SubscribeLocalEvent]
    private void OnUIOpened(Entity<PaperComponent> paper, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey.Equals(PaperUiKey.Key))
            SendView(paper, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnViewRequest(Entity<PaperComponent> paper, ref PaperViewRequestMessage args) => SendView(paper, args.Actor, force: true);

    [SubscribeLocalEvent]
    private void OnSelectLanguage(Entity<PaperComponent> paper, ref PaperSelectLanguageMessage args)
    {
        var state = EnsureComp<PaperLanguageStateComponent>(paper);
        GetViewer(state, args.Actor).WritingLanguage = args.Language;
    }

    private static PaperViewerState GetViewer(PaperLanguageStateComponent state, EntityUid actor)
    {
        if (!state.Viewers.TryGetValue(actor, out var viewer))
            state.Viewers[actor] = viewer = new PaperViewerState();

        return viewer;
    }

    public override void UpdateViews(Entity<PaperComponent> paper)
    {
        var timer = Stopwatch.GetTimestamp();
        UpdateHasWriting(paper);

        foreach (var actor in _ui.GetActors(paper.Owner, PaperUiKey.Key))
            SendView(paper, actor);

        Logger.GetSawmill("paper.lang").Info($"UpdateViews took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    /// <summary>
    /// Sends a player their copy of the paper with unreadable text locked or scrambled, unless nothing changed for them. Given text replaces the paper's, for a rejected save.
    /// </summary>
    private void SendView(Entity<PaperComponent> paper, EntityUid actor, string? text = null, bool force = false)
    {
        var timer = Stopwatch.GetTimestamp();
        var state = EnsureComp<PaperLanguageStateComponent>(paper);
        var viewer = GetViewer(state, actor);
        var mode = paper.Comp.Writers.Contains(actor) ? PaperAction.Write : PaperAction.Read;
        var languages = GetWritableLanguages(actor);
        var view = new PaperSentView(paper.Comp.Content,
            paper.Comp.StampedBy,
            paper.Comp.StampedBy.Count,
            mode,
            languages,
            GetDefaultWritingLanguage(actor, languages));

        // Skip unchanged views, like the repeat sends after a save or signature
        if (text == null && !force && viewer.SentView is { } last && last.Matches(view))
            return;

        string shown;
        int? hiddenLength = null;
        if (text == null)
        {
            shown = GetUiView(paper, state, actor, mode == PaperAction.Write);
            hiddenLength = paper.Comp.Content.Length - shown.Length;
            viewer.SentView = view;
            viewer.SentExtra = Math.Max(0, shown.Length - paper.Comp.Content.Length);

            // The editor keeps its text until a save, later views don't change it
            if (mode == PaperAction.Write)
                viewer.EditVersion ??= state.ContentVersion;
            else
                viewer.EditVersion = null;
        }
        else
        {
            // Text given back isn't the paper's, the next view has to be resent
            shown = text;
            viewer.SentView = null;
        }

        // Hidden length is mostly markup in locked text, which the editor can't see
        var message = new PaperViewMessage(
            text == null ? state.ContentVersion : -1,
            new PaperBoundUserInterfaceState(shown, paper.Comp.StampedBy, mode),
            languages,
            view.DefaultLanguage,
            hiddenLength);

        _ui.ServerSendUiMessage(paper.Owner, PaperUiKey.Key, message, actor);

        Logger.GetSawmill("paper.lang").Info($"SendView took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    public override void ClearViewer(Entity<PaperComponent> paper, EntityUid viewer)
    {
        if (TryComp<PaperLanguageStateComponent>(paper, out var state))
            state.Viewers.Remove(viewer);
    }

    /// <summary>
    /// Checks the cooldown, writing language and size, then merges the save. Null if rejected, after sending the player their view.
    /// </summary>
    public override string? TrySave(Entity<PaperComponent> paper, EntityUid actor, string text)
    {
        var timer = Stopwatch.GetTimestamp();

        // Readers fill things in through their own messages
        if (!paper.Comp.Writers.Contains(actor))
            return null;

        var state = EnsureComp<PaperLanguageStateComponent>(paper);

        // Without a language, saving just closes the editor
        if (GetWritingLanguage(state, actor) is not { } writing)
        {
            _popup.PopupEntity(Loc.GetString("paper-language-cannot-write"), actor, actor);
            paper.Comp.Writers.Remove(actor);
            SendView(paper, actor);
            return null;
        }

        // Per player, not per paper. Switching papers doesn't skip it
        if (!TryStartCooldown(paper, actor))
        {
            SendView(paper, actor, text);
            return null;
        }

        var viewer = state.Viewers.GetValueOrDefault(actor);

        // Only the ids and scrambles the server sent can go over the limit
        if (text.Length > paper.Comp.ContentSize + (viewer?.SentExtra ?? 0))
        {
            RejectFull(paper, actor, text);
            return null;
        }

        // Locked text comes back in full, which can be longer than what the player sent. Shrinking is still fine
        var result = MergeEdit(paper, state, actor, writing, text);
        if (result.Content.Length > paper.Comp.ContentSize && result.Content.Length > paper.Comp.Content.Length)
        {
            RejectFull(paper, actor, text);
            return null;
        }

        viewer?.HiddenSections.Clear();
        ShowSavePopup(actor, result, writing, viewer?.EditVersion != state.ContentVersion);

        Logger.GetSawmill("paper.lang").Info($"TrySave took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
        return result.Content;
    }

    private void RejectFull(Entity<PaperComponent> paper, EntityUid actor, string text)
    {
        _popup.PopupEntity(Loc.GetString("paper-full"), actor, actor);
        SendView(paper, actor, text);
    }

    /// <summary>
    /// Starts the save cooldown. False with a popup if it's still running.
    /// </summary>
    private bool TryStartCooldown(Entity<PaperComponent> paper, EntityUid actor)
    {
        if (TryComp<PaperSaveCooldownComponent>(actor, out var cooldown) && _timing.CurTime < cooldown.NextSave)
        {
            _popup.PopupEntity(Loc.GetString("paper-save-cooldown"), actor, actor);
            return false;
        }

        EnsureComp<PaperSaveCooldownComponent>(actor).NextSave = _timing.CurTime + paper.Comp.SaveDelay;
        return true;
    }

    /// <summary>
    /// One popup per save, for the worst thing that happened to the text.
    /// </summary>
    private void ShowSavePopup(EntityUid actor, PaperMergeResult result, ProtoId<LanguagePrototype> writing, bool stale)
    {
        // Saves overwrite the whole paper, so the editor's text wins over anything written since
        if (stale)
            _popup.PopupEntity(Loc.GetString("paper-language-overwritten"), actor, actor);
        else if (result.RemovedSections > 0)
            _popup.PopupEntity(Loc.GetString("paper-language-removed", ("count", result.RemovedSections)), actor, actor);
        else if (result.ConvertedFrom.Count > 0)
        {
            var names = string.Join(", ", result.ConvertedFrom.Select(GetLanguageName));
            _popup.PopupEntity(Loc.GetString("paper-language-converted",
                    ("languages", names),
                    ("language", GetLanguageName(writing))),
                actor,
                actor);
        }
    }

    private string GetLanguageName(ProtoId<LanguagePrototype> language) => _prototype.TryIndex(language, out var proto) ? proto.Name : language.Id;

    /// <summary>
    /// The language picked in the paper UI if the player can still write it, otherwise their default.
    /// </summary>
    private ProtoId<LanguagePrototype>? GetWritingLanguage(PaperLanguageStateComponent state, EntityUid actor)
    {
        if (state.Viewers.GetValueOrDefault(actor)?.WritingLanguage is { } selected && CanWrite(actor, selected))
            return selected;

        return GetDefaultWritingLanguage(actor);
    }

    private void UpdateHasWriting(Entity<PaperComponent> paper)
    {
        var hasWriting = !string.IsNullOrWhiteSpace(paper.Comp.Content);
        if (paper.Comp.HasWriting == hasWriting)
            return;

        paper.Comp.HasWriting = hasWriting;
        Dirty(paper);
    }
}
