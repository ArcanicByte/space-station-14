using Content.Shared._Starlight.Devil;
using Content.Shared.Examine;
using Content.Shared.Paper;

namespace Content.Client._Starlight.Devil;

public sealed class DevilSystem : SharedDevilSystem
{
    // Clients don't have the contract text, so the server handles this
    protected override void OnExamineEvent(EntityUid uid, InfernalContractComponent contractComp, ref ExaminedEvent args)
    {
        if (contractComp.Completed)
            base.OnExamineEvent(uid, contractComp, ref args);
    }

    protected override void OnSignedEvent(EntityUid uid, InfernalContractComponent contractComp, ref PaperSignedEvent args)
    {
        if (!contractComp.Completed)
            args.Cancelled = true;
    }
}
