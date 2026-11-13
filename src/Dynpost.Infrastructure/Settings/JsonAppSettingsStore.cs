using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dynpost.Application.Common;
using Dynpost.Application.Settings;

namespace Dynpost.Infrastructure.Settings;

/// <summary>
/// Keeps <see cref="AppSettings"/> in a JSON file, written atomically (a temporary file replaces
/// the old one) so a crash can't leave it half-written. Until the file exists, the settings are
/// read from <paramref name="legacyFilePath"/> when given (the file the app kept under its former
/// name); the next save writes them to <paramref name="filePath"/>.
/// </summary>
public sealed class JsonAppSettingsStore(string filePath, string? legacyFilePath = null) : IAppSettingsStore
{
    public const string SaveFailedCode = "settings.save_failed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// The settings file in the user's application data folder (for example
    /// <c>%APPDATA%\Dynpost\settings.json</c> or <c>~/.config/Dynpost/settings.json</c>).
    /// </summary>
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "Dynpost",
        "settings.json");

    /// <summary>The settings file of the app's former name, Missive, read until <see cref="DefaultFilePath"/> exists.</summary>
    public static string LegacyDefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Missive",
        "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnlockedAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<Unit>> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await SaveUnlockedAsync(settings, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<AppSettings>> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var settings = await LoadUnlockedAsync(cancellationToken);
            var updated = update(settings);
            if (updated == settings)
            {
                return updated;
            }

            var saved = await SaveUnlockedAsync(updated, cancellationToken);
            return saved.IsSuccess ? updated : saved.Error!;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<AppSettings> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = File.Exists(filePath) ? filePath : legacyFilePath;
            if (path is null || !File.Exists(path))
            {
                return AppSettings.Default;
            }

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings are not worth failing the app over; start from the defaults.
            return AppSettings.Default;
        }
    }

    private async Task<Result<Unit>> SaveUnlockedAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var temporaryPath = filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            var json = JsonSerializer.Serialize(settings, JsonOptions) + "\n";
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, filePath, overwrite: true);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(SaveFailedCode, $"Could not save the settings to '{filePath}': {ex.Message}");
        }
    }
}
