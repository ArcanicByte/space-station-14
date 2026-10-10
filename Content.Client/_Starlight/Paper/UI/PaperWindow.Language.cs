// ReSharper disable CheckNamespace
using System.Linq;
using Content.Shared._Starlight.Language;
using Content.Shared.Paper;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Content.Shared._Starlight.Paper.SharedPaperLanguageSystem;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Content.Client.Paper.UI;

public sealed partial class PaperWindow
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;

    public event Action<ProtoId<LanguagePrototype>>? OnLanguageSelected;

    /// <summary>
    /// Form filled in: content version, form index, answer.
    /// </summary>
    public event Action<int, int, string>? OnFormFilled;

    /// <summary>
    /// Check ticked: content version, check index, mark.
    /// </summary>
    public event Action<int, int, char>? OnCheckFilled;

    /// <summary>
    /// Version of the paper text being shown. Sent with form fills.
    /// </summary>
    public int ContentVersion { get; set; }

    public TimeSpan SaveDelay { get; set; }

    public ProtoId<LanguagePrototype>? SelectedLanguage { get; private set; }

    /// <summary>
    /// Whether the player can read a language. Translate only changes locked sections they can read.
    /// </summary>
    public Func<ProtoId<LanguagePrototype>, bool> CanRead { get; set; } = _ => false;

    /// <summary>
    /// Paper length minus the length of the text sent to this player. Added to the character count.
    /// </summary>
    public int HiddenLength
    {
        get => _hiddenLength;
        set
        {
            _hiddenLength = value;
            UpdateFillState();
        }
    }

    private int _hiddenLength;
    private Popup? _activeFormPopup;
    private readonly List<ProtoId<LanguagePrototype>> _languageOptions = [];
    private string? _saveButtonText;
    private static TimeSpan s_saveCooldownEnd;

    // Handled next frame since OnTextChanged fires before the cursor moves
    private string _trackedText = string.Empty;
    private bool _editPending;

    public void InitializeLanguageBar()
    {
        LanguageSelector.OnItemSelected += args =>
        {
            LanguageSelector.SelectId(args.Id);
            var language = _languageOptions[args.Id];
            SelectedLanguage = language;
            OnLanguageSelected?.Invoke(language);
            UpdateWarnings();
        };

        TranslateButton.OnPressed += _ =>
        {
            if (SelectedLanguage is { } language)
                SetInputText(TranslateSections(Rope.Collapse(Input.TextRope), language, CanRead));
        };
        Input.OnTextChanged += _ => _editPending = true;
        LockedWarningLabel.SetMessage(Loc.GetString("paper-ui-locked-warning"), null, Color.Gold);
        NoLanguageLabel.FontColorOverride = Color.Gold;
        _saveButtonText = SaveButton.Text;

        // Every fill shares the server's save cooldown
        OnSaved += _ => StartSaveCooldown();
        OnFormFilled += (_, _, _) => StartSaveCooldown();
        OnCheckFilled += (_, _, _) => StartSaveCooldown();
        OnSignatureRequested += _ => StartSaveCooldown();
        OnDateTimeRequested += _ => StartSaveCooldown();
    }

    private void StartSaveCooldown() => s_saveCooldownEnd = _timing.RealTime + SaveDelay;

    /// <summary>
    /// Closes the form dialog. True if one was open.
    /// </summary>
    public bool CloseFormDialog()
    {
        if (_activeFormPopup is not { Visible: true } popup)
            return false;

        popup.Close();
        _activeFormPopup = null;
        return true;
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

    public void UpdateLanguageBar(List<ProtoId<LanguagePrototype>> languages, ProtoId<LanguagePrototype>? defaultLanguage, bool isEditing)
    {
        var timer = Stopwatch.GetTimestamp();
        // Readers need it too for forms, signatures and dates
        LanguageBar.Visible = isEditing
            || FillableTags.Any(tag => _currentRawText.Contains(tag, StringComparison.Ordinal));

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

        if (SelectedLanguage == null || !_languageOptions.Contains(SelectedLanguage.Value))
            SelectedLanguage = defaultLanguage;

        var hasLanguages = _languageOptions.Count > 0;
        LanguageSelector.Visible = hasLanguages;
        TranslateButton.Visible = hasLanguages && isEditing;
        NoLanguageLabel.Visible = !hasLanguages;

        // Without a language, saving only closes the editor
        SaveButton.Text = hasLanguages ? _saveButtonText : Loc.GetString("paper-ui-close-button");
        UpdateWarnings();

        if (SelectedLanguage != null)
        {
            var index = _languageOptions.IndexOf(SelectedLanguage.Value);
            if (index >= 0)
                LanguageSelector.SelectId(index);
        }

        Logger.GetSawmill("paper.lang").Info($"UpdateLanguageBar took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    public void ShowWithoutText(bool writing)
    {
        // Stamps come with the text and lay out around it
        Populate(new PaperComponent.PaperBoundUserInterfaceState(string.Empty, [], writing ? PaperComponent.PaperAction.Write : PaperComponent.PaperAction.Read));
        BlankPaperIndicator.Visible = false;
    }

    /// <summary>
    /// Redraws stamps, text is left alone. Does nothing in the editor.
    /// </summary>
    public void RefreshStamps(List<StampDisplayInfo> stamps)
    {
        var timer = Stopwatch.GetTimestamp();
        if (InputContainer.Visible)
            return;

        StampDisplay.RemoveAllChildren();
        StampDisplay.RemoveStamps();
        foreach (var stamp in stamps)
            StampDisplay.AddStamp(new StampWidget { StampInfo = stamp });
        Logger.GetSawmill("paper.lang").Info($"RefreshStamps took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    /// <summary>
    /// Call after setting the editor text from code, otherwise it counts as typing.
    /// </summary>
    public void ResyncLanguageTracking()
    {
        _editPending = false;
        _trackedText = Rope.Collapse(Input.TextRope);
        UpdateWarnings();
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

        UpdateWarnings();
    }

    private void TagInsertedText(string previous, string text, int cursor, ProtoId<LanguagePrototype> language)
    {
        var timer = Stopwatch.GetTimestamp();
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
            || inserted.Contains(ClosingTag, StringComparison.Ordinal)
            || IsInsideMarkupTag(text, start))
            return;

        var (section, locked) = GetSectionAt(text, start);
        if (locked || (section ?? DefaultLanguage) == language)
        {
            // A backslash right before a section tag would turn the tag into text. Doubled it still shows as one
            if (IsEscaped(text, end) && StartsLanguageTag(text, end))
                SetInputText(text.Insert(end, "\\"), end + 1);

            return;
        }

        // The new text gets a closing tag right after it, which a trailing backslash would escape
        var escaped = EscapeTrailingBackslash(inserted);
        var added = escaped.Length - inserted.Length;
        inserted = escaped;

        // Inside a section Common needs its own tag, closing only goes back to the outer section
        var isDefault = language == DefaultLanguage && section == null;
        var open = isDefault ? string.Empty : OpeningTag(language);
        var close = isDefault ? string.Empty : ClosingTag;

        string newText;
        int newCursor;

        // Right after a section in the same language, extend it
        if (section == null
            && !isDefault
            && start >= close.Length
            && text[..start].EndsWith(close, StringComparison.Ordinal)
            && GetSectionAt(text, start - close.Length) is var previousSection
            && previousSection.Language == language
            && !previousSection.Locked)
        {
            newText = text[..(start - close.Length)] + inserted + close + text[end..];
            newCursor = end + added - close.Length;
        }
        else
        {
            // Close the section we're in around the new text, then reopen it
            var before = section != null ? ClosingTag + open : open;

            // Same for a backslash already in the section, right where it gets closed
            if (section != null && IsEscaped(text, start))
                before = "\\" + before;

            var after = section is { } reopen ? close + OpeningTag(reopen) : close;

            newText = text[..start] + before + inserted + after + text[end..];
            newCursor = end + added + before.Length;
        }

        SetInputText(newText, newCursor);
        Logger.GetSawmill("paper.lang").Info($"TagInsertedText took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    /// <summary>
    /// Replaces the editor text from code. The cursor goes to the given index, or the end.
    /// </summary>
    private void SetInputText(string text, int? cursor = null)
    {
        var timer = Stopwatch.GetTimestamp();
        Input.TextRope = Rope.Leaf.Empty;
        Input.CursorPosition = new TextEdit.CursorPos();
        Input.InsertAtCursor(text);
        if (cursor is { } index)
            Input.CursorPosition = new TextEdit.CursorPos(index, TextEdit.LineBreakBias.Top);

        UpdateFillState();
        ResyncLanguageTracking();
        Logger.GetSawmill("paper.lang").Info($"SetInputText took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
    }

    /// <summary>
    /// Updates the locked warning above the paper and the save warning next to the save button.
    /// </summary>
    private void UpdateWarnings()
    {
        var timer = Stopwatch.GetTimestamp();
        string? warning = null;
        var hasLocked = false;

        if (InputContainer.Visible)
        {
            var lost = new List<string>();
            foreach (var section in ParseSections(_trackedText))
            {
                if (section.Id != null)
                {
                    hasLocked = true;
                    continue;
                }

                var language = section.Language ?? DefaultLanguage;
                if (string.IsNullOrWhiteSpace(section.Text) || _languageOptions.Contains(language))
                    continue;

                var name = GetLanguageName(language);
                if (!lost.Contains(name))
                    lost.Add(name);
            }

            if (_languageOptions.Count == 0)
            {
                warning = Loc.GetString("paper-ui-cannot-save-warning");
            }
            else if (lost.Count > 0 && SelectedLanguage is { } selected)
            {
                warning = Loc.GetString("paper-ui-lost-language-warning",
                    ("languages", string.Join(", ", lost)),
                    ("language", GetLanguageName(selected)));
            }
        }

        LockedWarning.Visible = hasLocked;

        if (warning != null)
            SaveWarningLabel.SetMessage($"[font size=10]{warning}[/font]", null, Color.Gold);

        SaveWarningLabel.Visible = warning != null;
        Logger.GetSawmill("paper.lang").Info($"UpdateWarnings took {Stopwatch.GetElapsedTime(timer).TotalMilliseconds:0.000} ms");
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
