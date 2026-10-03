using System.Linq;
using Content.Shared._Starlight.Language;
using Content.Shared._Starlight.Language.Events;
using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Server._Starlight.Paper;

public sealed partial class PaperLanguageSystem : SharedPaperLanguageSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    // The merge done by CanSave, used by SaveEdit right after
    private (EntityUid Paper, EntityUid Actor, string Text, PaperMergeResult Result)? _pendingMerge;

    public override void UpdateViews(Entity<PaperComponent> paper)
    {
        UpdateHasWriting(paper);

        foreach (var actor in _ui.GetActors(paper.Owner, PaperUiKey.Key))
            SendView(paper, actor);
    }

    /// <param name="text">Text to show instead of the paper's, like text given back after a failed save.</param>
    private void SendView(Entity<PaperComponent> paper, EntityUid actor, string? text = null, bool force = false)
    {
        var state = EnsureComp<PaperLanguageStateComponent>(paper);
        var mode = paper.Comp.Writers.Contains(actor) ? PaperAction.Write : PaperAction.Read;
        var languages = GetWritableLanguages(actor);
        var view = new PaperSentView(paper.Comp.Content,
            paper.Comp.StampedBy,
            paper.Comp.StampedBy.Count,
            mode,
            languages,
            GetDefaultWritingLanguage(actor, languages));

        // Skip unchanged views, like when someone else opens the paper
        if (text == null && !force && state.SentViews.TryGetValue(actor, out var last) && last.Matches(view))
            return;

        // Text given back isn't the paper's, so resend the next view
        if (text == null)
            state.SentViews[actor] = view;
        else
            state.SentViews.Remove(actor);

        var paperText = text == null ? paper.Comp.Content : null;
        var shown = text ?? GetUiView(paper, state, actor, mode == PaperAction.Write);

        // Hidden length is mostly markup in locked text, which the editor can't see
        var message = new PaperViewMessage(
            AddToHistory(state, actor, paperText),
            new PaperBoundUserInterfaceState(shown, paper.Comp.StampedBy, mode),
            languages,
            view.DefaultLanguage,
            paperText?.Length - shown.Length);
        _ui.ServerSendUiMessage(paper.Owner, PaperUiKey.Key, message, actor);
    }

    [SubscribeLocalEvent]
    private void OnLanguagesUpdate(Entity<UserInterfaceUserComponent> ent, ref LanguagesUpdateEvent args)
    {
        foreach (var (uiEntity, keys) in ent.Comp.OpenInterfaces)
        {
            if (keys.Contains(PaperUiKey.Key) && TryComp<PaperComponent>(uiEntity, out var paper))
                SendView((uiEntity, paper), ent.Owner, force: true);
        }
    }

    [SubscribeLocalEvent]
    private void OnUIOpened(Entity<PaperComponent> paper, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey.Equals(PaperUiKey.Key))
            SendView(paper, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnViewRequest(Entity<PaperComponent> paper, ref PaperViewRequestMessage args) => SendView(paper, args.Actor, force: true);

    [SubscribeLocalEvent]
    private void OnSelectLanguage(Entity<PaperComponent> paper, ref PaperSelectLanguageMessage args) =>
        EnsureComp<PaperLanguageStateComponent>(paper).WritingLanguages[args.Actor] = args.Language;

    public override void ClearViewer(Entity<PaperComponent> paper, EntityUid viewer)
    {
        if (!TryComp<PaperLanguageStateComponent>(paper, out var state))
            return;

        state.WritingLanguages.Remove(viewer);
        state.SentViews.Remove(viewer);
        state.ViewHistory.Remove(viewer);
        state.HiddenSections.Remove(viewer);
        state.NextHiddenIds.Remove(viewer);
    }

    /// <summary>
    /// Players have to wait between saves, and need a language to write in.
    /// </summary>
    public override bool CanSave(Entity<PaperComponent> paper, EntityUid actor, string text)
    {
        // Without a language, saving just closes the editor. Forms in read mode still work
        if (paper.Comp.Writers.Contains(actor) && GetWritableLanguages(actor).Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("paper-language-cannot-write"), actor, actor);
            paper.Comp.Writers.Remove(actor);
            SendView(paper, actor);
            return false;
        }

        // Per entity, not per paper, so switching papers doesn't skip it
        if (TryComp<PaperSaveCooldownComponent>(actor, out var cooldown) && _timing.CurTime < cooldown.NextSave)
        {
            _popup.PopupEntity(Loc.GetString("paper-save-cooldown"), actor, actor);
            GiveTextBack(paper, actor, text);
            return false;
        }

        // Upstream skips text over the limit itself, so don't merge it
        if (text.Length > paper.Comp.ContentSize)
            return true;

        // Locked text comes back in full, which can be longer than what the player sent. Shrinking is still fine
        var result = MergeEdit(paper, EnsureComp<PaperLanguageStateComponent>(paper), actor, text);
        if (result.Content.Length > paper.Comp.ContentSize && result.Content.Length > paper.Comp.Content.Length)
        {
            StartCooldown(paper, actor);
            _popup.PopupEntity(Loc.GetString("paper-full"), actor, actor);
            GiveTextBack(paper, actor, text);
            return false;
        }

        _pendingMerge = (paper.Owner, actor, text, result);
        return true;
    }

    private void StartCooldown(Entity<PaperComponent> paper, EntityUid actor) =>
        EnsureComp<PaperSaveCooldownComponent>(actor).NextSave = _timing.CurTime + paper.Comp.SaveDelay;

    /// <summary>
    /// The editor clears itself on save, so a rejected save gives the text back.
    /// </summary>
    private void GiveTextBack(Entity<PaperComponent> paper, EntityUid actor, string text)
    {
        if (paper.Comp.Writers.Contains(actor))
            SendView(paper, actor, text);
    }

    public override string SaveEdit(Entity<PaperComponent> paper, EntityUid actor, string text)
    {
        var state = EnsureComp<PaperLanguageStateComponent>(paper);

        // Reuse the merge from CanSave
        var result = _pendingMerge is { } pending && pending.Paper == paper.Owner && pending.Actor == actor && ReferenceEquals(pending.Text, text)
            ? pending.Result
            : MergeEdit(paper, state, actor, text);

        _pendingMerge = null;
        state.HiddenSections.Remove(actor);
        StartCooldown(paper, actor);

        if (result.ConvertedFrom.Count > 0 && result.ConvertedTo is { } convertedTo)
        {
            var names = string.Join(", ", result.ConvertedFrom.Select(GetLanguageName));
            _popup.PopupEntity(Loc.GetString("paper-language-converted",
                    ("languages", names),
                    ("language", GetLanguageName(convertedTo))),
                actor,
                actor);
        }

        if (result.DroppedText)
            _popup.PopupEntity(Loc.GetString("paper-language-dropped"), actor, actor);

        if (result.ErasedSections > 0)
            _popup.PopupEntity(Loc.GetString("paper-language-erased", ("count", result.ErasedSections)), actor, actor);

        if (result.RemovedSections > 0)
            _popup.PopupEntity(Loc.GetString("paper-language-removed", ("count", result.RemovedSections)), actor, actor);

        return result.Content;
    }

    private string GetLanguageName(ProtoId<LanguagePrototype> language) => _prototype.TryIndex(language, out var proto) ? proto.Name : language.Id;

    private void UpdateHasWriting(Entity<PaperComponent> paper)
    {
        var hasWriting = !string.IsNullOrWhiteSpace(paper.Comp.Content);
        if (paper.Comp.HasWriting == hasWriting)
            return;

        paper.Comp.HasWriting = hasWriting;
        Dirty(paper);
    }
}
