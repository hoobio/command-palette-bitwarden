using System.IO;
using System.Reflection;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class BitwardenSettingsManagerTests : IDisposable
{
    private readonly string _tempDir;

    public BitwardenSettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bw_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private BitwardenSettingsManager CreateManager()
    {
        var m = new BitwardenSettingsManager();
        typeof(BitwardenSettingsManager)
            .GetProperty("FilePath", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(m, Path.Combine(_tempDir, $"settings_{Guid.NewGuid():N}.json"));
        return m;
    }

    private static void FireSettingsChanged(BitwardenSettingsManager m)
    {
        var method = typeof(BitwardenSettingsManager).GetMethod(
            "OnSettingsChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(m, [null!, m.Settings]);
    }

    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        var m = CreateManager();
        Assert.Equal("3", m.ContextItemLimit.Value);
        Assert.Equal("static", m.TotpTagStyle.Value);
        Assert.Equal("5", m.BackgroundRefresh.Value);
    }

    [Fact]
    public void Constructor_DefaultCliSettings_AreEmpty()
    {
        var m = CreateManager();
        Assert.Equal("", m.CliDirectoryOverride.Value);
        Assert.Equal("", m.CliDataDirectoryOverride.Value);
    }

    [Fact]
    public void OnSettingsChanged_ClearsSession_WhenRememberSessionDisabled()
    {
        var m = CreateManager();
        SessionStore.Save("test-key");
        Assert.NotNull(SessionStore.Load());

        m.RememberSession.Value = false;
        FireSettingsChanged(m);

        Assert.Null(SessionStore.Load());
    }

    [Fact]
    public void OnSettingsChanged_KeepsSession_WhenRememberSessionEnabled()
    {
        var m = CreateManager();
        SessionStore.Save("test-key");

        m.RememberSession.Value = true;
        FireSettingsChanged(m);

        Assert.NotNull(SessionStore.Load());
        SessionStore.Clear();
    }

    [Fact]
    public void SyncClipboardSettings_PropagatesAutoClear()
    {
        var m = CreateManager();
        m.AutoClearClipboard.Value = false;
        FireSettingsChanged(m);
        Assert.False(SecureClipboardService.AutoClearEnabled);

        m.AutoClearClipboard.Value = true;
        FireSettingsChanged(m);
        Assert.True(SecureClipboardService.AutoClearEnabled);
    }

    [Fact]
    public void SyncClipboardSettings_PropagatesDelay()
    {
        var m = CreateManager();
        m.ClipboardClearDelay.Value = "60";
        FireSettingsChanged(m);
        Assert.Equal(60, SecureClipboardService.ClearDelaySeconds);
    }
}
