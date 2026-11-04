using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Git;
using Missive.Desktop.Bridge;
using Missive.Desktop.Bridge.Git;
using Missive.Desktop.Bridge.Workspace;
using Missive.Infrastructure.Git;

namespace Missive.Desktop.Composition;

internal static class ServiceCollectionExtensions
{
    /// <summary>Registers the use cases, their adapters and the bridge handlers.</summary>
    public static IServiceCollection AddMissive(this IServiceCollection services)
    {
        // Infrastructure adapters.
        services.AddSingleton<IGitService, GitCliService>();

        // Use cases.
        services.AddTransient<GetGitStatus>();
        services.AddTransient<CommitChanges>();
        services.AddTransient<PullChanges>();
        services.AddTransient<PushChanges>();

        // Bridge.
        services.AddSingleton<IBridgeHandler, PickWorkspaceFolderHandler>();
        services.AddSingleton<IBridgeHandler, GitStatusHandler>();
        services.AddSingleton<IBridgeHandler, GitCommitHandler>();
        services.AddSingleton<IBridgeHandler, GitPullHandler>();
        services.AddSingleton<IBridgeHandler, GitPushHandler>();
        services.AddSingleton<BridgeDispatcher>();
        services.AddSingleton<PhotinoBridge>();

        return services;
    }
}
