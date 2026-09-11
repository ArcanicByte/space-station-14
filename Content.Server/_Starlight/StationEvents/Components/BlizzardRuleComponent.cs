using Content.Server._Starlight.StationEvents.Events;
using Robust.Shared.Map;
using Content.Shared.Atmos;

namespace Content.Server._Starlight.StationEvents.Components;

/// <summary>
///     Blizzard event specific configuration
/// </summary>
[RegisterComponent, Access(typeof(BlizzardRule))]
public sealed partial class BlizzardRuleComponent : Component
{
    [DataField]
    public float DoorToggleChancePerSecond;

    [DataField]
    public float LightBreakChancePerSecond;

    public MapId Map; // where old weather was originally

    public EntityUid MapEntity;

    public GasMixture OriginalMixture = default!; // atmosphere before event

    public float OriginalTemperature;

    public TimeSpan Delay = TimeSpan.Zero;

}
