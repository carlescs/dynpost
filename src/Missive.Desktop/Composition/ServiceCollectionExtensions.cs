using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Git;
using Missive.Application.Settings;
using Missive.Application.Workspace;
using Missive.Desktop.Bridge;
using Missive.Desktop.Bridge.Git;
using Missive.Desktop.Bridge.Workspace;
using Missive.Infrastructure.Git;
using Missive.Infrastructure.Settings;
using Missive.Infrastructure.Workspace;

namespace Missive.Desktop.Composition;

internal static class ServiceCollectionExtensions
{
    /// <summary>Registers the use cases, their adapters and the bridge handlers.</summary>
    public static IServiceCollection AddMissive(this IServiceCollection services)
    {
        // Infrastructure adapters.
        services.AddSingleton<IGitService, GitCliService>();
        services.AddSingleton<IWorkspaceRepository, FileSystemWorkspaceRepository>();
        services.AddSingleton<IAppSettingsStore>(new JsonAppSettingsStore(JsonAppSettingsStore.DefaultFilePath));

        // Use cases.
        services.AddTransient<OpenWorkspace>();
        services.AddTransient<GetLastWorkspace>();
        services.AddTransient<GetGitStatus>();
        services.AddTransient<CommitChanges>();
        services.AddTransient<PullChanges>();
        services.AddTransient<PushChanges>();

        // Bridge.
        services.AddSingleton<IBridgeHandler, PickWorkspaceFolderHandler>();
        services.AddSingleton<IBridgeHandler, OpenWorkspaceHandler>();
        services.AddSingleton<IBridgeHandler, GetLastWorkspaceHandler>();
        services.AddSingleton<IBridgeHandler, GitStatusHandler>();
        services.AddSingleton<IBridgeHandler, GitCommitHandler>();
        services.AddSingleton<IBridgeHandler, GitPullHandler>();
        services.AddSingleton<IBridgeHandler, GitPushHandler>();
        services.AddSingleton<BridgeDispatcher>();
        services.AddSingleton<PhotinoBridge>();

        return services;
    }
}
