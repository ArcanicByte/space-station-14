// ReSharper disable CheckNamespace
using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;

namespace Content.Shared.Labels.EntitySystems;

public sealed partial class LabelSystem
{
    [Dependency] private SharedPaperLanguageSystem _paperLanguage = default!;

    private string GetReadableLabelText(Entity<PaperComponent> paper, EntityUid examiner) => _paperLanguage.GetStyledView(paper, examiner);
}
