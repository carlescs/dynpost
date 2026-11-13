using Microsoft.Extensions.DependencyInjection;
using Dynpost.Application.Captures;
using Dynpost.Application.Cookies;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Git;
using Dynpost.Application.History;
using Dynpost.Application.Http;
using Dynpost.Application.Interchange;
using Dynpost.Application.OAuth2;
using Dynpost.Application.Requests;
using Dynpost.Application.Runs;
using Dynpost.Application.Settings;
using Dynpost.Application.Sharing;
using Dynpost.Application.About;
using Dynpost.Application.Locale;
using Dynpost.Application.Mcp;
using Dynpost.Application.Updates;
using Dynpost.Application.Workspaces;
using Dynpost.Desktop.Bridge;
using Dynpost.Desktop.Hosting;
using Dynpost.Desktop.Mcp;
using Dynpost.Infrastructure.Cookies;
using Dynpost.Infrastructure.Environments;
using Dynpost.Infrastructure.Git;
using Dynpost.Infrastructure.History;
using Dynpost.Infrastructure.Http;
using Dynpost.Infrastructure.Interchange;
using Dynpost.Infrastructure.OAuth2;
using Dynpost.Infrastructure.Runs;
using Dynpost.Infrastructure.Settings;
using Dynpost.Infrastructure.Sharing;
using Dynpost.Infrastructure.About;
using Dynpost.Infrastructure.Locale;
using Dynpost.Infrastructure.Updates;
using Dynpost.Infrastructure.Workspaces;
using Dynpost.Infrastructure.Yaml;
using Photino.NET;
using Velopack;

namespace Dynpost.Desktop;

internal static class Program
{
    /// <summary>The GitHub repository whose releases the app updates itself from.</summary>
    private const string ReleasesRepository = "https://github.com/carlescs/dynpost";

    [STAThread]
    private static void Main()
    {
        // Must run first: Velopack's installer and updater start the app with special arguments,
        // and vpk checks that Main calls this directly.
        VelopackApp.Build().Run();

        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var indexPath = Path.Combine(wwwroot, "index.html");

        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("Angular assets are missing. Run dotnet build first.", indexPath);
        }

        using var services = ConfigureServices();
        var assets = new BundledAssets(wwwroot);

        var window = new PhotinoWindow()
            .SetTitle("Dynpost")
            .SetUseOsDefaultSize(false)
            .SetSize(1024, 768)
            .Center()
            .RegisterCustomSchemeHandler(BundledAssets.Scheme, assets.Handle);

        // Windows title bars need an .ico; GTK loads a PNG reliably. macOS takes the
        // icon from the app bundle instead.
        var iconPath = Path.Combine(
            AppContext.BaseDirectory, "Assets", OperatingSystem.IsWindows() ? "dynpost.ico" : "dynpost.png");
        if (!OperatingSystem.IsMacOS() && File.Exists(iconPath))
        {
            window.SetIconFile(iconPath);
        }

        var bridge = new PhotinoBridge(window);
        var events = services.GetRequiredService<HostEvents>();
        bridge.Attach(BridgeHandlers.Create(services, bridge, bridge), events);
        // Keep the source control view current as files and git state change, whoever changes them.
        services.GetRequiredService<WatchWorkingTree>().Start(() => _ = events.GitChangedAsync());
        // Off unless the user turned it on; a failure to start is shown in its settings.
        _ = services.GetRequiredService<StartMcpServer>().ExecuteAsync();

