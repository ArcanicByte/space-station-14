using Content.Shared.Item;
using Robust.Shared.Prototypes;

namespace Content.Shared.Nutrition.Components;

public sealed partial class FoodSequenceStartPointComponent
{
    /// <summary>
    /// Item size to switch to once the first layer is added, so the start point (e.g. a bottom bun) can begin small.
    /// </summary>
    [DataField]
    public ProtoId<ItemSizePrototype>? FilledSize;
}
