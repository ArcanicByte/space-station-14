using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.Light.EntitySystems;
using Content.Server._Starlight.StationEvents.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Light.Components;
using Content.Server.Weather;
using Content.Server.StationEvents.Events;
using Content.Server.StationEvents.Components;
using Content.Shared.Station.Components;
using Content.Server.GameTicking;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Components;
using Content.Server.Maps;

namespace Content.Server._Starlight.StationEvents.Events;

public sealed partial class BlizzardRule : StationEventSystem<BlizzardRuleComponent>
{

    [Dependency] private WeatherSystem _weather = default!;
    [Dependency] private PoweredLightSystem _poweredLight = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IGameMapManager _gameMapManager = default!;

    private float _effectTimer = 0;
    private float _startingTemperature;
    private EntityUid _mapEntity;

    public override void Initialize()
    {
        base.Initialize();
    }

    protected override void Started(EntityUid uid, BlizzardRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        EntityUid? chosenStation = null;
        if (!TryComp<StationEventComponent>(uid, out var stationEvent)) return;
        chosenStation = stationEvent.TargetStation;
        if (chosenStation is null)
            if (!TryGetRandomStation(out chosenStation))
                return;

        if (!TryComp<StationDataComponent>(chosenStation, out var stationData))
            return;

        var grid = StationSystem.GetLargestGrid((chosenStation.Value, stationData));

        if (grid is null)
            return;

        comp.Map = Transform(grid.Value).MapID;

        _mapEntity = _map.GetMap(comp.Map);

        if (!_atmosphere.TryGetMapTemperature(_mapEntity, out comp.OriginalTemperature))
            return;

        _startingTemperature = comp.OriginalTemperature;

        for (var i = 1; i <= 6; i++)
        {
            var seconds = i * 5;

            Timer.Spawn(seconds, () =>
            {
                var progress = seconds / 30f;

                var temperature = _startingTemperature +
                                  (100f - _startingTemperature) * progress;

                _atmosphere.SetMapTemperature(_mapEntity, temperature);
            });
        }

        _weather.TryRemoveWeather(comp.Map, "WeatherSnowfallLobster");

        Timer.Spawn(comp.Delay, () => _weather.TrySetWeather(comp.Map, "WeatherSnowfallHeavy", out _));

    }

    protected override void ActiveTick(EntityUid uid, BlizzardRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, comp, gameRule, frameTime);

        _effectTimer -= frameTime;

        if (_effectTimer < 0)
        {
            _effectTimer += 1;
            var lightQuery = EntityQueryEnumerator<PoweredLightComponent>();
            while (lightQuery.MoveNext(out var lightEnt, out var light))
            {
                if (RobustRandom.Prob(comp.LightBreakChancePerSecond))
                    _poweredLight.TryDestroyBulb(lightEnt, light);
            }
            var airlockQuery = EntityQueryEnumerator<AirlockComponent, DoorComponent>();
            while (airlockQuery.MoveNext(out var airlockEnt, out var airlock, out var door))
            {
                if (airlock.AutoClose && RobustRandom.Prob(comp.DoorToggleChancePerSecond))
                    _door.TryToggleDoor(airlockEnt, door);
            }
        }
    }

    protected override void Ended(EntityUid uid, BlizzardRuleComponent comp, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, comp, gameRule, args);

        for (var i = 1; i <= 6; i++)
        {
            var seconds = i * 5;
            Timer.Spawn(seconds, () =>
            {
                var progress = seconds / 30f;
                var temperature = 100f +
                                  (_startingTemperature - 100f) * progress;

                _atmosphere.SetMapTemperature(_mapEntity, temperature);
            });
        }

        var map = _gameMapManager.GetSelectedMap();

        if (map?.ID == "StarlightNovoLobster")
        {
            _weather.TrySetWeather(comp.Map, "WeatherSnowfallLobster", out _);
        }

        _weather.TryRemoveWeather(comp.Map, "WeatherSnowfallHeavy");

    }

}
