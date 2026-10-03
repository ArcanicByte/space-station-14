// ReSharper disable CheckNamespace
using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Client.Paper.UI;

public sealed partial class PaperBoundUserInterface
{
    private void OpenLanguage()
    {
        if (_window == null)
            return;

        _window.InitializeLanguageBar();
        _window.OnLanguageSelected += language => SendMessage(new PaperSelectLanguageMessage(language));
        _window.OnFormFilled += (view, index, text) => SendMessage(new PaperFormFillMessage(view, index, text));

        // Show a blank paper until the server sends the text
        if (EntMan.TryGetComponent<PaperComponent>(Owner, out var paper) && PlayerManager.LocalEntity is { } player)
        {
            var writing = paper.Writers.Contains(player);
            _window.ShowWithoutText(writing);
            _window.ResyncLanguageTracking();

            var paperLanguage = EntMan.System<SharedPaperLanguageSystem>();
            var languages = paperLanguage.GetWritableLanguages(player);
            _window.UpdateLanguageBar(languages, paperLanguage.GetDefaultWritingLanguage(player, languages), writing);
        }

        SendMessage(new PaperViewRequestMessage());
    }

    /// <summary>
    /// Whether the server's text has arrived. Stamps wait for it.
    /// </summary>
    private bool _hasText;

    /// <summary>
    /// Shows predicted stamps right away.
    /// </summary>
    public override void Update()
    {
        base.Update();

        if (_hasText && _window != null && EntMan.TryGetComponent<PaperComponent>(Owner, out var paper))
            _window.RefreshStamps(paper.StampedBy);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is not PaperViewMessage view || _window == null)
            return;

        _hasText = true;
        _window.View = view.View;
        _window.Populate(view.State);
        _window.ResyncLanguageTracking();
        _window.UpdateLanguageBar(view.WritableLanguages, view.DefaultLanguage, view.State.Mode == PaperAction.Write);
    }
}
