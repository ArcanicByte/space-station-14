using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.StationEvents.Components;

public sealed partial class StationEventComponent
{
    /// <summary>
    /// Maps this event is allowed to run on. If null, the event can run on any map.
    /// </summary>
    [DataField]
    public List<ProtoId<GameMapPrototype>>? Maps;
}
