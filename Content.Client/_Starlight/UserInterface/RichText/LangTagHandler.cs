using Content.Shared._Starlight.Language;
using Content.Shared._Starlight.Language.Systems;
using Robust.Client.Player;
using Robust.Client.Replays.Playback;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Starlight.UserInterface.RichText;

/// <summary>
/// Styles a section of paper like its language in chat.
/// </summary>
public sealed partial class LangTagHandler : IMarkupTagHandler
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private MarkupTagManager _tags = default!;
    [Dependency] private IReplayPlaybackManager _replayPlayback = default!;

    public string Name => "lang";

    // [/lang] has no value, always push and pop one font and color
    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        GetStyle(node, out var fontId, out var color);

        // Load the font through the font tag, like chat
        if (fontId != null && _tags.GetMarkupTagHandler("font") is { } fontTag)
            fontTag.PushDrawContext(new MarkupNode("font", new MarkupParameter(fontId), null), context);
        else if (context.Font.TryPeek(out var currentFont))
            context.Font.Push(currentFont);

        var currentColor = context.Color.TryPeek(out var previousColor) ? previousColor : Color.Black;
        context.Color.Push(color is { } languageColor
            ? Color.InterpolateBetween(currentColor, languageColor, languageColor.A)
            : currentColor);
    }

    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        if (context.Font.Count > 0)
            context.Font.Pop();

        context.Color.Pop();
    }

    private void GetStyle(MarkupNode node, out string? fontId, out Color? color)
    {
        fontId = null;
        color = null;

        if (node.Value.StringValue is not { } languageId
            || !_prototype.TryIndex<LanguagePrototype>(languageId, out var language))
            return;

        // Replays show the real text, even when spectating someone who can't read it
        var obfuscated = _replayPlayback.Replay == null
            && (_player.LocalEntity is not { } player
                || !_entityManager.System<SharedLanguageSystem>().CanUnderstand(player, language.ID));

        // Obfuscation fonts are only for readers who don't understand it
        if (language.Speech.FontId is { } id
            && (language.Speech.ObfuscationFont != true || obfuscated)
            && _prototype.HasIndex<FontPrototype>(id))
            fontId = id;

        color = language.PaperColor ?? language.Speech.Color;
    }
}
