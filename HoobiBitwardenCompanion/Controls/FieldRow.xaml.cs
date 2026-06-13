using System;
using HoobiBitwardenCompanion.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HoobiBitwardenCompanion.Controls;

// Reusable labelled field row (COMPANION_WINUI_PHASE1 §3.4): read-only display with copy, optional
// secret masking + reveal, optional regenerate, and an inline edit mode. One component for every
// field the item detail window renders, so adding a fuller vault UI later reuses it.
public sealed partial class FieldRow : UserControl
{
    private static readonly FontFamily Monospace = new("Consolas");
    private bool _revealed;

    public FieldRow() => InitializeComponent();

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(FieldRow), new PropertyMetadata(string.Empty));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(FieldRow), new PropertyMetadata(string.Empty, OnAppearanceChanged));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty IsSecretProperty =
        DependencyProperty.Register(nameof(IsSecret), typeof(bool), typeof(FieldRow), new PropertyMetadata(false, OnAppearanceChanged));

    public bool IsSecret
    {
        get => (bool)GetValue(IsSecretProperty);
        set => SetValue(IsSecretProperty, value);
    }

    public static readonly DependencyProperty ShowRegenerateProperty =
        DependencyProperty.Register(nameof(ShowRegenerate), typeof(bool), typeof(FieldRow), new PropertyMetadata(false, OnAppearanceChanged));

    public bool ShowRegenerate
    {
        get => (bool)GetValue(ShowRegenerateProperty);
        set => SetValue(ShowRegenerateProperty, value);
    }

    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(FieldRow), new PropertyMetadata(false, OnAppearanceChanged));

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public static readonly DependencyProperty ShowCopyProperty =
        DependencyProperty.Register(nameof(ShowCopy), typeof(bool), typeof(FieldRow), new PropertyMetadata(true, OnAppearanceChanged));

    public bool ShowCopy
    {
        get => (bool)GetValue(ShowCopyProperty);
        set => SetValue(ShowCopyProperty, value);
    }

    // Raised when the user clicks regenerate; the host opens the generator and assigns the result.
    public event EventHandler? RegenerateRequested;

    // The live value: the edit box when editing, otherwise the stored value.
    public string CurrentValue => IsEditing ? EditBox.Text : Value;

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((FieldRow)d).UpdateAppearance();

    private void UpdateAppearance()
    {
        if (DisplayText == null) return; // before template is ready

        var font = IsSecret ? Monospace : FontFamily.XamlAutoFontFamily;
        DisplayText.FontFamily = font;
        EditBox.FontFamily = font;

        EditBox.Visibility = IsEditing ? Visibility.Visible : Visibility.Collapsed;
        DisplayText.Visibility = IsEditing ? Visibility.Collapsed : Visibility.Visible;

        // Sync the edit box only when it differs, so typing (which writes Value back) doesn't loop,
        // while an external change (e.g. regenerate) still flows in.
        if (IsEditing && EditBox.Text != (Value ?? string.Empty))
            EditBox.Text = Value ?? string.Empty;

        DisplayText.Text = IsSecret && !_revealed && !IsEditing
            ? new string('•', Math.Min(12, string.IsNullOrEmpty(Value) ? 0 : Value.Length))
            : Value ?? string.Empty;

        RevealButton.Visibility = IsSecret && !IsEditing ? Visibility.Visible : Visibility.Collapsed;
        RegenerateButton.Visibility = ShowRegenerate && IsEditing ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = ShowCopy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        _revealed = !_revealed;
        UpdateAppearance();
    }

    private void OnRegenerateClick(object sender, RoutedEventArgs e) => RegenerateRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CurrentValue))
            ClipboardHelper.Copy(CurrentValue);
    }

    private void OnEditTextChanged(object sender, TextChangedEventArgs e)
    {
        // Keep Value in sync so CurrentValue and a later read are consistent without two-way binding.
        SetValue(ValueProperty, EditBox.Text);
    }
}
