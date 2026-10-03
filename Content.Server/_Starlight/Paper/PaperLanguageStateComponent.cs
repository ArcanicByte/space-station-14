using System.Linq;
using Content.Shared._Starlight.Language;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;
using static Content.Shared._Starlight.Paper.SharedPaperLanguageSystem;

namespace Content.Server._Starlight.Paper;

/// <summary>
/// What each player was sent of a paper, and its scrambled text.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class PaperLanguageStateComponent : Component
{
    /// <summary>
    /// Language each player picked to write in.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, ProtoId<LanguagePrototype>> WritingLanguages = [];

    /// <summary>
    /// Last view sent to each player, so unchanged views aren't sent again.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, PaperSentView> SentViews = [];

    /// <summary>
    /// Numbered views sent to each player and the text each showed, so form fills land in the right form.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, List<(int View, string? Content)>> ViewHistory = [];

    /// <summary>
    /// Locked sections sent to each player, by id.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, Dictionary<int, HiddenPaperSection>> HiddenSections = [];

    /// <summary>
    /// Last hidden section id given to each player.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, int> NextHiddenIds = [];

    /// <summary>
    /// Scrambled text per section, shared by everyone who can't read it.
    /// </summary>
    [ViewVariables]
    public Dictionary<(ProtoId<LanguagePrototype> Language, string Text), string> Scrambled = [];

    [ViewVariables]
    public List<PaperSection> Sections = [];

    /// <summary>
    /// The text <see cref="Sections"/> and <see cref="Scrambled"/> belong to.
    /// </summary>
    [ViewVariables]
    public string? ScrambledContent;
}

/// <summary>
/// What a player's paper UI was last built from.
/// </summary>
public readonly record struct PaperSentView(
    string Content,
    List<StampDisplayInfo> Stamps,
    int StampCount,
    PaperComponent.PaperAction Mode,
    List<ProtoId<LanguagePrototype>> Languages,
    ProtoId<LanguagePrototype>? DefaultLanguage)
{
    // Changed text is always a new string, so references are enough
    public bool Matches(PaperSentView other) =>
        ReferenceEquals(Content, other.Content)
        && ReferenceEquals(Stamps, other.Stamps)
        && StampCount == other.StampCount
        && Mode == other.Mode
        && DefaultLanguage == other.DefaultLanguage
        && Languages.SequenceEqual(other.Languages);
}

/// <summary>
/// A locked section sent to a player, and what they were sent in its place.
/// </summary>
public readonly record struct HiddenPaperSection(ProtoId<LanguagePrototype> Language, string Original, string Rendered);
