using System;
using System.Threading.Tasks;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanionIpc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HoobiBitwardenCompanion.Views;

// WinUI login/unlock flow (COMPANION_WINUI_PHASE1 §3.3). Drives the extension's existing auth
// (LoginAsync / UnlockAsync / UnlockWithBiometricsAsync / SubmitDeviceVerificationAsync) over IPC -
// no auth logic is reimplemented here. Windows Hello reuses the extension's DesktopIpcService path.
public sealed partial class LoginPage : Page
{
    private VaultClient? _client;
    private Action? _close;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private bool _awaitingDeviceVerification;

    public LoginPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = StartAsync(e.Parameter);
    }

    private async Task StartAsync(object? parameter)
    {
        if (parameter is not CompanionContext { Client: { } client } ctx)
        {
            ShowStatus("Not connected to the Bitwarden extension.", isError: true);
            return;
        }
        _client = client;
        _close = ctx.RequestClose;
        await RouteByStatusAsync();
    }

    private async Task RouteByStatusAsync()
    {
        SetBusy(true);
        var status = await _client!.GetStatusAsync();
        SetBusy(false);

        switch (status)
        {
            case IpcStatus.Unlocked:
                Done();
                break;
            case IpcStatus.Locked:
                SubheaderText.Text = "Your vault is locked. Unlock to continue.";
                Show(UnlockPanel);
                break;
            case IpcStatus.Unauthenticated:
                SubheaderText.Text = "Log in to your Bitwarden account.";
                Show(LoginPanel);
                break;
            default:
                ShowStatus("The Bitwarden CLI was not found. Install it from the Command Palette first.", isError: true);
                break;
        }
    }

    private void OnLoginClick(object sender, RoutedEventArgs e) => _ = LoginAsync();

    private async Task LoginAsync()
    {
        _email = EmailBox.Text.Trim();
        _password = LoginPasswordBox.Password;
        if (string.IsNullOrEmpty(_email) || string.IsNullOrEmpty(_password))
        {
            ShowStatus("Enter your email and master password.", isError: true);
            return;
        }

        SetBusy(true);
        var (ok, error, twoFactorRequired, deviceVerificationRequired) = await _client!.LoginAsync(_email, _password);
        SetBusy(false);

        if (ok) { Done(); return; }
        if (deviceVerificationRequired) { _awaitingDeviceVerification = true; ShowCode("Enter the verification code sent to your email."); return; }
        if (twoFactorRequired) { _awaitingDeviceVerification = false; ShowCode("Enter your two-factor authentication code."); return; }
        ShowStatus(error ?? "Login failed.", isError: true);
    }

    private void OnCodeClick(object sender, RoutedEventArgs e) => _ = SubmitCodeAsync();

    private async Task SubmitCodeAsync()
    {
        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code)) { ShowStatus("Enter the code.", isError: true); return; }

        SetBusy(true);
        bool ok;
        string? error;
        if (_awaitingDeviceVerification)
        {
            (ok, error) = await _client!.SubmitDeviceVerificationAsync(code);
        }
        else
        {
            var r = await _client!.LoginAsync(_email, _password, code);
            (ok, error) = (r.Ok, r.Error);
        }
        SetBusy(false);

        if (ok) Done();
        else ShowStatus(error ?? "That code was not accepted.", isError: true);
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e) => _ = UnlockAsync();

    private async Task UnlockAsync()
    {
        var password = UnlockPasswordBox.Password;
        if (string.IsNullOrEmpty(password)) { ShowStatus("Enter your master password.", isError: true); return; }

        SetBusy(true);
        var (ok, error) = await _client!.UnlockAsync(password);
        SetBusy(false);

        if (ok) Done();
        else ShowStatus(error ?? "Unlock failed.", isError: true);
    }

    private void OnBiometricClick(object sender, RoutedEventArgs e) => _ = BiometricAsync();

    private async Task BiometricAsync()
    {
        SetBusy(true);
        ShowStatus("Waiting for Windows Hello…", isError: false);
        var (ok, error) = await _client!.UnlockWithBiometricsAsync();
        SetBusy(false);

        if (ok) Done();
        else ShowStatus(error ?? "Windows Hello unlock failed.", isError: true);
    }

    private void Done()
    {
        SubheaderText.Text = "You're in.";
        HideAll();
        ShowStatus("Vault unlocked. You can close this window.", isError: false);
        _close?.Invoke();
    }

    private void ShowCode(string caption)
    {
        CodeCaption.Text = caption;
        CodeBox.Text = string.Empty;
        Show(CodePanel);
    }

    private void Show(StackPanel panel)
    {
        HideAll();
        panel.Visibility = Visibility.Visible;
    }

    private void HideAll()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        UnlockPanel.Visibility = Visibility.Collapsed;
        CodePanel.Visibility = Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        Busy.IsActive = busy;
        if (busy) StatusText.Visibility = Visibility.Collapsed;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        StatusText.Visibility = Visibility.Visible;
    }
}
