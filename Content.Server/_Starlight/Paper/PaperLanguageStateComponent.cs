using System.Linq;
using Content.Shared._Starlight.Language;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;
using static Content.Shared._Starlight.Paper.SharedPaperLanguageSystem;

namespace Content.Server._Starlight.Paper;

/// <summary>
/// Per player view state of a paper, plus its scrambled text.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class PaperLanguageStateComponent : Component
{
    [ViewVariables]
    public Dictionary<EntityUid, PaperViewerState> Viewers = [];

    /// <summary>
    /// Last hidden section id handed out. Shared by all viewers and never reset.
    /// </summary>
    [ViewVariables]
    public int NextHiddenId;

    /// <summary>
    /// Scrambled text per section, shared by everyone who can't read it.
    /// </summary>
    [ViewVariables]
    public Dictionary<(ProtoId<LanguagePrototype> Language, string Original), ScrambledText> Scrambled = [];

    [ViewVariables]
    public List<PaperSection> Sections = [];

    /// <summary>
    /// The text <see cref="Sections"/> and <see cref="Scrambled"/> belong to.
    /// </summary>
    [ViewVariables]
    public string? SectionsContent;

    /// <summary>
    /// Goes up each time the text changes. Form fills from an older version are rejected.
    /// </summary>
    [ViewVariables]
    public int ContentVersion;
}

/// <summary>
/// What one player has open of a paper. Cleared when they close it.
/// </summary>
public sealed class PaperViewerState
{
    [ViewVariables]
    public ProtoId<LanguagePrototype>? WritingLanguage;

    /// <summary>
    /// Last view sent, null if the next one has to be resent.
    /// </summary>
    [ViewVariables]
    public PaperSentView? SentView;

    /// <summary>
    /// How much longer the last text sent was than the paper, from ids and scrambles.
    /// </summary>
    [ViewVariables]
    public int SentExtra;

    /// <summary>
    /// Content version the open editor was filled from, null when not writing.
    /// </summary>
    [ViewVariables]
    public int? EditVersion;

    [ViewVariables]
    public Dictionary<int, HiddenPaperSection> HiddenSections = [];
}

public readonly record struct PaperSentView(
    string Content,
    List<StampDisplayInfo> Stamps,
    int StampCount,
    PaperComponent.PaperAction Mode,
    List<ProtoId<LanguagePrototype>> Languages,
    ProtoId<LanguagePrototype>? DefaultLanguage)
{
    // Changed text is always a new string, reference checks are enough
    public bool Matches(PaperSentView other) =>
        ReferenceEquals(Content, other.Content)
        && ReferenceEquals(Stamps, other.Stamps)
        && StampCount == other.StampCount
        && Mode == other.Mode
        && DefaultLanguage == other.DefaultLanguage
        && Languages.SequenceEqual(other.Languages);
}

public sealed class ScrambledText(string text)
{
    [ViewVariables]
    public readonly string Text = text;

    /// <summary>
    /// Text shown in the editor instead. Filled by GetPlaceholder the first time someone edits the paper.
    /// </summary>
    [ViewVariables]
    public string? Placeholder;
}

/// <summary>
/// Locked section sent to a player and the text shown in its place.
/// </summary>
public readonly record struct HiddenPaperSection(ProtoId<LanguagePrototype> Language, string Original, string Rendered);
