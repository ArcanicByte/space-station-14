// ReSharper disable CheckNamespace
using Content.Shared._Starlight.Language;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Paper;

public sealed partial class PaperComponent
{
    [ViewVariables, AutoNetworkedField]
    public bool HasWriting;

    [ViewVariables]
    public HashSet<EntityUid> Writers = [];

    [DataField]
    public TimeSpan ReopenDelay = TimeSpan.FromSeconds(0.25);

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> ReopenTimes = [];

    [DataField]
    public TimeSpan SaveDelay = TimeSpan.FromSeconds(0.5);

    [Serializable, NetSerializable]
    public sealed class PaperViewMessage(
        int contentVersion,
        PaperBoundUserInterfaceState state,
        List<ProtoId<LanguagePrototype>> writableLanguages,
        ProtoId<LanguagePrototype>? defaultLanguage,
        int? hiddenLength) : BoundUserInterfaceMessage
    {
        /// <summary>
        /// Goes up each time the paper text changes. Form fills send it back. -1 when the text isn't the paper's.
        /// </summary>
        public readonly int ContentVersion = contentVersion;

        public readonly PaperBoundUserInterfaceState State = state;
        public readonly List<ProtoId<LanguagePrototype>> WritableLanguages = writableLanguages;
        public readonly ProtoId<LanguagePrototype>? DefaultLanguage = defaultLanguage;

        /// <summary>
        /// Paper length minus this view's length. Null when the text isn't the paper's.
        /// </summary>
        public readonly int? HiddenLength = hiddenLength;
    }

    /// <summary>
    /// Fills in a [form], written in the player's selected language.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class PaperFormFillMessage(int contentVersion, int index, string text) : BoundUserInterfaceMessage
    {
        /// <summary>
        /// Version of the text the form was clicked in. Rejected if the paper changed since.
        /// </summary>
        public readonly int ContentVersion = contentVersion;
        public readonly int Index = index;
        public readonly string Text = text;
    }

    /// <summary>
    /// Ticks a [check]. Marks have no language, anyone can tick one.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class PaperCheckFillMessage(int contentVersion, int index, char mark) : BoundUserInterfaceMessage
    {
        public readonly int ContentVersion = contentVersion;
        public readonly int Index = index;
        public readonly char Mark = mark;
    }

    [Serializable, NetSerializable]
    public sealed class PaperViewRequestMessage : BoundUserInterfaceMessage;

    [Serializable, NetSerializable]
    public sealed class PaperSelectLanguageMessage(ProtoId<LanguagePrototype> language) : BoundUserInterfaceMessage
    {
        public readonly ProtoId<LanguagePrototype> Language = language;
    }
}
