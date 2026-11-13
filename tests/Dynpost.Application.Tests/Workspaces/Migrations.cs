using Dynpost.Application.Common;
using Dynpost.Application.History;
using Dynpost.Application.Runs;
using Dynpost.Application.Sharing;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Workspaces;

internal static class Migrations
{
    /// <summary>A migration that finds every workspace already in the current layout.</summary>
    public static MigrateWorkspaceLayout UpToDate()
    {
        var layout = Substitute.For<IWorkspaceLayoutStore>();
        layout.ReadVersionAsync(default!, default).ReturnsForAnyArgs(Result<int>.Success(WorkspaceLayout.CurrentLayoutVersion));
        return new MigrateWorkspaceLayout(
            layout,
            Substitute.For<IWorkspaceRepository>(),
            Substitute.For<IRunPlanStore>(),
            Substitute.For<IRunPlanFormat>(),
            Substitute.For<IHistoryStore>(),
            Substitute.For<IRunStore>(),
            Substitute.For<ILocalOnlyFiles>());
    }
}
