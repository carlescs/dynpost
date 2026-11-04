using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Missive.Application.Common;
using Missive.Application.Settings;

namespace Missive.Infrastructure.Settings;

/// <summary>
/// Keeps <see cref="AppSettings"/> in a JSON file, written atomically (a temporary file replaces
/// the old one) so a crash can't leave it half-written.
/// </summary>
public sealed class JsonAppSettingsStore(string filePath) : IAppSettingsStore
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
    /// <c>%APPDATA%\Missive\settings.json</c> or <c>~/.config/Missive/settings.json</c>).
    /// </summary>
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "Missive",
        "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                return AppSettings.Default;
            }

            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings are not worth failing the app over; start from the defaults.
            return AppSettings.Default;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<Unit>> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
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
        finally
        {
            _lock.Release();
        }
    }
}
