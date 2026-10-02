// ReSharper disable CheckNamespace
using Content.Shared._Starlight.Language;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Paper;

public sealed partial class PaperComponent
{
    /// <summary>
    /// Whether anything is written on the paper. Networked since clients don't get the text.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool HasWriting;

    /// <summary>
    /// Players editing the paper.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Writers = [];

    [DataField]
    public TimeSpan ReopenDelay = TimeSpan.FromSeconds(0.1);

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> ReopenTimes = [];

    [DataField]
    public TimeSpan SaveDelay = TimeSpan.FromSeconds(0.1);

    /// <summary>
    /// A player's own view of the paper.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class PaperViewMessage(
        PaperBoundUserInterfaceState state,
        List<ProtoId<LanguagePrototype>> writableLanguages,
        ProtoId<LanguagePrototype>? defaultLanguage) : BoundUserInterfaceMessage
    {
        public readonly PaperBoundUserInterfaceState State = state;
        public readonly List<ProtoId<LanguagePrototype>> WritableLanguages = writableLanguages;
        public readonly ProtoId<LanguagePrototype>? DefaultLanguage = defaultLanguage;
    }

    /// <summary>
    /// Asks the server for this player's view when the paper UI opens.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class PaperViewRequestMessage : BoundUserInterfaceMessage;

    /// <summary>
    /// The language a player picked to write in.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class PaperSelectLanguageMessage(ProtoId<LanguagePrototype> language) : BoundUserInterfaceMessage
    {
        public readonly ProtoId<LanguagePrototype> Language = language;
    }
}
