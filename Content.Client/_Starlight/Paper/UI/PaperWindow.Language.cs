// ReSharper disable CheckNamespace
using System.Linq;
using Content.Shared._Starlight.Language;
using Content.Shared._Starlight.Paper;
using Content.Shared.Paper;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Paper.UI;

public sealed partial class PaperWindow
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;

    public event Action<ProtoId<LanguagePrototype>>? OnLanguageSelected;

    /// <summary>
    /// A [form] was filled in, with the view it was clicked in, its index and the answer.
    /// </summary>
    public event Action<int, int, string>? OnFormFilled;

    /// <summary>
    /// Number of the server view shown, so form fills can say which one they were clicked in.
    /// </summary>
    public int View { get; set; }

    /// <summary>
    /// Language new text is written in.
    /// </summary>
    public ProtoId<LanguagePrototype>? SelectedLanguage { get; private set; }

    private readonly List<ProtoId<LanguagePrototype>> _languageOptions = [];
    private string? _saveButtonText;
    private static readonly TimeSpan _saveCooldown = TimeSpan.FromSeconds(0.25);
    private static TimeSpan s_saveCooldownEnd;
    // OnTextChanged fires before the cursor moves, so edits are handled next frame
    private string _trackedText = string.Empty;
    private bool _editPending;

    public void InitializeLanguageBar()
    {
        LanguageSelector.OnItemSelected += args =>
        {
            LanguageSelector.SelectId(args.Id);
            SelectedLanguage = _languageOptions[args.Id];
            OnLanguageSelected?.Invoke(_languageOptions[args.Id]);
            UpdateSaveWarning();
        };

        TranslateButton.OnPressed += _ => TranslateInput();
        Input.OnTextChanged += _ => _editPending = true;
        LockedWarningLabel.SetMessage(Loc.GetString("paper-ui-locked-warning"), null, Color.Gold);
        OnSaved += _ => s_saveCooldownEnd = _timing.RealTime + _saveCooldown;
        _saveButtonText = SaveButton.Text;
    }

    private void UpdateSaveCooldown()
    {
        if (s_saveCooldownEnd == TimeSpan.Zero)
            return;

        if (_timing.RealTime < s_saveCooldownEnd)
        {
            SaveButton.Disabled = true;
            return;
        }

        s_saveCooldownEnd = TimeSpan.Zero;
        UpdateFillState();
    }

    /// <summary>
    /// Fills the language selector, keeping the selection if it's still valid.
    /// </summary>
    public void UpdateLanguageBar(List<ProtoId<LanguagePrototype>> languages, ProtoId<LanguagePrototype>? defaultLanguage, bool isEditing)
    {
        // Forms are answered in the selected language, so readers need it too
        LanguageBar.Visible = isEditing || _currentRawText.Contains("[form]", StringComparison.Ordinal);

        if (!_languageOptions.SequenceEqual(languages))
        {
            _languageOptions.Clear();
            _languageOptions.AddRange(languages);

            LanguageSelector.Clear();
            for (var i = 0; i < _languageOptions.Count; i++)
            {
                LanguageSelector.AddItem(GetLanguageName(_languageOptions[i]), i);
            }
        }

        if (SelectedLanguage is not { } selected || !_languageOptions.Contains(selected))
            SelectedLanguage = defaultLanguage ?? (_languageOptions.Count > 0 ? _languageOptions[0] : (ProtoId<LanguagePrototype>?) null);

        var hasLanguages = _languageOptions.Count > 0;
        LanguageSelector.Visible = hasLanguages;
        TranslateButton.Visible = hasLanguages && isEditing;
        NoLanguageLabel.Visible = !hasLanguages;

        // Without a language, saving only closes the editor
        SaveButton.Text = hasLanguages ? _saveButtonText : Loc.GetString("paper-ui-close-button");
        UpdateSaveWarning();

        if (SelectedLanguage is { } current && _languageOptions.IndexOf(current) is var index and >= 0)
            LanguageSelector.SelectId(index);
    }

    public void ShowWithoutText(bool writing)
    {
        // Stamps come with the text, so they lay out around it
        Populate(new PaperComponent.PaperBoundUserInterfaceState(string.Empty, [], writing ? PaperComponent.PaperAction.Write : PaperComponent.PaperAction.Read));
        BlankPaperIndicator.Visible = false;
    }

    /// <summary>
    /// Redraws stamps without touching the text. The editor has none.
    /// </summary>
    public void RefreshStamps(List<StampDisplayInfo> stamps)
    {
        if (InputContainer.Visible)
            return;

        StampDisplay.RemoveAllChildren();
        StampDisplay.RemoveStamps();
        foreach (var stamp in stamps)
            StampDisplay.AddStamp(new StampWidget { StampInfo = stamp });
    }

    private void TranslateInput()
    {
        if (SelectedLanguage is not { } language)
            return;

        var translated = SharedPaperLanguageSystem.TranslateUnlockedSections(Rope.Collapse(Input.TextRope), language);
        Input.TextRope = Rope.Leaf.Empty;
        Input.CursorPosition = new TextEdit.CursorPos();
        Input.InsertAtCursor(translated);
        UpdateFillState();
        ResyncLanguageTracking();
    }

    /// <summary>
    /// Call after the editor text is set by code, so it isn't treated as typing.
    /// </summary>
    public void ResyncLanguageTracking()
    {
        _editPending = false;
        _trackedText = Rope.Collapse(Input.TextRope);
        UpdateLockedWarning();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        UpdateSaveCooldown();

        if (!_editPending)
            return;

        _editPending = false;
        var previous = _trackedText;
        _trackedText = Rope.Collapse(Input.TextRope);

        if (InputContainer.Visible && SelectedLanguage is { } language)
            TagInsertedText(previous, _trackedText, Input.CursorPosition.Index, language);

        UpdateLockedWarning();
    }

    /// <summary>
    /// Tags newly typed text with the selected language.
    /// </summary>
    private void TagInsertedText(string previous, string text, int cursor, ProtoId<LanguagePrototype> language)
    {
        cursor = Math.Clamp(cursor, 0, text.Length);

        // The inserted text ends at the cursor
        var suffix = 0;
        var maxSuffix = Math.Min(previous.Length, text.Length - cursor);
        while (suffix < maxSuffix && previous[^(suffix + 1)] == text[^(suffix + 1)])
            suffix++;

        var prefix = 0;
        var maxPrefix = Math.Min(cursor, Math.Min(previous.Length, text.Length) - suffix);
        while (prefix < maxPrefix && previous[prefix] == text[prefix])
            prefix++;

        var start = prefix;
        var end = text.Length - suffix;
        if (end <= start)
            return;

        var inserted = text[start..end];

        if (string.IsNullOrWhiteSpace(inserted)
            || inserted.Contains("[lang", StringComparison.Ordinal)
            || inserted.Contains(SharedPaperLanguageSystem.ClosingTag, StringComparison.Ordinal)
            || IsInsideMarkupTag(text, start))
            return;

        var (section, locked) = SharedPaperLanguageSystem.GetSectionAt(text, start);
        if (locked || (section ?? SharedPaperLanguageSystem.DefaultLanguage) == language)
            return;

        var isDefault = language == SharedPaperLanguageSystem.DefaultLanguage;
        var open = isDefault ? string.Empty : SharedPaperLanguageSystem.OpeningTag(language);
        var close = isDefault ? string.Empty : SharedPaperLanguageSystem.ClosingTag;

        string newText;
        int newCursor;

        // Right after a section in the same language, extend it
        if (section == null
            && !isDefault
            && start >= close.Length
            && text[..start].EndsWith(close, StringComparison.Ordinal)
            && SharedPaperLanguageSystem.GetSectionAt(text, start - close.Length) is var previousSection
            && previousSection.Language == language
            && !previousSection.Locked)
        {
            newText = text[..(start - close.Length)] + inserted + close + text[end..];
            newCursor = end - close.Length;
        }
        else
        {
            // Close the section we're in around the new text, then reopen it
            var before = section != null ? SharedPaperLanguageSystem.ClosingTag + open : open;
            var after = section is { } reopen ? close + SharedPaperLanguageSystem.OpeningTag(reopen) : close;

            newText = text[..start] + before + inserted + after + text[end..];
            newCursor = end + before.Length;
        }

        Input.TextRope = Rope.Leaf.Empty;
        Input.CursorPosition = new TextEdit.CursorPos();
        Input.InsertAtCursor(newText);
        Input.CursorPosition = new TextEdit.CursorPos(newCursor, TextEdit.LineBreakBias.Top);
        UpdateFillState();
        ResyncLanguageTracking();
    }

    private void UpdateLockedWarning()
    {
        LockedWarning.Visible = InputContainer.Visible && _trackedText.Contains(" id=", StringComparison.Ordinal);
        UpdateSaveWarning();
    }

    /// <summary>
    /// Warns when the player can't write, or has text in a language they lost.
    /// </summary>
    private void UpdateSaveWarning()
    {
        string? warning = null;

        if (InputContainer.Visible && _languageOptions.Count == 0)
        {
            warning = Loc.GetString("paper-ui-cannot-save-warning");
        }
        else if (InputContainer.Visible && SelectedLanguage is { } selected)
        {
            var lost = new List<string>();
            foreach (var section in SharedPaperLanguageSystem.ParseSections(_trackedText))
            {
                var language = section.Language ?? SharedPaperLanguageSystem.DefaultLanguage;
                if (section.Id != null || string.IsNullOrWhiteSpace(section.Text) || _languageOptions.Contains(language))
                    continue;

                var name = GetLanguageName(language);
                if (!lost.Contains(name))
                    lost.Add(name);
            }

            if (lost.Count > 0)
            {
                warning = Loc.GetString("paper-ui-lost-language-warning",
                    ("languages", string.Join(", ", lost)),
                    ("language", GetLanguageName(selected)));
            }
        }

        if (warning != null)
            SaveWarningLabel.SetMessage(warning, null, Color.Gold);

        SaveWarningLabel.Visible = warning != null;
    }

    private string GetLanguageName(ProtoId<LanguagePrototype> language) =>
        _prototype.TryIndex(language, out var proto) ? proto.Name : language.Id;

    private static bool IsInsideMarkupTag(string text, int position)
    {
        if (position == 0)
            return false;

        var open = text.LastIndexOf('[', position - 1);
        if (open == -1)
            return false;

        var close = text.IndexOf(']', open);
        return close == -1 || close >= position;
    }
}
