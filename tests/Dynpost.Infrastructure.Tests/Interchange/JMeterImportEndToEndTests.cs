using Dynpost.Application.Interchange;
using Dynpost.Application.Workspaces;
using Dynpost.Infrastructure.Environments;
using Dynpost.Infrastructure.Interchange;
using Dynpost.Infrastructure.Runs;
using Dynpost.Infrastructure.Workspaces;

namespace Dynpost.Infrastructure.Tests.Interchange;

/// <summary>Imports a JMeter plan into a real workspace folder, with the real stores and formats.</summary>
public sealed class JMeterImportEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dynpost-jmx-" + Guid.NewGuid().ToString("N"));
    private readonly string _workspace;
    private readonly string _source;

    public JMeterImportEndToEndTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _source = Path.Combine(_root, "jmeter");
        Directory.CreateDirectory(Path.Combine(_workspace, "requests"));
        Directory.CreateDirectory(Path.Combine(_source, "data"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private const string TestPlan = """
        <?xml version="1.0" encoding="UTF-8"?>
        <jmeterTestPlan version="1.2" properties="5.0" jmeter="5.6.3">
          <hashTree>
            <TestPlan guiclass="TestPlanGui" testclass="TestPlan" testname="Shop"/>
            <hashTree>
              <ConfigTestElement guiclass="HttpDefaultsGui" testclass="ConfigTestElement" testname="HTTP Request Defaults">
                <stringProp name="HTTPSampler.domain">shop.test</stringProp>
                <stringProp name="HTTPSampler.protocol">https</stringProp>
              </ConfigTestElement>
              <hashTree/>
              <ThreadGroup guiclass="ThreadGroupGui" testclass="ThreadGroup" testname="Checkout load">
                <stringProp name="ThreadGroup.num_threads">20</stringProp>
                <stringProp name="ThreadGroup.ramp_time">10</stringProp>
                <elementProp name="ThreadGroup.main_controller" elementType="LoopController" guiclass="LoopControlPanel" testclass="LoopController">
                  <stringProp name="LoopController.loops">50</stringProp>
                </elementProp>
              </ThreadGroup>
              <hashTree>
                <ConstantTimer guiclass="ConstantTimerGui" testclass="ConstantTimer" testname="Think">
                  <stringProp name="ConstantTimer.delay">250</stringProp>
                </ConstantTimer>
                <hashTree/>
                <CSVDataSet guiclass="TestBeanGUI" testclass="CSVDataSet" testname="Users">
                  <stringProp name="filename">data/users.csv</stringProp>
                  <stringProp name="variableNames"></stringProp>
                  <stringProp name="delimiter">,</stringProp>
                  <boolProp name="recycle">true</boolProp>
                  <stringProp name="shareMode">shareMode.all</stringProp>
                </CSVDataSet>
                <hashTree/>
                <ResponseAssertion guiclass="AssertionGui" testclass="ResponseAssertion" testname="2xx">
                  <collectionProp name="Asserion.test_strings"><stringProp name="1">2..</stringProp></collectionProp>
                  <stringProp name="Assertion.test_field">Assertion.response_code</stringProp>
                  <intProp name="Assertion.test_type">1</intProp>
                </ResponseAssertion>
                <hashTree/>
                <HTTPSamplerProxy guiclass="HttpTestSampleGui" testclass="HTTPSamplerProxy" testname="List products">
                  <stringProp name="HTTPSampler.path">/products</stringProp>
                  <stringProp name="HTTPSampler.method">GET</stringProp>
                </HTTPSamplerProxy>
                <hashTree/>
                <HTTPSamplerProxy guiclass="HttpTestSampleGui" testclass="HTTPSamplerProxy" testname="Old page" enabled="false">
                  <stringProp name="HTTPSampler.path">/old</stringProp>
                  <stringProp name="HTTPSampler.method">GET</stringProp>
                </HTTPSamplerProxy>
                <hashTree/>
              </hashTree>
            </hashTree>
          </hashTree>
        </jmeterTestPlan>
        """;

    [Fact]
    public async Task WritesRequestsFolderSettingsARunPlanAndItsData()
    {
        var file = Path.Combine(_source, "shop.jmx");
        await File.WriteAllTextAsync(file, TestPlan);
        await File.WriteAllTextAsync(Path.Combine(_source, "data", "users.csv"), "user,pass\r\nann,1\r\n");

        var current = new CurrentWorkspace();
        current.Open(_workspace);
        var useCase = new ImportCollection(
            new FileSystemWorkspaceRepository(),
            new YamlFolderDefaultsStore(),
            new FileSystemRunPlanStore(),
            new YamlRunPlanFormat(),
            new FileSystemEnvironmentStore(),
            new YamlSecretStore(),
            new LocalExternalFiles(),
            new CollectionImporter(),
            current);

        var result = await useCase.ExecuteAsync(new ImportCollectionRequest(file));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("requests/Shop", result.Value.Folder);
        Assert.Equal(["plans/Shop/checkout-load.run.yaml"], result.Value.Plans);
        Assert.Equal(2, result.Value.Requests);

        Assert.Equal("variables:\n  baseUrl: https://shop.test\n", await File.ReadAllTextAsync(Path.Combine(_workspace, "requests/Shop/_folder.yaml")));
        Assert.True(File.Exists(Path.Combine(_workspace, "requests/Shop/Checkout load/list-products.yaml")));
        Assert.Equal("user,pass\nann,1\n", await File.ReadAllTextAsync(Path.Combine(_workspace, "data/Shop/users.csv")));

        var plan = await File.ReadAllTextAsync(Path.Combine(_workspace, "plans/Shop/checkout-load.run.yaml"));
        Assert.Equal(
            """
            name: Checkout load
            requests:
              - requests/Shop/Checkout load/list-products.yaml
              - requests/Shop/Checkout load/old-page.yaml
            disabled:
              - requests/Shop/Checkout load/old-page.yaml
            load:
              threads: 20
              iterations: 50
              rampUp: 10s
              thinkTime: 250ms
            data:
              file: data/Shop/users.csv
            assert:
              "*":
                status: 2xx

            """.Replace("\r\n", "\n"),
            plan);
        Assert.True(new YamlRunPlanFormat().Parse(plan).IsSuccess);
    }
}
