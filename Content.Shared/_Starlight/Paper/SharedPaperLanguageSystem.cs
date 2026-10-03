using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._Starlight.Language;
using Content.Shared._Starlight.Language.Components;
using Content.Shared._Starlight.Language.Systems;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Paper;

/// <summary>
/// Handles paper written in multiple languages, split into [lang] sections.
/// </summary>
public abstract partial class SharedPaperLanguageSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedLanguageSystem _language = default!;

    /// <summary>
    /// Language of untagged text.
    /// </summary>
    public static readonly ProtoId<LanguagePrototype> DefaultLanguage = "GalacticCommon";

    // [lang="X"], [lang="X" id=N] and [/lang]
    private static readonly Regex _languageTagRegex = new(@"\[lang=""?(?<lang>[A-Za-z0-9_]+)""?(?:\s+id=(?<id>\d+))?\s*\]|\[/lang\]", RegexOptions.Compiled);

    /// <summary>
    /// A language that no longer exists can't be obfuscated, so just show it.
    /// </summary>
    public bool CanRead(EntityUid reader, ProtoId<LanguagePrototype> language) => !_prototype.HasIndex(language) || _language.CanUnderstand(reader, language);

    public bool CanReadAll(Entity<PaperComponent> paper, EntityUid reader) =>
        GetSections(paper).All(s => string.IsNullOrWhiteSpace(s.Text) || CanRead(reader, s.Language ?? DefaultLanguage));

    /// <summary>
    /// Writing in a language requires speaking it.
    /// </summary>
    public bool CanWrite(EntityUid writer, ProtoId<LanguagePrototype> language) =>
        _prototype.TryIndex(language, out var proto)
        && proto.Writable
        && (HasComp<UniversalLanguageSpeakerComponent>(writer) || _language.CanSpeak(writer, language));

    public List<ProtoId<LanguagePrototype>> GetWritableLanguages(EntityUid writer)
    {
        var result = new List<ProtoId<LanguagePrototype>>();

        void TryAdd(ProtoId<LanguagePrototype> language)
        {
            if (!result.Contains(language) && CanWrite(writer, language))
                result.Add(language);
        }

        if (TryComp<LanguageSpeakerComponent>(writer, out var speaker))
        {
            foreach (var language in speaker.SpokenLanguages)
                TryAdd(language);
        }

        if (HasComp<UniversalLanguageSpeakerComponent>(writer))
        {
            foreach (var language in _language.Languages.OrderBy(x => x.Id))
                TryAdd(language);
        }

        return result;
    }

    /// <summary>
    /// Default writing language. Galactic Common, then the current language, then the first writable one.
    /// </summary>
    /// <param name="writable">The writer's <see cref="GetWritableLanguages"/>, if already worked out.</param>
    public ProtoId<LanguagePrototype>? GetDefaultWritingLanguage(EntityUid writer, List<ProtoId<LanguagePrototype>>? writable = null)
    {
        if (CanWrite(writer, DefaultLanguage))
            return DefaultLanguage;

        ProtoId<LanguagePrototype> current = _language.GetLanguage(writer).ID;
        if (CanWrite(writer, current))
            return current;

        writable ??= GetWritableLanguages(writer);
        return writable.Count > 0 ? writable[0] : (ProtoId<LanguagePrototype>?) null;
    }

    protected virtual List<PaperSection> GetSections(Entity<PaperComponent> paper) => ParseSections(paper.Comp.Content);

    /// <summary>
    /// Call when the paper changes, to update everyone with it open.
    /// </summary>
    public virtual void UpdateViews(Entity<PaperComponent> paper)
    {
    }

    /// <summary>
    /// Forgets what was sent to a viewer once they close the paper.
    /// </summary>
    public virtual void ClearViewer(Entity<PaperComponent> paper, EntityUid viewer)
    {
    }

    public virtual bool CanSave(Entity<PaperComponent> paper, EntityUid actor, string text) => true;

    /// <summary>
    /// Turns text from the paper UI into paper content.
    /// </summary>
    public virtual string SaveEdit(Entity<PaperComponent> paper, EntityUid actor, string text) => text;

    /// <summary>
    /// Paper content with the Nth <paramref name="tag"/> filled in, or null if the player can't fill it.
    /// </summary>
    public virtual string? FillTag(Entity<PaperComponent> paper, EntityUid actor, string tag, int index, string text) => null;

    /// <summary>
    /// Paper text with language tags, obfuscated where the viewer can't read it.
    /// </summary>
    public virtual string GetStyledView(Entity<PaperComponent> paper, EntityUid viewer) => string.Empty;
}
