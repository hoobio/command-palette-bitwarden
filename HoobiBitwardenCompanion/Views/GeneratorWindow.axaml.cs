using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class GeneratorWindow : Window
{
  public GeneratorWindow() : this(null)
  {
  }

  public GeneratorWindow(GeneratorViewModel? viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    Topmost = ItemWindow.AlwaysOnTop;
    AppTitleBar.PointerPressed += OnTitleBarPointerPressed;
    if (viewModel is not null)
      viewModel.PropertyChanged += OnViewModelPropertyChanged;
    Opened += (_, _) =>
    {
      if (Screens.ScreenFromWindow(this) is { } screen)
        MaxHeight = screen.WorkingArea.Height / screen.Scaling;
      FitToContent();
    };
    Resized += (_, e) => _resizedByUser |= e.Reason == WindowResizeReason.User;
    Closed += (_, _) =>
    {
      if (viewModel is not null)
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    };
  }

  private bool _resizedByUser;

  internal GeneratorViewModel? ViewModel => DataContext as GeneratorViewModel;

  internal Func<string, Task>? SaveAsNewItem { get; set; }

  private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(GeneratorViewModel.OptionsExpanded) && !_resizedByUser)
      Dispatcher.UIThread.Post(FitToContent, DispatcherPriority.Loaded);
  }

  // SizeToContent did not resize the open headless window when the content changed, so the height is set from the measured content.
  private void FitToContent()
  {
    if (!IsVisible)
      return;
    var content = (Control)Content!;
    content.Measure(new Size(ClientSize.Width, double.PositiveInfinity));
    Height = Math.Clamp(content.DesiredSize.Height, MinHeight, MaxHeight);
  }

  private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      BeginMoveDrag(e);
  }

  internal Task SaveAsNewItemAsync() =>
    ViewModel?.Value is { Length: > 0 } value && SaveAsNewItem is { } save ? save(value) : Task.CompletedTask;

  private void OnSaveAsNewItemClick(object? sender, RoutedEventArgs e) => _ = SaveAsNewItemAsync();
}
