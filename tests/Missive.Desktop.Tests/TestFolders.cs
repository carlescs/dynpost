using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Folders;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Desktop.Tests;

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
