using Content.Server.Maps;
using Content.Server.StationEvents.Components;

namespace Content.Server.StationEvents;

public sealed partial class EventManagerSystem
{
    [Dependency] private IGameMapManager _gameMapManager = default!;

    private bool IsMapAllowed(StationEventComponent stationEvent)
    {
        var currentMap = _gameMapManager.GetSelectedMap();

        if (stationEvent.Maps != null &&
            (currentMap == null || !stationEvent.Maps.Contains(currentMap.ID)))
        {
            return false;
        }

        return true;
    }
}
