using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class ServerPickerTests(AvaloniaSession session)
{
  private sealed class FakeExtension
  {
    public List<SetServerRequest> Servers { get; } = [];

    public SetServerResponse Reply { get; set; } = new(true, null);

    public SemaphoreSlim? Gate { get; set; }

    public async Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      if (op != Ops.SetServer)
        throw new IpcException(IpcErrors.Invalid);
      lock (Servers)
        Servers.Add(IpcJson.Read(payload, IpcJson.Default.SetServerRequest));
      if (Gate is not null)
        await Gate.WaitAsync(cancellationToken);
      return IpcJson.ToElement(Reply, IpcJson.Default.SetServerResponse);
    }
  }

  private static WindowCoordinator NewCoordinator(ChannelPair pair)
  {
    var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { ReturnForeground = _ => { }, TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    return coordinator;
  }

  private static readonly SetServerRequest SelfHosted = new(ServerRegions.SelfHosted, "https://vault.hoobi.io", ApiUrl: "https://api.hoobi.io");

  [Theory]
  [InlineData(ServerRegions.Cloud, null, "bitwarden.com")]
  [InlineData(ServerRegions.Eu, null, "bitwarden.eu")]
  [InlineData(ServerRegions.SelfHosted, "https://vault.hoobi.io", "vault.hoobi.io")]
  [InlineData(ServerRegions.SelfHosted, "https://vault.example.com:8443/", "vault.example.com:8443")]
  public Task Picker_ShowsTheCurrentServer_OnBothLoginWindows(string region, string? baseUrl, string expected) => session.RunAsync(async () =>
  {
    using var pair = new ChannelPair(new FakeExtension().HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var server = new SetServerRequest(region, baseUrl);

    var login = coordinator.ShowLogin(server: server);
    var apiKey = coordinator.ShowApiKeyLogin(server: server);

    Assert.Equal(expected, login.ViewModel!.Server.Host);
    Assert.Equal(expected, apiKey.ViewModel!.Server.Host);
    Assert.Equal((region == ServerRegions.Cloud, region == ServerRegions.Eu, region == ServerRegions.SelfHosted), (login.ViewModel.Server.IsCloud, login.ViewModel.Server.IsEu, login.ViewModel.Server.IsSelfHosted));
    login.Close();
    apiKey.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Picker_WithoutAServer_DefaultsToBitwardenCom() => session.RunAsync(async () =>
  {
    using var pair = new ChannelPair(new FakeExtension().HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();

    Assert.Equal("bitwarden.com", window.ViewModel!.Server.Host);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Picker_RendersInsideBothLoginWindows() => session.RunAsync(async () =>
  {
    using var pair = new ChannelPair(new FakeExtension().HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var login = coordinator.ShowLogin(server: SelfHosted);
    var apiKey = coordinator.ShowApiKeyLogin(server: SelfHosted);
    login.UpdateLayout();
    apiKey.UpdateLayout();

    foreach (var window in (Window[])[login, apiKey])
      Assert.Equal("vault.hoobi.io", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ServerText").Text);
    login.Close();
    apiKey.Close();
    await Task.CompletedTask;
  });

  [Theory]
  [InlineData(ServerRegions.Cloud, "bitwarden.com")]
  [InlineData(ServerRegions.Eu, "bitwarden.eu")]
  public Task Choosing_CloudOrEu_SendsTheRequestAtOnce_AndUpdatesTheLabel(string region, string host) => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin(server: SelfHosted);
    var picker = window.ViewModel!.Server;

    await picker.SelectAsync(region);

    Assert.Equal(new SetServerRequest(region, null), extension.Servers.Single());
    Assert.Equal(host, picker.Host);
    Assert.Null(picker.ErrorMessage);
    window.Close();
  });

  [Fact]
  public Task Choosing_CloudOrEu_Failure_ShowsTheErrorAndKeepsTheServer() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Reply = new SetServerResponse(false, "Could not set the server") };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowApiKeyLogin(server: SelfHosted);
    var picker = window.ViewModel!.Server;

    await picker.SelectAsync(ServerRegions.Eu);

    Assert.Equal("Could not set the server", picker.ErrorMessage);
    Assert.Equal("vault.hoobi.io", picker.Host);
    Assert.True(picker.IsSelfHosted);
    window.Close();
  });

  [Fact]
  public Task Choosing_Eu_SwitchesTheLabelAtOnce_AndLocksTheLoginWindowUntilTheReply() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Gate = new SemaphoreSlim(0) };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin(server: SelfHosted);
    var login = window.ViewModel!;
    login.Email = "a@b.c";
    login.MasterPassword = "pw";

    var selecting = login.Server.SelectAsync(ServerRegions.Eu);

    Assert.Equal("bitwarden.eu", login.Server.Host);
    Assert.True(login.Server.IsSwitching);
    Assert.True(login.IsBusy);
    Assert.Equal("Switching to bitwarden.eu...", login.BusyText);
    Assert.False(login.CredentialsEnabled);
    Assert.False(login.SubmitEnabled);
    await login.SubmitAsync();
    Assert.Equal("Switching to bitwarden.eu...", login.BusyText);

    extension.Gate.Release();
    await selecting;

    Assert.False(login.IsBusy);
    Assert.Null(login.BusyText);
    Assert.True(login.SubmitEnabled);
    Assert.Equal("bitwarden.eu", login.Server.Host);
    window.Close();
  });

  [Fact]
  public Task Choosing_Failure_RevertsTheOptimisticLabel_AndClearsTheBusyState() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Reply = new SetServerResponse(false, "Could not set the server"), Gate = new SemaphoreSlim(0) };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowApiKeyLogin(server: SelfHosted);
    var api = window.ViewModel!;

    var selecting = api.Server.SelectAsync(ServerRegions.Eu);
    Assert.Equal("bitwarden.eu", api.Server.Host);
    Assert.True(api.IsBusy);
    Assert.Equal("Switching to bitwarden.eu...", api.BusyText);
    extension.Gate.Release();
    await selecting;

    Assert.Equal("vault.hoobi.io", api.Server.Host);
    Assert.True(api.Server.IsSelfHosted);
    Assert.Equal("Could not set the server", api.Server.ErrorMessage);
    Assert.False(api.IsBusy);
    window.Close();
  });

  [Fact]
  public Task SelfHostedDialog_DisablesSave_WhileSaving_AndReenablesAfterAValidationError() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Reply = new SetServerResponse(false, "bad url"), Gate = new SemaphoreSlim(0) };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin();
    var picker = window.ViewModel!.Server;
    SelfHostedServerViewModel? viewModel = null;
    picker.SelfHostedRequested += d => viewModel = d;
    await picker.SelectAsync(ServerRegions.SelfHosted);
    var dialog = SelfHostedServerView.CreateDialog(viewModel!);

    var saving = viewModel!.SaveAsync();

    Assert.True(viewModel.IsSaving);
    Assert.False(dialog.IsPrimaryButtonEnabled);
    extension.Gate.Release();
    Assert.False(await saving);
    Assert.False(viewModel.IsSaving);
    Assert.True(dialog.IsPrimaryButtonEnabled);
    window.Close();
  });

  [Fact]
  public Task Choosing_SelfHosted_OpensTheDialogPrefilled_AndSendsNothing() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin(server: SelfHosted);
    var picker = window.ViewModel!.Server;
    SelfHostedServerViewModel? dialog = null;
    picker.SelfHostedRequested += d => dialog = d;

    await picker.SelectAsync(ServerRegions.SelfHosted);

    Assert.Equal("https://vault.hoobi.io", dialog!.BaseUrl);
    Assert.Equal(7, dialog.Fields.Count);
    Assert.Equal("https://api.hoobi.io", dialog.Fields.Single(f => f.Label == "API URL").Value);
    Assert.Equal("{base URL}/api", dialog.Fields.Single(f => f.Label == "API URL").Placeholder);
    Assert.True(dialog.Expanded);
    Assert.Empty(extension.Servers);
    window.Close();
  });

  [Fact]
  public Task SelfHostedDialog_WithoutOverrides_StartsCollapsed_AndEmptyForCloud() => session.RunAsync(async () =>
  {
    using var pair = new ChannelPair(new FakeExtension().HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin();
    SelfHostedServerViewModel? dialog = null;
    window.ViewModel!.Server.SelfHostedRequested += d => dialog = d;

    await window.ViewModel.Server.SelectAsync(ServerRegions.SelfHosted);

    Assert.Null(dialog!.BaseUrl);
    Assert.False(dialog.Expanded);
    Assert.All(dialog.Fields, f => Assert.Null(f.Value));
    window.Close();
  });

  [Fact]
  public Task SelfHostedDialog_Save_SendsOneRequestWithTheUrlAndOverrides_AndUpdatesThePicker() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin();
    var picker = window.ViewModel!.Server;
    SelfHostedServerViewModel? dialog = null;
    picker.SelfHostedRequested += d => dialog = d;
    await picker.SelectAsync(ServerRegions.SelfHosted);

    dialog!.BaseUrl = "https://vault.example.com";
    dialog.Fields.Single(f => f.Label == "Identity URL").Value = "https://id.example.com";
    dialog.Fields.Single(f => f.Label == "Icons URL").Value = "   ";
    var saved = await dialog.SaveAsync();

    Assert.True(saved);
    Assert.Equal(new SetServerRequest(ServerRegions.SelfHosted, "https://vault.example.com", IdentityUrl: "https://id.example.com"), extension.Servers.Single());
    Assert.Equal("vault.example.com", picker.Host);
    Assert.True(picker.IsSelfHosted);
    Assert.Null(dialog.ErrorMessage);
    window.Close();
  });

  [Fact]
  public Task SelfHostedDialog_ValidationError_IsShownInline_AndTheServerStays() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Reply = new SetServerResponse(false, "Enter a valid server URL starting with http:// or https://") };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin();
    var picker = window.ViewModel!.Server;
    SelfHostedServerViewModel? dialog = null;
    picker.SelfHostedRequested += d => dialog = d;
    await picker.SelectAsync(ServerRegions.SelfHosted);

    dialog!.BaseUrl = "vault";
    var saved = await dialog.SaveAsync();

    Assert.False(saved);
    Assert.Equal("Enter a valid server URL starting with http:// or https://", dialog.ErrorMessage);
    Assert.Equal("bitwarden.com", picker.Host);
    Assert.Null(picker.ErrorMessage);
    window.Close();
  });

  [Fact]
  public Task SelfHostedDialog_Cancel_SendsNothing_AndThePickerKeepsThePreviousServer() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin(server: new SetServerRequest(ServerRegions.Eu, null));
    var picker = window.ViewModel!.Server;
    SelfHostedServerViewModel? dialog = null;
    picker.SelfHostedRequested += d => dialog = d;

    await picker.SelectAsync(ServerRegions.SelfHosted);
    dialog!.BaseUrl = "https://typed.example.com";

    Assert.Empty(extension.Servers);
    Assert.Equal("bitwarden.eu", picker.Host);
    Assert.True(picker.IsEu);
    window.Close();
  });

  [Fact]
  public Task PickerView_OpensTheSelfHostedDialog_FromTheLoginWindow() => session.RunAsync(async () =>
  {
    using var pair = new ChannelPair(new FakeExtension().HandleAsync);
    using var coordinator = NewCoordinator(pair);
    var window = coordinator.ShowLogin(server: SelfHosted);
    window.Show();
    window.UpdateLayout();
    var view = window.GetVisualDescendants().OfType<ServerPickerView>().Single();

    await window.ViewModel!.Server.SelectAsync(ServerRegions.SelfHosted);

    AvaloniaSession.WaitFor(() => view.OpenDialog is not null);
    Assert.Equal("Self-hosted environment", view.OpenDialog!.Title);
    view.OpenDialog.Hide();
    AvaloniaSession.WaitFor(() => view.OpenDialog is null);
    window.Close();
  });
}
