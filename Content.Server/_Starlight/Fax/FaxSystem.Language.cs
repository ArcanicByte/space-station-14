using Content.Server._Starlight.Paper;

// ReSharper disable CheckNamespace
namespace Content.Server.Fax;

public sealed partial class FaxSystem
{
    [Dependency] private PaperLanguageSystem _paperLanguage = default!;
}
