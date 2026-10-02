using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Client._Starlight.Paper;

public sealed partial class PaperLanguageSystem : SharedPaperLanguageSystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    // The server sends the text, so only predicted stamps are shown straight away.
    public override void UpdateViews(Entity<PaperComponent> paper)
    {
        if (_ui.TryGetOpenUi(paper.Owner, PaperUiKey.Key, out var bui))
            bui.Update();
    }
}
