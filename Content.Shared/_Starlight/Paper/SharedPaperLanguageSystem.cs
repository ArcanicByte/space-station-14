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
    /// Default language when no [lang] tag.
    /// </summary>
    public static readonly ProtoId<LanguagePrototype> DefaultLanguage = "GalacticCommon";

    // [lang="X"], [lang="X" id=N] and [/lang]
    private static readonly Regex _languageTagRegex = new(@"\[lang=""?(?<lang>[A-Za-z0-9_]+)""?(?:\s+id=(?<id>\d+))?\s*\]|\[/lang\]", RegexOptions.Compiled);

    // Languages that no longer exist can't be scrambled, shown as is.
    public bool CanRead(EntityUid reader, ProtoId<LanguagePrototype> language) => !_prototype.HasIndex(language) || _language.CanUnderstand(reader, language);

    public bool CanReadAll(Entity<PaperComponent> paper, EntityUid reader)
    {
        foreach (var section in GetSections(paper))
        {
            if (string.IsNullOrWhiteSpace(section.Text))
                continue;

            if (!CanRead(reader, section.Language ?? DefaultLanguage))
                return false;
        }

        return true;
    }

    public bool CanWrite(EntityUid writer, ProtoId<LanguagePrototype> language)
    {
        if (!_prototype.TryIndex(language, out var proto) || !proto.Writable)
            return false;

        return HasComp<UniversalLanguageSpeakerComponent>(writer) || _language.CanSpeak(writer, language);
    }

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
    /// Default writing language. Galactic Common, then current spoken language selected, then the first writable one in list.
    /// </summary>
    public ProtoId<LanguagePrototype>? GetDefaultWritingLanguage(EntityUid writer, List<ProtoId<LanguagePrototype>>? writable = null)
    {
        if (CanWrite(writer, DefaultLanguage))
            return DefaultLanguage;

        ProtoId<LanguagePrototype> current = _language.GetLanguage(writer).ID;
        if (CanWrite(writer, current))
            return current;

        writable ??= GetWritableLanguages(writer);
        return writable.Count > 0 ? writable[0] : (ProtoId<LanguagePrototype>?)null;
    }

    protected virtual List<PaperSection> GetSections(Entity<PaperComponent> paper) => ParseSections(paper.Comp.Content);

    /// <summary>
    /// Turns text from the paper UI into paper content, or null if the save is rejected.
    /// </summary>
    public virtual string? TrySave(Entity<PaperComponent> paper, EntityUid actor, string text) => text.Length <= paper.Comp.ContentSize ? text : null;

    /// <summary>
    /// Paper content with the Nth <paramref name="tag"/> filled in, or null if the player can't fill it or the paper changed.
    /// </summary>
    public virtual string? FillTag(Entity<PaperComponent> paper, EntityUid actor, string tag, int version, int index, string text) => null;

    /// <summary>
    /// Paper text with language tags, obfuscated where the viewer can't read it.
    /// </summary>
    public virtual string GetStyledView(Entity<PaperComponent> paper, EntityUid viewer) => string.Empty;

    public virtual void UpdateViews(Entity<PaperComponent> paper) {}
    public virtual void ClearViewer(Entity<PaperComponent> paper, EntityUid viewer) {}
}
