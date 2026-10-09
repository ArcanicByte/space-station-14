using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Client._Starlight.Paper;

public sealed partial class PaperLanguageSystem : SharedPaperLanguageSystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    // Text comes from the server, only predicted stamps and pen signatures are redrawn here
    public override void UpdateViews(Entity<PaperComponent> paper)
    {
        if (_ui.TryGetOpenUi(paper.Owner, PaperUiKey.Key, out var bui))
            bui.Update();
    }

    // Replays only, text written while the paper is open
    [SubscribeLocalEvent]
    private void OnReplayContentState(Entity<PaperReplayContentComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<PaperComponent>(ent, out var paper))
            UpdateViews((ent, paper));
    }
}
