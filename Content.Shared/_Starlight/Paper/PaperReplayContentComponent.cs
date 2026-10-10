using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Paper;

/// <summary>
/// The paper's full text for replays. Players never get it, they get their own view instead.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), UnsavedComponent]
public sealed partial class PaperReplayContentComponent : Component
{
    public override bool SessionSpecific => true;

    [ViewVariables, AutoNetworkedField]
    public string Content = string.Empty;
}
