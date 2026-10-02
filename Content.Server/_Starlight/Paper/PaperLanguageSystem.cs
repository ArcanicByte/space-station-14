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

    public override void UpdateViews(Entity<PaperComponent> paper)
    {
        UpdateHasWriting(paper);

        foreach (var actor in _ui.GetActors(paper.Owner, PaperUiKey.Key))
            SendView(paper, actor);
    }

    /// <param name="text">Text to show instead of the paper's, like text given back after a failed save.</param>
    /// <param name="force">Send even if nothing changed.</param>
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

        // Skip views that haven't changed, e.g. when someone else opens the paper.
        if (text == null && !force && state.SentViews.TryGetValue(actor, out var last) && last.Matches(view))
            return;

        // Text given back isn't the paper's, so resend the next view.
        if (text == null)
            state.SentViews[actor] = view;
        else
            state.SentViews.Remove(actor);

        var uiState = new PaperBoundUserInterfaceState(
            text ?? GetUiView(paper, state, actor, mode == PaperAction.Write),
            paper.Comp.StampedBy,
            mode);

        var message = new PaperViewMessage(uiState, languages, view.DefaultLanguage);
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
        state.HiddenSections.Remove(viewer);
    }

    /// <summary>
    /// Players have to wait between saves, and need a language to write in.
    /// </summary>
    public override bool CanSave(Entity<PaperComponent> paper, EntityUid actor, string text)
    {
        // Without a language, saving from the editor just closes it. Forms in the read view still work since that isn't writing.
        if (paper.Comp.Writers.Contains(actor) && GetWritableLanguages(actor).Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("paper-language-cannot-write"), actor, actor);
            paper.Comp.Writers.Remove(actor);
            SendView(paper, actor);
            return false;
        }

        // Per entity, not per paper, so switching papers doesn't get around it.
        if (!TryComp<PaperSaveCooldownComponent>(actor, out var cooldown) || _timing.CurTime >= cooldown.NextSave)
            return true;

        _popup.PopupEntity(Loc.GetString("paper-save-cooldown"), actor, actor);

        // The editor clears itself when saving, so give the text back.
        if (paper.Comp.Writers.Contains(actor))
            SendView(paper, actor, text);

        return false;
    }

    public override string SaveEdit(Entity<PaperComponent> paper, EntityUid actor, string text)
    {
        var state = EnsureComp<PaperLanguageStateComponent>(paper);

        ProtoId<LanguagePrototype>? language = null;
        if (state.WritingLanguages.TryGetValue(actor, out var selected))
            language = selected;

        var result = MergeEdit(paper, state, actor, text, language);
        EnsureComp<PaperSaveCooldownComponent>(actor).NextSave = _timing.CurTime + paper.Comp.SaveDelay;

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

    [SubscribeLocalEvent]
    private void OnPaperStartup(Entity<PaperComponent> paper, ref ComponentStartup args) => UpdateHasWriting(paper);

    private void UpdateHasWriting(Entity<PaperComponent> paper)
    {
        var hasWriting = !string.IsNullOrWhiteSpace(paper.Comp.Content);
        if (paper.Comp.HasWriting == hasWriting)
            return;

        paper.Comp.HasWriting = hasWriting;
        Dirty(paper);
    }
}
