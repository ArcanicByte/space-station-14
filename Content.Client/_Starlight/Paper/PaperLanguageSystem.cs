using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Client._Starlight.Paper;

public sealed partial class PaperLanguageSystem : SharedPaperLanguageSystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    // Text comes from the server, so just redraw predicted stamps
    public override void UpdateViews(Entity<PaperComponent> paper)
    {
        if (_ui.TryGetOpenUi(paper.Owner, PaperUiKey.Key, out var bui))
            bui.Update();
    }
}
