// ReSharper disable CheckNamespace
using Content.Shared._Starlight.Paper;
using Content.Shared.UserInterface;
using Robust.Shared.Timing;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Shared.Paper;

public sealed partial class PaperSystem
{
    [Dependency] private SharedPaperLanguageSystem _paperLanguage = default!;
    [Dependency] private IGameTiming _timing = default!;

    private void UpdateLanguageUserInterface(Entity<PaperComponent> entity) => _paperLanguage.UpdateViews(entity);

    private bool CanSave(Entity<PaperComponent> entity, EntityUid actor, string text) => _paperLanguage.CanSave(entity, actor, text);

    private string MergeLanguageEdit(Entity<PaperComponent> entity, EntityUid actor, string text) => _paperLanguage.SaveEdit(entity, actor, text);

    [SubscribeLocalEvent]
    private void OnUIClosed(Entity<PaperComponent> entity, ref BoundUIClosedEvent args)
    {
        if (!args.UiKey.Equals(PaperUiKey.Key))
            return;

        entity.Comp.Writers.Remove(args.Actor);

        // Closes from server state shouldn't restart the delay.
        if (!_timing.ApplyingState)
        {
            RemoveExpiredReopenTimes(entity.Comp);
            entity.Comp.ReopenTimes[args.Actor] = _timing.CurTime + entity.Comp.ReopenDelay;
        }

        _paperLanguage.ClearViewer(entity, args.Actor);
    }

    private void RemoveExpiredReopenTimes(PaperComponent paper)
    {
        var now = _timing.CurTime;
        foreach (var (user, reopenTime) in paper.ReopenTimes)
        {
            if (reopenTime <= now)
                paper.ReopenTimes.Remove(user);
        }
    }

    [SubscribeLocalEvent]
    private void OnOpenAttempt(Entity<PaperComponent> entity, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!CanOpenAgain(entity, args.User))
            args.Cancel();
    }

    private bool CanOpenAgain(Entity<PaperComponent> entity, EntityUid user)
    {
        if (!entity.Comp.ReopenTimes.TryGetValue(user, out var reopenTime))
            return true;

        if (_timing.CurTime < reopenTime)
            return false;

        entity.Comp.ReopenTimes.Remove(user);
        return true;
    }
}
