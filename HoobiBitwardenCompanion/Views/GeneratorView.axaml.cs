using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class GeneratorView : UserControl
{
  private static readonly HashSet<GeneratorViewModel> Open = [];

  public GeneratorView() => InitializeComponent();

  internal static void OnLocked()
  {
    foreach (var viewModel in Open)
      viewModel.OnLocked();
  }

  internal static FAContentDialog CreateDialog(GeneratorViewModel viewModel)
  {
    var dialog = new FAContentDialog
    {
      Title = "Generator",
      Content = new ScrollViewer { MaxHeight = 560, Content = new GeneratorView { DataContext = viewModel } },
      PrimaryButtonText = "Use password",
      CloseButtonText = "Close",
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
    var dialog = CreateDialog(viewModel);
    string? used = null;
    viewModel.UseRequested = value => used = value;
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
