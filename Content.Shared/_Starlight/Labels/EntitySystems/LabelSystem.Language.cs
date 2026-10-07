// ReSharper disable CheckNamespace
using Content.Shared._Starlight.Paper;

namespace Content.Shared.Labels.EntitySystems;

public sealed partial class LabelSystem
{
    [Dependency] private SharedPaperLanguageSystem _paperLanguage = default!;
}
