using System;
using System.Text;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class BitwardenCliServiceVaultEditingTests
{
  private static (BitwardenCliService Service, FakeProcessFactory Factory) CreateService()
  {
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(processFactory: factory.Create);
    return (svc, factory);
  }

  private const string LoginItemJson =
    "{\"id\":\"abc-123\",\"type\":1,\"name\":\"Example\",\"login\":{\"username\":\"u\",\"password\":\"OLDPASS\"}}";

  private static readonly string[] NewPassExpected = ["NEWPASS"];

  // --- GenerateAsync ---

  [Fact]
  public async Task Generate_Password_BuildsExpectedArgs_AndReturnsValue()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Generated!Pass1\n", exitCode: 0));

    var result = await svc.GenerateAsync(GeneratorOptionsProvider.DefaultPassword());

    Assert.Equal("Generated!Pass1", result);
    Assert.Equal(
      "generate --length 20 --uppercase --lowercase --number --special --minNumber 1 --minSpecial 1",
      factory.LastArgs);
  }

  [Fact]
  public async Task Generate_Passphrase_IncludesPassphraseFlags()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "correct-horse-battery\n", exitCode: 0));

    var result = await svc.GenerateAsync(GeneratorOptionsProvider.DefaultPassphrase());

    Assert.Equal("correct-horse-battery", result);
    Assert.Equal("generate --passphrase --words 6 --separator - --includeNumber", factory.LastArgs);
  }

  [Fact]
  public async Task Generate_AvoidAmbiguousFalse_AddsAmbiguousFlag()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "x\n", exitCode: 0));

    await svc.GenerateAsync(GeneratorOptionsProvider.DefaultPassword() with { AvoidAmbiguous = false });

    Assert.Contains("--ambiguous", factory.LastArgs, StringComparison.Ordinal);
  }

  // --- GetItemRawAsync / GetItemAsync ---

  [Fact]
  public async Task GetItemRaw_ReturnsJson()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: LoginItemJson + "\n", exitCode: 0));

    var raw = await svc.GetItemRawAsync("abc-123");

    Assert.NotNull(raw);
    Assert.Contains("OLDPASS", raw, StringComparison.Ordinal);
    Assert.Equal("get item abc-123", factory.LastArgs);
  }

  [Fact]
  public async Task GetItemAsync_ParsesLogin()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: LoginItemJson + "\n", exitCode: 0));

    var item = await svc.GetItemAsync("abc-123");

    Assert.NotNull(item);
    Assert.Equal("Example", item!.Name);
    Assert.Equal(BitwardenItemType.Login, item.Type);
    Assert.Equal("OLDPASS", item.Password);
  }

  // --- EditItemAsync ---

  [Fact]
  public async Task EditItem_EncodesJsonAsBase64Arg()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: LoginItemJson + "\n", exitCode: 0));

    await svc.EditItemAsync("abc-123", LoginItemJson);

    var expectedB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(LoginItemJson));
    Assert.Equal($"edit item abc-123 {expectedB64}", factory.LastArgs);
  }

  // --- SaveItemAsync (persist + verify + refresh) ---

  [Fact]
  public async Task SaveItem_NewValuePresentAfterSync_ReportsSuccess()
  {
    var (svc, factory) = CreateService();
    var edited = LoginItemJson.Replace("OLDPASS", "NEWPASS", StringComparison.Ordinal);
    // 1. edit
    factory.Enqueue(new FakeCliProcess(stdout: edited + "\n", exitCode: 0));
    // 2. sync
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0));
    // 3. verify re-fetch (server now has NEWPASS)
    factory.Enqueue(new FakeCliProcess(stdout: edited + "\n", exitCode: 0));

    var result = await svc.SaveItemAsync("abc-123", edited, NewPassExpected);

    Assert.True(result.Success);
    Assert.Null(result.Error);
    Assert.NotNull(result.Item);
    Assert.Equal("NEWPASS", result.Item!.Password);
  }

  [Fact]
  public async Task SaveItem_ValueMissingAfterSync_ReportsFailure()
  {
    var (svc, factory) = CreateService();
    var edited = LoginItemJson.Replace("OLDPASS", "NEWPASS", StringComparison.Ordinal);
    // 1. edit succeeds
    factory.Enqueue(new FakeCliProcess(stdout: edited + "\n", exitCode: 0));
    // 2. sync succeeds
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0));
    // 3. verify re-fetch still shows OLD value -> persistence not confirmed
    factory.Enqueue(new FakeCliProcess(stdout: LoginItemJson + "\n", exitCode: 0));

    var result = await svc.SaveItemAsync("abc-123", edited, NewPassExpected);

    Assert.False(result.Success);
    Assert.NotNull(result.Error);
  }

  [Fact]
  public async Task SaveItem_EditRejected_ReportsFailureWithoutSync()
  {
    var (svc, factory) = CreateService();
    // edit returns a non-JSON error line -> treated as rejection
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Not found.\n", exitCode: 1));

    var result = await svc.SaveItemAsync("abc-123", LoginItemJson);

    Assert.False(result.Success);
  }

  // --- Pure option/arg building ---

  [Fact]
  public void ToCliArgs_Password_OmitsDisabledClasses()
  {
    var opts = GeneratorOptionsProvider.DefaultPassword() with { Symbols = false, Numbers = false };
    var args = string.Join(' ', opts.ToCliArgs());

    Assert.Contains("--uppercase", args, StringComparison.Ordinal);
    Assert.Contains("--lowercase", args, StringComparison.Ordinal);
    Assert.DoesNotContain("--special", args, StringComparison.Ordinal);
    Assert.DoesNotContain("--number", args, StringComparison.Ordinal);
    Assert.DoesNotContain("--minSpecial", args, StringComparison.Ordinal);
  }

  [Fact]
  public void ToCliArgs_AllClassesOff_ForcesLowercase()
  {
    var opts = GeneratorOptionsProvider.DefaultPassword() with
    {
      Uppercase = false,
      Lowercase = false,
      Numbers = false,
      Symbols = false,
    };
    var args = string.Join(' ', opts.ToCliArgs());

    Assert.Contains("--lowercase", args, StringComparison.Ordinal);
  }

  [Fact]
  public void QuoteArgIfNeeded_QuotesSeparatorWithSpace()
  {
    Assert.Equal("\" \"", BitwardenCliService.QuoteArgIfNeeded(" "));
    Assert.Equal("-", BitwardenCliService.QuoteArgIfNeeded("-"));
  }

  [Fact]
  public void BuildArgString_QuotesPassphraseSeparatorWithSpace()
  {
    var opts = GeneratorOptionsProvider.DefaultPassphrase() with { Separator = " " };
    var args = BitwardenCliService.BuildArgString(opts.ToCliArgs());

    Assert.Contains("--separator \" \"", args, StringComparison.Ordinal);
  }

  // --- Quick Rotate target selection ---

  [Fact]
  public void TrySetSingleHiddenSecret_LoginPassword_SetsIt()
  {
    var item = JsonNode.Parse("{\"login\":{\"password\":\"old\"}}")!.AsObject();

    var ok = BitwardenCliService.TrySetSingleHiddenSecret(item, "NEW", out var error);

    Assert.True(ok);
    Assert.Null(error);
    Assert.Equal("NEW", item["login"]!["password"]!.GetValue<string>());
  }

  [Fact]
  public void TrySetSingleHiddenSecret_SingleHiddenField_SetsIt()
  {
    var item = JsonNode.Parse("{\"fields\":[{\"name\":\"api\",\"value\":\"old\",\"type\":1}]}")!.AsObject();

    var ok = BitwardenCliService.TrySetSingleHiddenSecret(item, "NEW", out var error);

    Assert.True(ok);
    Assert.Null(error);
    Assert.Equal("NEW", item["fields"]![0]!["value"]!.GetValue<string>());
  }

  [Fact]
  public void TrySetSingleHiddenSecret_NoSecret_Fails()
  {
    var item = JsonNode.Parse("{\"login\":{\"username\":\"u\"}}")!.AsObject();

    var ok = BitwardenCliService.TrySetSingleHiddenSecret(item, "NEW", out var error);

    Assert.False(ok);
    Assert.NotNull(error);
  }

  [Fact]
  public void TrySetSingleHiddenSecret_PasswordAndHiddenField_RotatesPassword()
  {
    // The login password is the primary secret: rotate it even when hidden fields also exist.
    var item = JsonNode.Parse("{\"login\":{\"password\":\"p\"},\"fields\":[{\"name\":\"x\",\"value\":\"v\",\"type\":1}]}")!.AsObject();

    var ok = BitwardenCliService.TrySetSingleHiddenSecret(item, "NEW", out var error);

    Assert.True(ok);
    Assert.Null(error);
    Assert.Equal("NEW", item["login"]!["password"]!.GetValue<string>());
    Assert.Equal("v", item["fields"]![0]!["value"]!.GetValue<string>());
  }

  [Fact]
  public void TrySetSingleHiddenSecret_MultipleHiddenFieldsNoPassword_IsAmbiguous()
  {
    var item = JsonNode.Parse("{\"fields\":[{\"name\":\"a\",\"value\":\"1\",\"type\":1},{\"name\":\"b\",\"value\":\"2\",\"type\":1}]}")!.AsObject();

    var ok = BitwardenCliService.TrySetSingleHiddenSecret(item, "NEW", out var error);

    Assert.False(ok);
    Assert.NotNull(error);
  }
}
