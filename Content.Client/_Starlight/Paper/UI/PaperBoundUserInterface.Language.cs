// ReSharper disable CheckNamespace
using System.Diagnostics;
using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
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

        // The server also sends a view when the UI opens, but that can arrive before this window exists.
        // This request can arrive too early instead, so both are needed. See OnUIOpened on the server
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
        _window.View = view.View;
        if (view.HiddenLength is { } hidden)
            _window.HiddenLength = hidden;

        _window.Populate(view.State);
        _window.ResyncLanguageTracking();
        _window.UpdateLanguageBar(view.WritableLanguages, view.DefaultLanguage, view.State.Mode == PaperAction.Write);
        Logger.GetSawmill("paper.lang").Info($"ReceiveMessage took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }
}
