using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class LoginViewModel : DialogViewModel
{
  internal const int CredentialsStep = 0;
  internal const int CodeStep = 1;
  internal const int DeviceStep = 2;

  private static readonly int?[] MethodValues = [TwoFactorRules.Authenticator, TwoFactorRules.Email, TwoFactorRules.YubiKey, null];

  private bool _deviceVerificationPending;

  public LoginViewModel(PipeChannel channel, bool rememberSession, string? customDataDir, SetServerRequest? server = null) : base(channel)
  {
    Server = new ServerPickerViewModel(channel, server);
    RememberSession = rememberSession;
    CustomDataDirWarning = customDataDir is null
      ? null
      : $"Custom CLI data directory: {customDataDir}. Set BITWARDENCLI_APPDATA_DIR to this path before you run `bw login` in a terminal.";
    TwoStepIndex = RememberedTwoStepIndex;
    PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(IsBusy))
      {
        OnPropertyChanged(nameof(CredentialsEnabled));
        OnPropertyChanged(nameof(SubmitEnabled));
      }

      if (e.PropertyName != nameof(Step))
        return;
      OnPropertyChanged(nameof(IsCredentialsStep));
      OnPropertyChanged(nameof(IsCodeStep));
      OnPropertyChanged(nameof(IsDeviceStep));
      OnPropertyChanged(nameof(SubmitLabel));
      OnPropertyChanged(nameof(SubmitEnabled));
    };
  }

  internal static int RememberedTwoStepIndex;

  public static IReadOnlyList<string> MethodChoices { get; } = ["Authenticator app", "Email", "YubiKey OTP", "Auto-detect"];

  public static IReadOnlyList<string> TwoStepChoices { get; } = ["Authenticator app or email", "Duo", "WebAuthn (FIDO2)"];

  internal Action<SetServerRequest>? ApiKeyLoginRequested { get; set; }

  public bool IsUnsupportedTwoStep => TwoStepIndex > 0;

  public bool CredentialsEnabled => !IsBusy && !IsUnsupportedTwoStep;

  public bool SubmitEnabled => !IsBusy && !(IsCredentialsStep && IsUnsupportedTwoStep);

  public ServerPickerViewModel Server { get; }

  public string? CustomDataDirWarning { get; }

  public bool IsCredentialsStep => Step == CredentialsStep;

  public bool IsCodeStep => Step == CodeStep;

  public bool IsDeviceStep => Step == DeviceStep;

  public string SubmitLabel => Step switch
  {
    CodeStep => "Verify",
    DeviceStep => "Verify device",
    _ => "Log in",
  };

  public int? Method => MethodIndex >= 0 && MethodIndex < MethodValues.Length ? MethodValues[MethodIndex] : null;

  public string CodePlaceholder => TwoFactorRules.Describe(Method).Placeholder;

  public string CodeHint => TwoFactorRules.Describe(Method).EmptyHint;

  [ObservableProperty]
  public partial string? Email { get; set; }

  [ObservableProperty]
  public partial string? MasterPassword { get; set; }

  [ObservableProperty]
  public partial int MethodIndex { get; set; }

  [ObservableProperty]
  public partial int TwoStepIndex { get; set; }

  [ObservableProperty]
  public partial string? TwoStepError { get; private set; }

  [ObservableProperty]
  public partial bool RememberSession { get; set; }

  partial void OnTwoStepIndexChanged(int value)
  {
    RememberedTwoStepIndex = value;
    TwoStepError = null;
    OnPropertyChanged(nameof(IsUnsupportedTwoStep));
    OnPropertyChanged(nameof(CredentialsEnabled));
    OnPropertyChanged(nameof(SubmitEnabled));
  }

  [RelayCommand]
  internal async Task UseApiKeyLoginAsync()
  {
    TwoStepError = null;
    try
    {
      var reply = IpcJson.Read(await Channel.RequestAsync(Ops.SetSetting, IpcJson.ToElement(new SetSettingRequest(SettingKeys.UseApiKeyAuthentication, "true"), IpcJson.Default.SetSettingRequest)), IpcJson.Default.SetSettingResponse);
      if (!reply.Success)
      {
        TwoStepError = reply.Error ?? "Could not turn on API key login";
        return;
      }
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      TwoStepError = ItemViewModel.Describe(ex);
      return;
    }

    ApiKeyLoginRequested?.Invoke(Server.Current);
  }

  [ObservableProperty]
  public partial string? Code { get; set; }

  internal override void ClearSecrets()
  {
    Email = null;
    MasterPassword = null;
    Code = null;
  }

  internal override void OnWindowClosed() => CancelPendingDeviceVerification();

  [RelayCommand]
  internal void Back()
  {
    CancelPendingDeviceVerification();
    ErrorMessage = null;
    Code = null;
    Step = CredentialsStep;
  }

  protected override string BusyMessage => Step switch
  {
    CodeStep => "Checking your two-step login code...",
    DeviceStep => "Checking your verification code...",
    _ => "Logging in to Bitwarden...",
  };

  protected override async Task<bool> RunStepAsync() => Step switch
  {
    CredentialsStep => await SignInAsync(null),
    CodeStep => await SignInAsync(Code?.Trim()),
    _ => await VerifyDeviceAsync(),
  };

  private async Task<bool> SignInAsync(string? code)
  {
    if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(MasterPassword))
    {
      ErrorMessage = "Enter your email and master password";
      return false;
    }

    if (Step == CodeStep && !TwoFactorRules.Describe(Method).Validator(code ?? string.Empty))
    {
      ErrorMessage = $"Enter a valid {TwoFactorRules.Describe(Method).TypedNoun}";
      return false;
    }

    var request = new LoginRequest(Email.Trim(), MasterPassword, Method, code, RememberSession);
    var reply = IpcJson.Read(await Channel.RequestAsync(Ops.Login, IpcJson.ToElement(request, IpcJson.Default.LoginRequest), Timeout.InfiniteTimeSpan), IpcJson.Default.LoginResponse);
    if (reply.Success)
      return true;
    if (reply.TwoFactorRequired)
      MoveTo(CodeStep);
    else if (reply.DeviceVerificationRequired)
    {
      _deviceVerificationPending = true;
      MoveTo(DeviceStep);
    }
    else
      ErrorMessage = reply.Error ?? "Login failed";
    return false;
  }

  private async Task<bool> VerifyDeviceAsync()
  {
    var code = Code?.Trim() ?? string.Empty;
    if (!TwoFactorRules.IsNumericCode(code))
    {
      ErrorMessage = "Enter the 6 to 8 digit code sent to your login email";
      return false;
    }

    var reply = IpcJson.Read(await Channel.RequestAsync(Ops.SubmitDeviceVerification, IpcJson.ToElement(new DeviceVerificationRequest(code), IpcJson.Default.DeviceVerificationRequest), Timeout.InfiniteTimeSpan), IpcJson.Default.LoginResponse);
    _deviceVerificationPending = false;
    if (reply.Success)
      return true;
    ErrorMessage = reply.Error ?? "Verification failed - try again";
    return false;
  }

  private void MoveTo(int step)
  {
    Code = null;
    OnPropertyChanged(nameof(CodePlaceholder));
    OnPropertyChanged(nameof(CodeHint));
    Step = step;
  }

  private void CancelPendingDeviceVerification()
  {
    if (!_deviceVerificationPending)
      return;
    _deviceVerificationPending = false;
    _ = SendQuietlyAsync(Ops.CancelLogin, null);
  }
}
