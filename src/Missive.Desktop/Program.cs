using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Folders;
using Missive.Application.Git;
using Missive.Application.Http;
using Missive.Application.Requests;
using Missive.Application.Settings;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Desktop.Hosting;
using Missive.Infrastructure.Git;
using Missive.Infrastructure.Http;
using Missive.Infrastructure.Settings;
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
            .AddSingleton<IGitService, GitCliService>()
            .AddSingleton<IHttpSender, HttpClientSender>()
            .AddSingleton<InFlightRequests>()
            .AddSingleton<IAppSettingsStore>(new JsonAppSettingsStore(JsonAppSettingsStore.DefaultFilePath))
            .AddTransient<OpenWorkspace>()
            .AddTransient<OpenLastWorkspace>()
            .AddTransient<GetWorkspaceTree>()
            .AddTransient<LoadRequest>()
            .AddTransient<CreateRequest>()
            .AddTransient<SaveRequest>()
            .AddTransient<MoveRequest>()
            .AddTransient<SendRequest>()
            .AddTransient<CancelRequest>()
            .AddTransient<CreateFolder>()
            .AddTransient<RenameFolder>()
            .AddTransient<MoveFolder>()
            .AddTransient<DeleteFolder>()
            .AddTransient<GetGitStatus>()
            .AddTransient<CommitChanges>()
            .AddTransient<PullChanges>()
            .AddTransient<PushChanges>()
            .BuildServiceProvider();
}
