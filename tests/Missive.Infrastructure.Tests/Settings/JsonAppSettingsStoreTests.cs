using Missive.Application.Settings;
using Missive.Infrastructure.Settings;
using Missive.Infrastructure.Tests.Git;

namespace Missive.Infrastructure.Tests.Settings;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task MissingFile_LoadsTheDefaults()
    {
        var store = new JsonAppSettingsStore(_temp.Combine("settings.json"));

        Assert.Equal(AppSettings.Default, await store.LoadAsync());
    }

    [Fact]
    public async Task SavedSettings_RoundTripThroughAReadableFile()
    {
        var file = _temp.Combine("nested", "Missive", "settings.json");
        var store = new JsonAppSettingsStore(file);

        var save = await store.SaveAsync(new AppSettings(@"C:\work\my api"));

        Assert.True(save.IsSuccess, save.Error?.Message);
        Assert.Equal(new AppSettings(@"C:\work\my api"), await new JsonAppSettingsStore(file).LoadAsync());
        Assert.Equal("{\n  \"lastWorkspacePath\": \"C:\\\\work\\\\my api\"\n}\n", File.ReadAllText(file).ReplaceLineEndings("\n"));
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public async Task CorruptFile_LoadsTheDefaults()
    {
        var file = _temp.Combine("settings.json");
        File.WriteAllText(file, "{ not json");

        Assert.Equal(AppSettings.Default, await new JsonAppSettingsStore(file).LoadAsync());
    }
}
