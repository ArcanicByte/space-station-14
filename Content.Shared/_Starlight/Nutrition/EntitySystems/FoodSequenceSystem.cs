using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Nutrition.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Shared.Nutrition.EntitySystems;

public sealed partial class FoodSequenceSystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SharedStorageSystem _storage = default!;

    /// <summary>
    /// Switches the item to <see cref="FoodSequenceStartPointComponent.FilledSize"/> once it has any layers.
    /// </summary>
    private void UpdateItemSize(Entity<FoodSequenceStartPointComponent> start, EntityUid? entity)
    {
        if (start.Comp.FilledSize is not { } size
            || !TryComp<ItemComponent>(start, out var item)
            || item.Size == size)
            return;

        // Storage doesn't eject items that grow into their neighbours, so take it out,
        // grow it, and put it back wherever it fits. Otherwise it goes to the entity's hands or the floor.
        if (!_storage.TryGetStorageLocation((start, item), out var container, out var storage, out _))
        {
            _item.SetSize(start, size, item);
            return;
        }

        _container.Remove(start.Owner, container, force: true);
        _item.SetSize(start, size, item);

        if (_storage.Insert(container.Owner, start, out _, entity, storage, playSound: false))
            return;

        if (entity != null)
            _hands.TryPickupAnyHand(entity.Value, start);
    }
}
