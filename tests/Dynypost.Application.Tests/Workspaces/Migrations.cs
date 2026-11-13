using Dynypost.Application.Common;
using Dynypost.Application.History;
using Dynypost.Application.Runs;
using Dynypost.Application.Sharing;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests.Workspaces;

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
