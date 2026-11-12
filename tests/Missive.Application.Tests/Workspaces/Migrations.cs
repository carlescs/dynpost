using Missive.Application.Common;
using Missive.Application.History;
using Missive.Application.Runs;
using Missive.Application.Sharing;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests.Workspaces;

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
