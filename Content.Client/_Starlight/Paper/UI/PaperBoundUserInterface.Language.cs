// ReSharper disable CheckNamespace
using System.Diagnostics;
using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
using Content.Shared.Popups;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Client.Paper.UI;

public sealed partial class PaperBoundUserInterface
{
    /// <summary>
    /// Whether the server's text has arrived. Stamps wait for it.
    /// </summary>
    private bool _hasText;

    private void OpenLanguage()
    {
        var timer = Stopwatch.GetTimestamp();
        if (_window == null)
            return;

        _window.InitializeLanguageBar();
        _window.OnLanguageSelected += language => SendMessage(new PaperSelectLanguageMessage(language));
        _window.OnFormFilled += (version, index, text) => SendMessage(new PaperFormFillMessage(version, index, text));
        _window.OnCheckFilled += (version, index, mark) => SendMessage(new PaperCheckFillMessage(version, index, mark));

        // Show a blank paper until the server sends the text
        if (EntMan.TryGetComponent<PaperComponent>(Owner, out var paper) && PlayerManager.LocalEntity is { } player)
        {
            var writing = paper.Writers.Contains(player);
            _window.SaveDelay = paper.SaveDelay;
            _window.ShowWithoutText(writing);
            _window.ResyncLanguageTracking();

            var paperLanguage = EntMan.System<SharedPaperLanguageSystem>();
            _window.CanRead = language => paperLanguage.CanRead(player, language);
            var languages = paperLanguage.GetWritableLanguages(player);
            _window.UpdateLanguageBar(languages, paperLanguage.GetDefaultWritingLanguage(player, languages), writing);
        }

        // The server's view on open can arrive before this window exists, and this request can arrive
        // before the server opens the UI. Both are needed, see OnUIOpened on the server
        SendMessage(new PaperViewRequestMessage());
        Logger.GetSawmill("paper.lang").Info($"OpenLanguage took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

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
        var timer = Stopwatch.GetTimestamp();
        base.ReceiveMessage(message);

        if (message is not PaperViewMessage view || _window == null)
            return;

        _hasText = true;

        // An open form belongs to the old text, the server would turn it away. Check dialogs just bounce
        if (view.ContentVersion != _window.ContentVersion && _window.CloseFormDialog())
            EntMan.System<SharedPopupSystem>().PopupCursor(Loc.GetString("paper-form-changed"));

        _window.ContentVersion = view.ContentVersion;
        if (view.HiddenLength is { } hidden)
            _window.HiddenLength = hidden;

        _window.Populate(view.State);
        _window.ResyncLanguageTracking();
        _window.UpdateLanguageBar(view.WritableLanguages, view.DefaultLanguage, view.State.Mode == PaperAction.Write);
        Logger.GetSawmill("paper.lang").Info($"ReceiveMessage took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }
}
