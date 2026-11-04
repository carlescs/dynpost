using Missive.Desktop.Bridge.Workspace;
using Photino.NET;

namespace Missive.Desktop.Hosting;

/// <summary>The native folder dialog of the Photino window.</summary>
internal sealed class PhotinoFolderPicker(PhotinoWindow window) : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken)
    {
        var paths = await window.ShowOpenFolderAsync(title: title);
        return paths is { Length: > 0 } ? paths[0] : null;
    }
}
