using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Desktop.Hosting;
using Missive.Infrastructure.Workspaces;
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

        using var services = ConfigureServices();
        var assets = new BundledAssets(wwwroot);

        var window = new PhotinoWindow()
            .SetTitle("Missive")
            .SetUseOsDefaultSize(false)
            .SetSize(1024, 768)
            .Center()
            .RegisterCustomSchemeHandler(BundledAssets.Scheme, assets.Handle);

        var bridge = new PhotinoBridge(window);
        bridge.Attach(BridgeHandlers.Create(services, bridge));

        window.Load(BundledAssets.StartUri).WaitForClose();
    }

    private static ServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddSingleton<CurrentWorkspace>()
            .AddSingleton<IWorkspaceRepository, FileSystemWorkspaceRepository>()
            .AddTransient<OpenWorkspace>()
            .AddTransient<GetWorkspaceTree>()
            .AddTransient<LoadRequest>()
            .AddTransient<CreateRequest>()
            .AddTransient<SaveRequest>()
            .BuildServiceProvider();
}
