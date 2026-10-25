using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class GeneratorView : UserControl
{
  private static readonly HashSet<GeneratorViewModel> Open = [];

  public GeneratorView()
  {
    InitializeComponent();
    PeekButton.AddHandler(PointerPressedEvent, OnPeekPressed, RoutingStrategies.Tunnel);
    PeekButton.AddHandler(PointerReleasedEvent, OnPeekReleased, RoutingStrategies.Tunnel);
    PeekButton.AddHandler(PointerCaptureLostEvent, (_, _) => CurrentPassword?.Unpeek());
  }

  private FieldViewModel? CurrentPassword => (DataContext as GeneratorViewModel)?.CurrentPassword;

  // Handled on tunnel so the button never takes the press as a click.
  private void OnPeekPressed(object? sender, PointerPressedEventArgs e)
  {
    if (CurrentPassword is not { } field)
      return;
    e.Handled = true;
    e.Pointer.Capture(PeekButton);
    _ = field.PeekAsync();
  }

  private void OnPeekReleased(object? sender, PointerReleasedEventArgs e)
  {
    CurrentPassword?.Unpeek();
    e.Pointer.Capture(null);
  }

  internal static void OnLocked()
  {
    foreach (var viewModel in Open)
      viewModel.OnLocked();
  }

  internal static FAContentDialog CreateDialog(GeneratorViewModel viewModel, string title = "Generator", string primaryLabel = "Use password", string closeLabel = "Close")
  {
    var dialog = new FAContentDialog
    {
      Title = title,
      Content = new ScrollViewer { MaxHeight = 560, Content = new GeneratorView { DataContext = viewModel } },
      PrimaryButtonText = primaryLabel,
      CloseButtonText = closeLabel,
      DefaultButton = FAContentDialogButton.Close,
    };
    dialog.PrimaryButtonClick += (_, e) =>
    {
      viewModel.UseCommand.Execute(null);
      e.Cancel = !viewModel.HasValue;
    };
    return dialog;
  }

  internal static async Task<string?> ShowAsync(TopLevel? owner, PipeChannel channel)
  {
    var viewModel = new GeneratorViewModel(channel) { Toast = message => ToastWindow.Show(owner, message) };
    return (await ShowAsync(owner, viewModel, CreateDialog(viewModel)))?.Value;
  }

  // The dialog closes when the item asks for the master password, because the password panel sits behind it.
  internal static async Task<long?> ShowForRotateAsync(TopLevel? owner, ItemViewModel item)
  {
    var current = item.HasPassword ? new FieldViewModel(item, new FieldDto(ItemFields.Password, "Password", string.Empty, null, true)) : null;
    var viewModel = new GeneratorViewModel(item.Channel) { Toast = message => ToastWindow.Show(owner, message), CurrentPassword = current };
    var dialog = CreateDialog(viewModel, "Rotate password", "Save", "Cancel");
    void HideOnPrompt(object? sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(ItemViewModel.IsPrompting) && item.IsPrompting)
        dialog.Hide();
    }

    item.PropertyChanged += HideOnPrompt;
    try
    {
      return (await ShowAsync(owner, viewModel, dialog))?.EntryId;
    }
    finally
    {
      item.PropertyChanged -= HideOnPrompt;
      current?.Unpeek();
    }
  }

  private static async Task<(string Value, long? EntryId)?> ShowAsync(TopLevel? owner, GeneratorViewModel viewModel, FAContentDialog dialog)
  {
    (string Value, long? EntryId)? used = null;
    viewModel.UseRequested = value => used = (value, viewModel.EntryId);
    Open.Add(viewModel);
    try
    {
      _ = viewModel.InitializeAsync();
      await dialog.ShowAsync(owner);
      return used;
    }
    finally
    {
      Open.Remove(viewModel);
    }
  }
}
