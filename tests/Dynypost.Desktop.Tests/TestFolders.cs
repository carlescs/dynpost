using Microsoft.Extensions.DependencyInjection;
using Dynypost.Application.Folders;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Desktop.Tests;

internal static class TestFolders
{
    /// <summary>Registers folder inheritance over a store in which no folder sets defaults.</summary>
    public static IServiceCollection AddEmptyFolderDefaults(this IServiceCollection services)
    {
        var store = Substitute.For<IFolderDefaultsStore>();
        store.ReadAsync(Arg.Any<string>(), Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>()).Returns(FolderDefaults.Empty);
        return services.AddSingleton(store).AddSingleton<FolderInheritance>();
    }
}
