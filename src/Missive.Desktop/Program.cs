using Microsoft.Extensions.DependencyInjection;
using Missive.Desktop.Bridge;
using Missive.Desktop.Bridge.Workspace;
using Missive.Desktop.Composition;
using Missive.Desktop.Hosting;
using Photino.NET;

namespace Missive.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var indexPath = Path.Combine(wwwroot, "index.html");

        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("Angular assets are missing. Run dotnet build first.", indexPath);
        }

        var assets = new BundledAssets(wwwroot);
        var window = new PhotinoWindow();

        using var services = new ServiceCollection()
            .AddMissive()
            .AddSingleton<IFolderPicker>(new PhotinoFolderPicker(window))
            .BuildServiceProvider();

        var bridge = services.GetRequiredService<PhotinoBridge>();

        window
            .SetTitle("Missive")
            .SetUseOsDefaultSize(false)
            .SetSize(1024, 768)
            .Center()
            .RegisterCustomSchemeHandler(BundledAssets.Scheme, assets.Handle)
            .RegisterWebMessageReceivedHandler(bridge.OnWebMessageReceived)
            .Load(BundledAssets.StartUri)
            .WaitForClose();
    }
}
