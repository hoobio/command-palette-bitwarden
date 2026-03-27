using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class ApiKeyStoreTests
{
    public ApiKeyStoreTests()
    {
        ApiKeyStore.Clear();
    }

    [Fact]
    public void Load_WhenNothingStored_ReturnsNulls()
    {
        var (clientId, clientSecret) = ApiKeyStore.Load();
        Assert.Null(clientId);
        Assert.Null(clientSecret);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        ApiKeyStore.Save("user.test-client-id", "test-secret-abc");
        var (clientId, clientSecret) = ApiKeyStore.Load();
        Assert.Equal("user.test-client-id", clientId);
        Assert.Equal("test-secret-abc", clientSecret);
        ApiKeyStore.Clear();
    }

    [Fact]
    public void Clear_RemovesStoredCredentials()
    {
        ApiKeyStore.Save("user.some-id", "some-secret");
        ApiKeyStore.Clear();
        var (clientId, clientSecret) = ApiKeyStore.Load();
        Assert.Null(clientId);
        Assert.Null(clientSecret);
    }

    [Fact]
    public void Save_Overwrites_PreviousValues()
    {
        ApiKeyStore.Save("user.old-id", "old-secret");
        ApiKeyStore.Save("user.new-id", "new-secret");
        var (clientId, clientSecret) = ApiKeyStore.Load();
        Assert.Equal("user.new-id", clientId);
        Assert.Equal("new-secret", clientSecret);
        ApiKeyStore.Clear();
    }
}
