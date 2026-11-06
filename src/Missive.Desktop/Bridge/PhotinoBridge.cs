using System.Diagnostics;
using Photino.NET;

namespace Missive.Desktop.Bridge;

/// <summary>Connects a <see cref="BridgeRouter"/> to a Photino window's message channel.</summary>
internal sealed class PhotinoBridge(PhotinoWindow window) : IFileDialogs
{
    public void Attach(BridgeRouter router)
    {
        window.RegisterWebMessageReceivedHandler((_, message) =>
        {
            // Messages arrive on the UI thread; do the work elsewhere so the window stays responsive.
            _ = Task.Run(async () =>
            {
                try
                {
                    var response = await router.HandleAsync(message);
                    if (response is not null)
                    {
                        await window.SendWebMessageAsync(response);
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"Bridge message failed: {ex}");
                }
            });
        });
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await window.ShowOpenFolderAsync(title, null!, false);
        return folders is { Length: > 0 } && !string.IsNullOrEmpty(folders[0]) ? folders[0] : null;
    }

    public async Task<string?> PickFileAsync(string title, IReadOnlyList<FileFilter> filters)
    {
        var files = await window.ShowOpenFileAsync(title, null!, false, ToPhotino(filters));
        return files is { Length: > 0 } && !string.IsNullOrEmpty(files[0]) ? files[0] : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, IReadOnlyList<FileFilter> filters)
    {
        var file = await window.ShowSaveFileAsync(title, null!, ToPhotino(filters));
        return string.IsNullOrEmpty(file) ? null : file;
    }

    private static (string Name, string[] Extensions)[] ToPhotino(IReadOnlyList<FileFilter> filters) =>
        filters.Select(filter => (filter.Name, filter.Patterns.ToArray())).ToArray();
}