        window.Load(BundledAssets.StartUri).WaitForClose();
    }

    private static ServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddSingleton<CurrentWorkspace>()
            .AddSingleton<IWorkspaceRepository, FileSystemWorkspaceRepository>()
            .AddSingleton<IFolderDefaultsStore, YamlFolderDefaultsStore>()
            .AddSingleton<FolderInheritance>()
            .AddSingleton<IWorkspaceLayoutStore, FileSystemWorkspaceLayoutStore>()
            .AddSingleton<IEnvironmentStore, FileSystemEnvironmentStore>()
            .AddSingleton<ISecretStore, YamlSecretStore>()
            .AddSingleton<IWorkspaceStateStore, YamlWorkspaceStateStore>()
            .AddSingleton<ICookieJarStore, JsonCookieJarStore>()
            .AddSingleton<ICapturedValueStore, YamlCapturedValueStore>()
            .AddSingleton<IOAuth2TokenStore, JsonOAuth2TokenStore>()
            .AddSingleton<IAuthorizationBrowser, LoopbackAuthorizationBrowser>()
            .AddSingleton<OAuth2Tokens>()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IGitService, GitCliService>()
            .AddSingleton<IWorkingTreeWatcher, FileSystemWorkingTreeWatcher>()
            .AddSingleton<WatchWorkingTree>()
            .AddSingleton<ILocalOnlyFiles, GitIgnoreLocalOnlyFiles>()
            .AddSingleton<IRequestFileFormat, YamlRequestFileFormat>()
            .AddSingleton<IHttpSender, HttpClientSender>()
            .AddSingleton<InFlightRequests>()
            .AddSingleton<ResponseBodies>()
            .AddSingleton<IHistoryStore, JsonHistoryStore>()
            .AddSingleton<ResponseHistory>()
            .AddSingleton<IRunPlanStore, FileSystemRunPlanStore>()
            .AddSingleton<IRunPlanFormat, YamlRunPlanFormat>()
            .AddSingleton<IRunDataReader, CsvRunDataReader>()
            .AddSingleton<IRunBodyReader, FolderRunBodyReader>()
            .AddSingleton<IRunStore, JsonRunStore>()
            .AddSingleton<RunRegistry>()
            .AddSingleton<IExternalFiles, LocalExternalFiles>()
            .AddSingleton<ICollectionImporter, CollectionImporter>()
            .AddSingleton<ICollectionExporter, PostmanCollectionExporter>()
            .AddSingleton<ICollectionExporter, InsomniaExporter>()
            .AddSingleton<IAppSettingsStore>(new JsonAppSettingsStore(JsonAppSettingsStore.DefaultFilePath, JsonAppSettingsStore.LegacyDefaultFilePath))
            .AddSingleton<IAppUpdater>(new VelopackAppUpdater(ReleasesRepository))
            .AddSingleton<IAppInfo, AssemblyAppInfo>()
            .AddSingleton<IRegionalSettings, SystemRegionalSettings>()
            .AddSingleton<IFileManager, SystemFileManager>()
            .AddSingleton<HostEvents>()
            .AddSingleton<IMcpServer, McpHttpServer>()
            .AddTransient<GetMcpStatus>()
            .AddTransient<SetMcpServer>()
            .AddTransient<RegenerateMcpToken>()
            .AddTransient<StartMcpServer>()
            .AddTransient<GetAboutInfo>()
            .AddTransient<GetDateFormat>()
            .AddTransient<GetAppSettings>()
            .AddTransient<SetTheme>()
            .AddTransient<SetHistorySettings>()
            .AddTransient<SetActivityOrder>()
            .AddTransient<ListResponseHistory>()
            .AddTransient<GetResponseHistoryEntry>()
            .AddTransient<DeleteResponseHistory>()
            .AddTransient<CheckForUpdate>()
            .AddTransient<RestartToUpdate>()
            .AddTransient<MigrateWorkspaceLayout>()
            .AddTransient<OpenWorkspace>()
            .AddTransient<OpenLastWorkspace>()
            .AddTransient<GetRecentWorkspaces>()
            .AddTransient<RemoveRecentWorkspace>()
            .AddTransient<GetWorkspaceTree>()
            .AddTransient<FindWorkspaceItem>()
            .AddTransient<ShowWorkspaceFolder>()
            .AddTransient<ExportWorkspace>()
            .AddTransient<ImportCollection>()
            .AddTransient<LoadRequest>()
            .AddTransient<InspectRequestFiles>()
            .AddTransient<CreateRequest>()
            .AddTransient<SaveRequest>()
            .AddTransient<MoveRequest>()
            .AddTransient<DuplicateRequest>()
            .AddTransient<DeleteRequest>()
            .AddTransient<SetLocalOnly>()
            .AddTransient<ActiveEnvironment>()
            .AddTransient<SendRequest>()
            .AddTransient<SendRequestFile>()
            .AddTransient<CancelRequest>()
            .AddTransient<SaveResponseBody>()
            .AddTransient<ListCookies>()
            .AddTransient<DeleteCookie>()
            .AddTransient<ClearCookies>()
            .AddTransient<ListCapturedValues>()
            .AddTransient<ClearCapturedValues>()
            .AddTransient<GetOAuth2Token>()
            .AddTransient<GetOAuth2TokenStatus>()
            .AddTransient<ClearOAuth2Token>()
            .AddTransient<CancelOAuth2SignIn>()
            .AddTransient<CreateFolder>()
            .AddTransient<RenameFolder>()
            .AddTransient<MoveFolder>()
            .AddTransient<DuplicateFolder>()
            .AddTransient<DeleteFolder>()
            .AddTransient<LoadFolderSettings>()
            .AddTransient<SaveFolderSettings>()
            .AddTransient<GetInheritedSettings>()
            .AddTransient<ListEnvironments>()
            .AddTransient<LoadEnvironment>()
            .AddTransient<CreateEnvironment>()
            .AddTransient<SaveEnvironment>()
            .AddTransient<DeleteEnvironment>()
            .AddTransient<SelectEnvironment>()
            .AddTransient<ListRunPlans>()
            .AddTransient<OpenRunPlan>()
            .AddTransient<CreateRunPlan>()
            .AddTransient<SaveRunPlan>()
            .AddTransient<DeleteRunPlan>()
            .AddTransient<MoveRunPlan>()
            .AddTransient<ListRunFolders>()
            .AddTransient<RenameRunFolder>()
            .AddTransient<MoveRunFolder>()
            .AddTransient<DuplicateRunFolder>()
            .AddTransient<DeleteRunFolder>()
            .AddTransient<StartRun>()
            .AddTransient<CancelRun>()
            .AddTransient<GetRunProgress>()
            .AddTransient<WaitForRun>()
            .AddTransient<GetRunSamples>()
            .AddTransient<GetRunResponse>()
            .AddTransient<ListRuns>()
            .AddTransient<DeleteRun>()
            .AddTransient<ClearRuns>()
            .AddTransient<GetGitStatus>()
            .AddTransient<InitRepository>()
            .AddTransient<CommitChanges>()
            .AddTransient<DiscardChanges>()
            .AddTransient<PullChanges>()
            .AddTransient<PushChanges>()
            .AddTransient<FetchChanges>()
            .AddTransient<GetGitIdentity>()
            .AddTransient<SetGitIdentity>()
            .AddTransient<GetConflict>()
            .AddTransient<ResolveConflict>()
            .AddTransient<AbortMerge>()
            .AddTransient<GetGitHistory>()
            .AddTransient<GetCommit>()
            .AddTransient<GetCommitFile>()
            .AddTransient<GetFileDiff>()
            .AddTransient<ListBranches>()
            .AddTransient<SwitchBranch>()
            .AddTransient<CreateBranch>()
            .AddTransient<CheckoutRemoteBranch>()
            .AddTransient<DeleteBranch>()
            .AddTransient<DeleteRemoteBranch>()
            .AddTransient<MergeBranch>()
            .AddTransient<ListRemotes>()
            .AddTransient<AddRemote>()
            .AddTransient<UpdateRemote>()
            .AddTransient<RemoveRemote>()
            .BuildServiceProvider();
}
