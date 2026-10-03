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
    public TimeSpan SaveDelay = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// A player's own view of the paper.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class PaperViewMessage(
        int view,
        PaperBoundUserInterfaceState state,
        List<ProtoId<LanguagePrototype>> writableLanguages,
        ProtoId<LanguagePrototype>? defaultLanguage,
        int? hiddenLength) : BoundUserInterfaceMessage
    {
        /// <summary>
        /// Counts up with each view sent to this player, so form fills can say which one they saw.
        /// </summary>
        public readonly int View = view;

        public readonly PaperBoundUserInterfaceState State = state;
        public readonly List<ProtoId<LanguagePrototype>> WritableLanguages = writableLanguages;
        public readonly ProtoId<LanguagePrototype>? DefaultLanguage = defaultLanguage;

        /// <summary>
        /// Paper length minus this view's length, so the editor can count text it can't see. Null when the text isn't the paper's.
        /// </summary>
        public readonly int? HiddenLength = hiddenLength;
    }

    /// <summary>
    /// Fills in a [form], written in the player's selected language.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class PaperFormFillMessage(int view, int index, string text) : BoundUserInterfaceMessage
    {
        /// <summary>
        /// The view the form was clicked in.
        /// </summary>
        public readonly int View = view;

        public readonly int Index = index;
        public readonly string Text = text;
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
