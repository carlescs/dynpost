using Dynpost.Application.Http;
using Dynpost.Application.History;
using Dynpost.Application.Requests;
using Dynpost.Application.Runs;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.History;
using Dynpost.Infrastructure.Runs;
using Dynpost.Infrastructure.Sharing;
using Dynpost.Infrastructure.Workspaces;

namespace Dynpost.Infrastructure.Tests.Workspaces;

/// <summary>Moves real workspaces in the old layout to the current one, with the real file stores.</summary>
public sealed class LayoutMigrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dynpost-layout-").FullName;
    private readonly FileSystemWorkspaceLayoutStore _layout = new();
    private readonly FileSystemWorkspaceRepository _repository = new();
    private readonly FileSystemRunPlanStore _plans = new();
    private readonly JsonHistoryStore _history = new();
    private readonly JsonRunStore _runs = new();
    private readonly GitIgnoreLocalOnlyFiles _localFiles = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private MigrateWorkspaceLayout Migration() =>
        new(_layout, _repository, _plans, new YamlRunPlanFormat(), _history, _runs, _localFiles);

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    private void Write(string path, string content)
    {
        var full = Path.Combine([_root, .. path.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string Read(string path) => File.ReadAllText(Path.Combine([_root, .. path.Split('/')]));

    private bool Exists(string path) => Path.Exists(Path.Combine([_root, .. path.Split('/')]));

    private const string PlanText = """
        # nightly smoke
        name: Smoke
        requests:
          - users/get-user.yaml   # the main one
          - 'health.yaml'
          - "users/gone.yaml"
        disabled: [health.yaml]
        weights:
          users/get-user.yaml: 2
        bodies:
          users/get-user.yaml:
            folder: perf/bodies
        assert:
          "*":
            status: 200
          health.yaml:
            maxTime: 1s
        load:
          threads: 1
          iterations: 1
        data:
          file: perf/users.csv

        """;

    [Fact]
    public async Task MovesRequestsAndPlansAndKeepsWhatFollowsThem()
    {
        Write("health.yaml", "name: Health\nmethod: GET\nurl: /health\n");
        Write("users/get-user.yaml", "name: Get user\nurl: /users/1\n");
        Write("users/_folder.yaml", "headers:\n  Accept: application/json\n");
        Write("users/README.md", "notes\n");
        Write("perf/smoke.run.yaml", PlanText);
        Write("perf/users.csv", "id\n1\n");
        Write("perf/bodies/a.json", "{}");
        Write("empty/.gitkeep", "");
        Write("environments/dev.yaml", "name: Dev\n");
        Write("docker-compose.yaml", "services:\n  api:\n    image: x\n");
        Write(".github/workflows/ci.yaml", "name: CI\non: push\n");
        Write("dynpost.yaml", "# my workspace\n");
        _localFiles.Set(_root, P("users/get-user.yaml"), true);
        await _history.AddAsync(_root, P("users/get-user.yaml"), Entry(), new HistoryLimits(10, 1_000_000));
        await _runs.SaveAsync(_root, Run("perf/smoke.run.yaml"));

        var migration = await Migration().ExecuteAsync(_root);

        Assert.NotNull(migration);
        Assert.Equal((3, 1, 0, null), (migration.Requests, migration.Plans, migration.LeftInPlace.Count, migration.Error));

        Assert.True(Exists("requests/health.yaml"));
        Assert.True(Exists("requests/users/get-user.yaml"));
        Assert.True(Exists("requests/users/_folder.yaml"));
        Assert.True(Exists("requests/empty/.gitkeep"));
        Assert.True(Exists("plans/perf/smoke.run.yaml"));
        Assert.False(Exists("health.yaml"));
        Assert.False(Exists("users/get-user.yaml"));
        Assert.False(Exists("empty"));

        // Files that aren't Dynpost's stay where they are, so paths to them keep working.
        Assert.True(Exists("users/README.md"));
        Assert.True(Exists("perf/users.csv"));
        Assert.True(Exists("perf/bodies/a.json"));
        Assert.True(Exists("environments/dev.yaml"));
        Assert.True(Exists("docker-compose.yaml"));
        Assert.True(Exists(".github/workflows/ci.yaml"));

        Assert.Equal("# my workspace\nlayout: 2\n", Read("dynpost.yaml"));
        Assert.Equal(
            PlanText
                .Replace("  - users/get-user.yaml", "  - requests/users/get-user.yaml")
                .Replace("'health.yaml'", "'requests/health.yaml'")
                .Replace("[health.yaml]", "[requests/health.yaml]")
                .Replace("  users/get-user.yaml:", "  requests/users/get-user.yaml:")
                .Replace("  health.yaml:", "  requests/health.yaml:"),
            Read("plans/perf/smoke.run.yaml"));

        Assert.Equal([P("requests/users/get-user.yaml")], _localFiles.List(_root).Value);
        Assert.Single((await _history.ListAsync(_root, P("requests/users/get-user.yaml"))).Value);
        Assert.Single((await _runs.ListAsync(_root, "plans/perf/smoke.run.yaml")).Value);

        var tree = await _repository.ListAsync(_root);
        Assert.Equal(["requests/empty", "requests/users", "requests/health.yaml"], tree.Value.Select(e => e.Path.Value));
        Assert.Equal([P("plans/perf/smoke.run.yaml")], _plans.List(_root).Value);
    }

    [Fact]
    public async Task RunsOnlyOnce()
    {
        Write("health.yaml", "name: Health\nmethod: GET\n");
        Assert.NotNull(await Migration().ExecuteAsync(_root));

        Write("stray.yaml", "name: Stray\nmethod: GET\n");

        Assert.Null(await Migration().ExecuteAsync(_root));
        Assert.True(Exists("stray.yaml"));
    }

    [Fact]
    public async Task MarksAWorkspaceWithNothingToMove_WithoutMovingEmptyFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Write("requests/users/get-user.yaml", "name: Get user\n");

        Assert.Null(await Migration().ExecuteAsync(_root));

        Assert.Equal("layout: 2\n", Read("dynpost.yaml"));
        Assert.True(Exists("src"));
        Assert.False(Exists("requests/src"));
    }

    [Fact]
    public async Task NeverOverwritesAndLeavesClashingFilesInPlace()
    {
        Write("requests/users/get-user.yaml", "name: New\n");
        Write("users/get-user.yaml", "name: Old\nmethod: GET\n");
        Write("users/list-users.yaml", "name: List\nmethod: GET\n");
        Write("requests/smoke.run.yaml", "name: Smoke\n");

        var migration = await Migration().ExecuteAsync(_root);

        Assert.NotNull(migration);
        Assert.Equal((1, 1), (migration.Requests, migration.Plans));
        Assert.Contains(migration.LeftInPlace, item => item.StartsWith("users/get-user.yaml", StringComparison.Ordinal));
        Assert.Equal("name: New\n", Read("requests/users/get-user.yaml"));
        Assert.Equal("name: Old\nmethod: GET\n", Read("users/get-user.yaml"));
        Assert.True(Exists("requests/users/list-users.yaml"));
        Assert.True(Exists("plans/smoke.run.yaml"));
    }

    [Fact]
    public async Task FoldersThatOnlyHeldPlansGoWithThePlans()
    {
        Write("perf/smoke.run.yaml", "name: Smoke\n");
        Directory.CreateDirectory(Path.Combine(_root, "perf", "nightly"));

        await Migration().ExecuteAsync(_root);

        Assert.True(Exists("plans/perf/smoke.run.yaml"));
        Assert.True(Exists("plans/perf/nightly"));
        Assert.False(Exists("requests/perf"));
        Assert.False(Exists("perf"));
    }

    [Fact]
    public async Task LayoutStore_ReadsAndUpdatesTheVersion()
    {
        Assert.Equal(1, (await _layout.ReadVersionAsync(_root)).Value);

        Write("dynpost.yaml", "name: api\nlayout: 1\nother: x");
        Assert.Equal(1, (await _layout.ReadVersionAsync(_root)).Value);

        await _layout.WriteVersionAsync(_root, 2);

        Assert.Equal("name: api\nlayout: 2\nother: x", Read("dynpost.yaml"));
        Assert.Equal(2, (await _layout.ReadVersionAsync(_root)).Value);
    }

    [Fact]
    public async Task LayoutStore_FindsOnlyDynpostFilesOutsideTheirFolders()
    {
        Write("health.yaml", "name: Health\nmethod: GET\nurl: /health\n");
        Write("blank.yaml", "name: Blank\n");
        Write("custom.yaml", "method: GET\nx-team: core\n");
        Write("openapi.yaml", "openapi: 3.0.0\ninfo:\n  title: x\n");
        Write("compose.yaml", "name: app\nservices: {}\n");
        Write("broken.yaml", "name: [oops");
        Write("dynpost.yaml", "name: api\n");
        Write("users/_folder.yaml", "headers: {}\n");
        Write("smoke.run.yaml", "name: Smoke\n");
        Write("requests/ok.yaml", "name: Ok\n");
        Write("requests/load.run.yaml", "name: Load\n");
        Write("plans/ok.run.yaml", "name: Ok\n");
        Write("plans/stray.yaml", "name: Stray\nmethod: GET\n");
        Write("environments/dev.yaml", "name: Dev\n");
        Write(".hidden/x.yaml", "name: X\nmethod: GET\n");
        Write("docs/notes.md", "x");
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var scan = (await _layout.ScanLegacyAsync(_root)).Value;

        Assert.Equal(
            ["blank.yaml", "custom.yaml", "health.yaml", "plans/stray.yaml", "users/_folder.yaml"],
            scan.Requests.Select(p => p.Value).Order(StringComparer.Ordinal));
        Assert.Equal(["requests/load.run.yaml", "smoke.run.yaml"], scan.RunPlans.Select(p => p.Value).Order(StringComparer.Ordinal));
        Assert.Equal(["empty"], scan.EmptyFolders.Select(p => p.Value));
    }

    private static HistoryEntryDetailsDto Entry() => new(
        new HistoryEntryDto("20261107T120000000-00000001", DateTimeOffset.UnixEpoch, "GET", "https://x", 200, "OK", 1, 2, "application/json"),
        new ResponseDto(200, "OK", [new KeyValueDto("Content-Type", "application/json")], "application/json", "{}", "text", 2, false, 1));

    private static RunResultDto Run(string plan)
    {
        var stats = new StatsDto(1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, new Dictionary<string, long> { ["200"] = 1 }, new Dictionary<string, long>());
        return new RunResultDto("20260101-000000-abc123", plan, "Smoke", "completed", DateTimeOffset.UnixEpoch, 1000, 1, 0, stats, [], [], [], true, [], [], null);
    }

    [Fact]
    public async Task RenamesTheFilesOfTheFormerName_AndTheirGitIgnoreLine()
    {
        Write("missive.yaml", "layout: 2\n");
        Write(".missive/history/a.json", "{}");
        Write(".gitignore", "bin/\r\n.missive/\r\n");

        await Migration().ExecuteAsync(_root);

        Assert.False(Exists("missive.yaml"));
        Assert.Equal("layout: 2\n", Read("dynpost.yaml"));
        Assert.False(Exists(".missive"));
        Assert.Equal("{}", Read(".dynpost/history/a.json"));
        Assert.Equal("bin/\r\n.dynpost/\r\n", Read(".gitignore"));
    }

    [Fact]
    public async Task KeepsFilesOfTheFormerName_WhenTheNewNamesAreTaken()
    {
        Write("missive.yaml", "layout: 1\n");
        Write("dynpost.yaml", "layout: 2\n");
        Write(".missive/state.yaml", "old");
        Write(".dynpost/state.yaml", "new");

        await Migration().ExecuteAsync(_root);

        Assert.Equal("layout: 1\n", Read("missive.yaml"));
        Assert.Equal("layout: 2\n", Read("dynpost.yaml"));
        Assert.Equal("old", Read(".missive/state.yaml"));
        Assert.Equal("new", Read(".dynpost/state.yaml"));
    }
}
