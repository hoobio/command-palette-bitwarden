using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed record FieldGroupViewModel(string Name, IReadOnlyList<FieldViewModel> Fields);

internal sealed partial class FieldViewModel : ObservableObject
{
  internal const string Mask = "••••••••";

  private readonly ItemViewModel? _owner;
  private readonly string? _value;

  public FieldViewModel(ItemViewModel? owner, FieldDto field, bool canCopy = true)
  {
    _owner = owner;
    _value = field.Value;
    Key = field.Key;
    Label = field.Label;
    IsSecret = field.IsSecret && field.Key != ItemFields.Totp;
    CanOpen = field.CanOpen;
    IsMultiline = field.IsMultiline;
    CanCopy = canCopy;
  }

  public string Key { get; }

  public string Label { get; }

  public bool IsSecret { get; }

  public bool CanOpen { get; }

  public bool CanCopy { get; }

  public bool IsMultiline { get; }

  public bool IsTotp => Key == ItemFields.Totp;

  public bool IsSensitive => IsSecret || IsTotp;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(DisplayValue))]
  public partial string? RevealedValue { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(DisplayValue))]
  public partial bool IsRevealed { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(DisplayValue))]
  public partial string? TotpCode { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(TotpSweep))]
  public partial int TotpRemaining { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(TotpSweep))]
  public partial int TotpPeriod { get; set; } = 30;

  public double TotpSweep => TotpPeriod > 0 ? 360.0 * TotpRemaining / TotpPeriod : 0;

  public string DisplayValue => IsTotp
    ? TotpCode ?? Mask
    : IsSecret ? (IsRevealed ? RevealedValue ?? string.Empty : Mask) : _value ?? string.Empty;

  internal void Wipe()
  {
    IsRevealed = false;
    RevealedValue = null;
    TotpCode = null;
  }

  [RelayCommand]
  private Task ToggleRevealAsync() => _owner?.ToggleRevealAsync(this) ?? Task.CompletedTask;

  [RelayCommand]
  private Task CopyAsync() => _owner?.CopyAsync(this) ?? Task.CompletedTask;

  [RelayCommand]
  private Task OpenAsync() => _owner?.OpenUriAsync(this) ?? Task.CompletedTask;
}
