using Content.Server.Atmos.Components;
using Robust.Shared.GameObjects;
using Content.Shared.Atmos;

namespace Content.Server.Atmos.EntitySystems;

public partial class AtmosphereSystem
{
    public bool TryGetMapTemperature(EntityUid uid, out float temperature)
    {
        if (!TryComp<MapAtmosphereComponent>(uid, out var component))
        {
            temperature = 0f;
            return false;
        }

        temperature = component.Mixture.Temperature;
        return true;
    }

    public void SetMapTemperature(EntityUid uid, float temperature)
    {
        if (!TryComp<MapAtmosphereComponent>(uid, out var component))
            return;

        var mixture = new GasMixture(component.Mixture);
        mixture.Temperature = temperature;
        SetMapGasMixture(uid, mixture);
    }
}
