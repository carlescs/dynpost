using Dynypost.Application.Runs;
using Dynypost.Application.Common;
using Dynypost.Domain.Runs;
using Dynypost.Domain.Workspaces;
using Dynypost.Infrastructure.Runs;

namespace Dynypost.Infrastructure.Tests.Runs;

public sealed class RunInfrastructureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dynypost-runs-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Yaml = """
        name: Users smoke load
        requests:
          - users/get-user.yaml
          - users/create-user.yaml
        load:
          threads: 10
          iterations: 50
          rampUp: 10s
          thinkTime: { min: 100ms, max: 500ms }
          maxRps: 100
        data:
          file: perf/users.csv
          mode: perThread
          onEof: stop
        assert:
          "*":
            status: 2xx
          users/create-user.yaml:
            status: [201]
            maxTime: 800ms
            bodyContains: '"id"'
        thresholds:
          p95: 500ms
          errorRate: 1%
        """;

    [Fact]
    public void Parse_ReadsTheWholePlan()
    {
        var result = new YamlRunPlanFormat().Parse(Yaml);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var plan = result.Value;
        Assert.Equal("Users smoke load", plan.Name);
        Assert.Equal(["users/get-user.yaml", "users/create-user.yaml"], plan.Requests.Select(r => r.Value));
        Assert.Equal(10, plan.Load.Threads);
        Assert.Equal(50, plan.Load.Iterations);
        Assert.Equal(TimeSpan.FromSeconds(10), plan.Load.RampUp);
        Assert.Equal(TimeSpan.FromMilliseconds(100), plan.Load.ThinkMin);
        Assert.Equal(TimeSpan.FromMilliseconds(500), plan.Load.ThinkMax);
        Assert.Equal(100, plan.Load.MaxRps);
        Assert.Equal(new RunDataSource(WorkspacePath.Parse("perf/users.csv"), SelectionMode.PerThread, EndBehavior.Stop), plan.Data);
        Assert.Equal(["2xx"], plan.Assertions["*"].Status);
        Assert.Equal(["201"], plan.Assertions["users/create-user.yaml"].Status);
        Assert.Equal("\"id\"", plan.Assertions["users/create-user.yaml"].BodyContains);
        Assert.Equal(TimeSpan.FromMilliseconds(500), plan.Thresholds.P95);
        Assert.Equal(1, plan.Thresholds.ErrorRatePercent);
        Assert.Empty(plan.Validate());
    }

    [Fact]
    public void Parse_ReadsAFixedThinkTimeAndDuration()
    {
        var result = new YamlRunPlanFormat().Parse("name: x\nrequests: [a.yaml]\nload:\n  threads: 1\n  duration: 1m\n  thinkTime: 200ms\n");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(TimeSpan.FromMinutes(1), result.Value.Load.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(200), result.Value.Load.ThinkMin);
        Assert.Equal(TimeSpan.FromMilliseconds(200), result.Value.Load.ThinkMax);
    }

    [Fact]
    public void Parse_ReadsTheDisabledRequests()
    {
        var result = new YamlRunPlanFormat().Parse(
            "name: x\nrequests: [a.yaml, b.yaml]\ndisabled: [b.yaml, b.yaml]\nload: {threads: 1, iterations: 1}\n");

        Assert.True(result.IsSuccess);
        Assert.Equal(["b.yaml"], result.Value.DisabledRequests);
        Assert.Equal(["a.yaml"], result.Value.EnabledRequests.Select(r => r.Value));
    }

    [Fact]
    public void Parse_ReadsTheOrderAndWeights()
    {
        var result = new YamlRunPlanFormat().Parse(
            "name: x\nrequests: [a.yaml, b.yaml]\norder: random\nweights:\n  a.yaml: 3\n  b.yaml: 0.5\nload: {threads: 1, iterations: 1}\n");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(RequestOrder.Random, result.Value.Order);
        Assert.Equal(3, result.Value.RequestWeights["a.yaml"]);
        Assert.Equal(0.5, result.Value.RequestWeights["b.yaml"]);
        Assert.Equal(RequestOrder.RoundRobin, new YamlRunPlanFormat().Parse("name: x\nrequests: [a.yaml]\norder: roundRobin\nload: {threads: 1, iterations: 1}\n").Value.Order);
        Assert.Equal(RequestOrder.Sequential, new YamlRunPlanFormat().Parse("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\n").Value.Order);
    }

    [Fact]
    public void Parse_ReadsBodyFolders()
    {
        var result = new YamlRunPlanFormat().Parse("""
            name: x
            requests: [users/create.yaml, users/update.yaml]
            load: {threads: 1, iterations: 1}
            bodies:
              users/create.yaml:
                folder: perf/bodies/create
                pattern: "*.json"
                mode: perThread
                onEof: stop
              users/update.yaml:
                folder: perf/bodies/update
            """);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(
            new RunBodySource(WorkspacePath.Parse("perf/bodies/create"), "*.json", SelectionMode.PerThread, EndBehavior.Stop),
            result.Value.BodySources["users/create.yaml"]);
        Assert.Equal(
            new RunBodySource(WorkspacePath.Parse("perf/bodies/update"), null, SelectionMode.Sequential, EndBehavior.Recycle),
            result.Value.BodySources["users/update.yaml"]);
        Assert.Empty(result.Value.Validate());
    }

    [Theory]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\nbodies:\n  a.yaml: perf\n", "bodies.a.yaml")]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\nbodies:\n  a.yaml: {folder: ../x}\n", "bodies.a.yaml.folder")]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\nbodies:\n  a.yaml: {folder: x, pattern: a/*.json}\n", "bodies.a.yaml.pattern")]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\nbodies:\n  a.yaml: {folder: x, mode: shuffle}\n", "bodies.a.yaml.mode")]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\nbodies:\n  a.yaml: {folder: x, onEof: never}\n", "bodies.a.yaml.onEof")]
    public void Parse_ReportsBodyFolderProblems(string yaml, string expected)
    {
        var result = new YamlRunPlanFormat().Parse(yaml);

        Assert.False(result.IsSuccess);
        Assert.Contains(expected, result.Error!.Message);
    }

    [Fact]
    public async Task BodyReader_ReadsMatchingFilesInOrdinalOrder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "perf", "bodies")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "b1.json"), "{\"n\":\"b\"}");
        await File.WriteAllTextAsync(Path.Combine(folder, "B2.json"), "{\"n\":\"B\"}");
        await File.WriteAllTextAsync(Path.Combine(folder, "a.JSON"), "\uFEFF{\"n\":\"a\"}");
        await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "skip me");
        await File.WriteAllTextAsync(Path.Combine(folder, ".hidden.json"), "skip me");
        Directory.CreateDirectory(Path.Combine(folder, "nested.json"));

        var files = await new FolderRunBodyReader().ReadFolderAsync(_root, WorkspacePath.Parse("perf/bodies"), "*.json", readText: true);

        Assert.True(files.IsSuccess, files.Error?.Message);
        Assert.Equal(["perf/bodies/B2.json", "perf/bodies/a.JSON", "perf/bodies/b1.json"], files.Value.Select(f => f.Path.Value));
        Assert.Equal(["{\"n\":\"B\"}", "{\"n\":\"a\"}", "{\"n\":\"b\"}"], files.Value.Select(f => f.Text));
    }

    [Fact]
    public async Task BodyReader_ListsBinaryFilesWithoutReadingThem()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "blobs")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(folder, "1.bin"), [0xff, 0xfe, 0x00]);

        var files = await new FolderRunBodyReader().ReadFolderAsync(_root, WorkspacePath.Parse("blobs"), null, readText: false);

        Assert.True(files.IsSuccess, files.Error?.Message);
        Assert.Null(Assert.Single(files.Value).Text);
    }

    [Fact]
    public async Task BodyReader_ReportsMissingEmptyAndNonTextFolders()
    {
        var reader = new FolderRunBodyReader();
        var folder = Directory.CreateDirectory(Path.Combine(_root, "bodies")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "a");

        Assert.Contains("does not exist", (await reader.ReadFolderAsync(_root, WorkspacePath.Parse("missing"), null, true)).Error!.Message);
        Assert.Contains("no files that match '*.json'", (await reader.ReadFolderAsync(_root, WorkspacePath.Parse("bodies"), "*.json", true)).Error!.Message);

        await File.WriteAllBytesAsync(Path.Combine(folder, "b.txt"), [0xc3, 0x28]);
        var invalid = await reader.ReadFolderAsync(_root, WorkspacePath.Parse("bodies"), null, true);
        Assert.Equal(ErrorCodes.InvalidFile, invalid.Error!.Code);
        Assert.Contains("'bodies/b.txt' is not UTF-8 text", invalid.Error.Message);
    }

    [Fact]
    public void Parse_ReadsWhetherToSaveResponses()
    {
        var format = new YamlRunPlanFormat();

        Assert.True(format.Parse("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\nsaveResponses: true\n").Value.SaveResponses);
        Assert.False(format.Parse("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\n").Value.SaveResponses);
    }

    [Theory]
    [InlineData("name: x\nrequests: [a.yaml]\norder: backwards\nload: {threads: 1, iterations: 1}\n", "'order'")]
    [InlineData("name: x\nrequests: [a.yaml]\nweights: {a.yaml: lots}\nload: {threads: 1, iterations: 1}\n", "weights.a.yaml")]
    [InlineData("name: x\nrequests: [a.yaml]\n", "'load' is required")]
    [InlineData("name: x\nrequests: [a.yaml]\nload:\n  threads: many\n", "load.threads")]
    [InlineData("name: x\nrequests: [a.yaml]\nload:\n  duration: soon\n", "load.duration")]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\nassert:\n  '*':\n    status: 20\n", "status")]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\ndata:\n  file: a.csv\n  mode: shuffle\n", "data.mode")]
    [InlineData("name: x\nrequests: [../a.yaml]\nload: {threads: 1, iterations: 1}\n", "not a valid workspace path")]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1, maxRps: Infinity}\n", "load.maxRps")]
    [InlineData("name: x\nrequests: [a.yaml]\nload: {threads: 1, iterations: 1}\nthresholds: {throughput: NaN}\n", "thresholds.throughput")]
    public void Parse_ReportsProblems(string yaml, string expected)
    {
        var result = new YamlRunPlanFormat().Parse(yaml);

        Assert.False(result.IsSuccess);
        Assert.Contains(expected, result.Error!.Message);
    }

    [Fact]
    public async Task CsvReader_ReadsHeaderAndQuotedValues()
    {
        Directory.CreateDirectory(Path.Combine(_root, "perf"));
        await File.WriteAllTextAsync(Path.Combine(_root, "perf", "users.csv"), "id,name\r\n1,\"Doe, Jane\"\r\n2,\"say \"\"hi\"\"\"\r\n\r\n");

        var rows = await new CsvRunDataReader().ReadCsvAsync(_root, WorkspacePath.Parse("perf/users.csv"));

        Assert.True(rows.IsSuccess, rows.Error?.Message);
        Assert.Equal(2, rows.Value.Count);
        Assert.Equal("Doe, Jane", rows.Value[0]["name"]);
        Assert.Equal("say \"hi\"", rows.Value[1]["name"]);
    }

    [Fact]
    public async Task CsvReader_RejectsRaggedRows()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "d.csv"), "a,b\n1\n");

        var rows = await new CsvRunDataReader().ReadCsvAsync(_root, WorkspacePath.Parse("d.csv"));

        Assert.Contains("Row 2", rows.Error!.Message);
    }

    [Fact]
    public async Task CsvReader_RejectsAnUnterminatedQuotedField()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "d.csv"), "id,name\n1,\"Jane");

        var rows = await new CsvRunDataReader().ReadCsvAsync(_root, WorkspacePath.Parse("d.csv"));

        Assert.Equal(ErrorCodes.InvalidFile, rows.Error!.Code);
        Assert.Contains("not terminated", rows.Error.Message);
    }

    [Fact]
    public async Task PlanStore_ListsPlansAndWritesOnlyChanges()
    {
        var store = new FileSystemRunPlanStore();
        var path = WorkspacePath.Parse("plans/perf/smoke.run.yaml");
        Assert.Empty(store.List(_root).Value);

        Assert.True((await store.WriteAsync(_root, path, "name: a\n")).Value);
        Assert.False((await store.WriteAsync(_root, path, "name: a\n")).Value);
        Directory.CreateDirectory(Path.Combine(_root, ".dynypost"));
        Directory.CreateDirectory(Path.Combine(_root, "plans", ".hidden"));
        Directory.CreateDirectory(Path.Combine(_root, "requests"));
        await File.WriteAllTextAsync(Path.Combine(_root, ".dynypost", "hidden.run.yaml"), "x");
        await File.WriteAllTextAsync(Path.Combine(_root, "plans", ".hidden", "hidden.run.yaml"), "x");
        await File.WriteAllTextAsync(Path.Combine(_root, "requests", "outside.run.yaml"), "x");
        await File.WriteAllTextAsync(Path.Combine(_root, "outside.run.yaml"), "x");
        await File.WriteAllTextAsync(Path.Combine(_root, "plans", "request.yaml"), "x");

        Assert.Equal([path], store.List(_root).Value);
        Assert.Equal("name: a\n", (await store.ReadAsync(_root, path)).Value);
    }

    [Fact]
    public async Task RunStore_SavesListsAndReadsRuns()
    {
        var store = new JsonRunStore();
        var stats = new StatsDto(3, 1, 33.33, 1, 2, 2, 3, 3, 3, 3, 1.5, 0, 10, new Dictionary<string, long> { ["200"] = 2 }, new Dictionary<string, long>());
        var run = new RunResultDto("20260101-000000-abc123", "p.run.yaml", "P", "completed", DateTimeOffset.UnixEpoch, 2000, 2, 0, stats, [], [], [], true, [], [], null);

        Assert.True((await store.SaveAsync(_root, run)).IsSuccess);

        var list = (await store.ListAsync(_root, "p.run.yaml")).Value;
        Assert.Equal(run.RunId, Assert.Single(list).RunId);
        Assert.Empty((await store.ListAsync(_root, "other.run.yaml")).Value);
        Assert.Equal(3, (await store.GetAsync(_root, run.RunId)).Value.Total.Samples);
        Assert.False((await store.GetAsync(_root, "../x")).IsSuccess);
    }

    [Fact]
    public async Task RunStore_SavesAndReadsTheRequestLogOfARun()
    {
        var store = new JsonRunStore();
        var sample = new SampleDto("a.yaml", 1, 2, 30, 4.5, 200, null, "GET", "https://api.test/a", new Dictionary<string, string> { ["user"] = "ada" }, "{}");

        Assert.Empty((await store.GetSamplesAsync(_root, "run-1")).Value);
        Assert.True((await store.SaveSamplesAsync(_root, "run-1", [sample])).IsSuccess);

        var read = Assert.Single((await store.GetSamplesAsync(_root, "run-1")).Value);
        Assert.Equal(sample.Url, read.Url);
        Assert.Equal("ada", read.DataRow!["user"]);
        Assert.Equal("{}", read.ResponsePreview);
        Assert.True(File.Exists(Path.Combine(_root, ".dynypost", "runs", "samples", "run-1.json")));
        Assert.False((await store.GetSamplesAsync(_root, "../x")).IsSuccess);
    }

    [Fact]
    public async Task RunStore_SavesAndReadsTheResponsesOfARun()
    {
        var store = new JsonRunStore();
        byte[] pdf = [0x25, 0x50, 0x44, 0x46, 0x00, 0xFF];

        Assert.True((await store.SaveResponseAsync(_root, "run-1", "1.pdf", pdf)).IsSuccess);

        Assert.Equal(pdf, (await store.GetResponseAsync(_root, "run-1", "1.pdf")).Value);
        Assert.True(File.Exists(Path.Combine(_root, ".dynypost", "runs", "responses", "run-1", "1.pdf")));
        Assert.Equal(RunErrorCodes.ResponseNotFound, (await store.GetResponseAsync(_root, "run-1", "2.pdf")).Error!.Code);
        Assert.False((await store.GetResponseAsync(_root, "run-1", "../1.pdf")).IsSuccess);
        Assert.False((await store.SaveResponseAsync(_root, "../x", "1.pdf", pdf)).IsSuccess);
    }

    [Fact]
    public async Task PlanStore_MovesPlansWithoutOverwriting()
    {
        var store = new FileSystemRunPlanStore();
        var from = WorkspacePath.Parse("a.run.yaml");
        var to = WorkspacePath.Parse("b.run.yaml");
        await store.WriteAsync(_root, from, "name: a\n");

        Assert.True(store.Move(_root, from, to).IsSuccess);
        Assert.False(store.Exists(_root, from));
        Assert.True(store.Exists(_root, to));

        await store.WriteAsync(_root, from, "name: a\n");
        Assert.Equal(ErrorCodes.Io, store.Move(_root, from, to).Error!.Code);
    }

    [Fact]
    public async Task RunStore_MovesTheHistoryOfAPlan()
    {
        var store = new JsonRunStore();
        var stats = new StatsDto(3, 1, 33.33, 1, 2, 2, 3, 3, 3, 3, 1.5, 0, 10, new Dictionary<string, long>(), new Dictionary<string, long>());
        RunResultDto Run(string id, string plan) =>
            new(id, plan, "P", "completed", DateTimeOffset.UnixEpoch, 2000, 2, 0, stats, [], [], [], true, [], [], null);
        await store.SaveAsync(_root, Run("run-1", "old.run.yaml"));
        await store.SaveAsync(_root, Run("run-2", "other.run.yaml"));

        Assert.True((await store.MovePlanAsync(_root, "old.run.yaml", "new.run.yaml")).IsSuccess);

        Assert.Empty((await store.ListAsync(_root, "old.run.yaml")).Value);
        Assert.Equal("run-1", Assert.Single((await store.ListAsync(_root, "new.run.yaml")).Value).RunId);
        Assert.Single((await store.ListAsync(_root, "other.run.yaml")).Value);
    }

    [Fact]
    public async Task PlanStore_DeletesPlans()
    {
        var store = new FileSystemRunPlanStore();
        var path = WorkspacePath.Parse("a.run.yaml");
        await store.WriteAsync(_root, path, "name: a\n");

        Assert.True(store.Delete(_root, path).IsSuccess);
        Assert.False(store.Exists(_root, path));
    }

    [Fact]
    public async Task RunStore_DeletesTheHistoryOfAPlan()
    {
        var store = new JsonRunStore();
        var stats = new StatsDto(3, 1, 33.33, 1, 2, 2, 3, 3, 3, 3, 1.5, 0, 10, new Dictionary<string, long>(), new Dictionary<string, long>());
        RunResultDto Run(string id, string plan) =>
            new(id, plan, "P", "completed", DateTimeOffset.UnixEpoch, 2000, 2, 0, stats, [], [], [], true, [], [], null);
        Assert.True((await store.DeletePlanAsync(_root, "gone.run.yaml")).IsSuccess);
        await store.SaveAsync(_root, Run("run-1", "gone.run.yaml"));
        await store.SaveSamplesAsync(_root, "run-1", [new SampleDto("a.yaml", 1, 1, 0, 1, 200, null, "GET", null, null, null)]);
        await store.SaveResponseAsync(_root, "run-1", "1.json", [0x7B, 0x7D]);
        await store.SaveAsync(_root, Run("run-2", "other.run.yaml"));

        Assert.True((await store.DeletePlanAsync(_root, "gone.run.yaml")).IsSuccess);

        Assert.False(Directory.Exists(Path.Combine(_root, ".dynypost", "runs", "responses", "run-1")));

        Assert.Empty((await store.GetSamplesAsync(_root, "run-1")).Value);

        Assert.Empty((await store.ListAsync(_root, "gone.run.yaml")).Value);
        Assert.False((await store.GetAsync(_root, "run-1")).IsSuccess);
        Assert.Single((await store.ListAsync(_root, "other.run.yaml")).Value);
    }

    [Fact]
    public async Task RunStore_DeletesARun()
    {
        var store = new JsonRunStore();
        var stats = new StatsDto(3, 1, 33.33, 1, 2, 2, 3, 3, 3, 3, 1.5, 0, 10, new Dictionary<string, long>(), new Dictionary<string, long>());
        RunResultDto Run(string id) =>
            new(id, "a.run.yaml", "P", "completed", DateTimeOffset.UnixEpoch, 2000, 2, 0, stats, [], [], [], true, [], [], null);
        await store.SaveAsync(_root, Run("run-1"));
        await store.SaveSamplesAsync(_root, "run-1", [new SampleDto("a.yaml", 1, 1, 0, 1, 200, null, "GET", null, null, null)]);
        await store.SaveResponseAsync(_root, "run-1", "1.json", [1, 2]);
        await store.SaveAsync(_root, Run("run-2"));

        Assert.True((await store.DeleteAsync(_root, "run-1")).IsSuccess);
        Assert.Equal(RunErrorCodes.ResponseNotFound, (await store.GetResponseAsync(_root, "run-1", "1.json")).Error!.Code);

        Assert.Equal("run-2", Assert.Single((await store.ListAsync(_root, "a.run.yaml")).Value).RunId);
        Assert.Equal(RunErrorCodes.RunNotFound, (await store.GetAsync(_root, "run-1")).Error!.Code);
        Assert.Empty((await store.GetSamplesAsync(_root, "run-1")).Value);
        Assert.Equal(RunErrorCodes.RunNotFound, (await store.DeleteAsync(_root, "run-1")).Error!.Code);
        Assert.Equal(ErrorCodes.InvalidPath, (await store.DeleteAsync(_root, "../x")).Error!.Code);
    }
}
