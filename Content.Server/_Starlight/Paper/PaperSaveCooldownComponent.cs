namespace Content.Server._Starlight.Paper;

/// <summary>
/// When this entity can save paper again, across all papers.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class PaperSaveCooldownComponent : Component
{
    [ViewVariables]
    public TimeSpan NextSave;
}
