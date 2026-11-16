using Dynpost.Application.Certificates;
using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Http;
using NSubstitute;

namespace Dynpost.Application.Tests.Certificates;

public class ClientCertificateUseCaseTests
{
    private const string Root = "/work/api";
    private readonly IClientCertificateStore _store = Substitute.For<IClientCertificateStore>();
    private readonly CurrentWorkspace _current = new();
    private IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>> _saved = new Dictionary<string, IReadOnlyList<ClientCertificateSetting>>();

    public ClientCertificateUseCaseTests()
    {
        _current.Open(Root);
        _store.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(_ =>
            Result<IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>>>.Success(_saved));
        _store.SaveAsync(Root, Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _saved = call.Arg<IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>>>();
                return Unit.Value;
            });
    }

    [Fact]
    public async Task Save_KeepsTheStoredPassphraseUnlessANewOneIsGiven()
    {
        _saved = new Dictionary<string, IReadOnlyList<ClientCertificateSetting>>
        {
            ["dev.yaml"] = [new ClientCertificateSetting("api.test", "a.pfx", Passphrase: "old")],
        };

        var result = await new SaveClientCertificates(_store, _current).ExecuteAsync(
            "dev.yaml",
            [new ClientCertificateDto(" api.test ", "a.pfx"), new ClientCertificateDto("", "b.pem", "b.key", Passphrase: "new"), new ClientCertificateDto("x", " ")]);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(
            [new ClientCertificateSetting("api.test", "a.pfx", "", "old"), new ClientCertificateSetting("", "b.pem", "b.key", "new")],
            _saved["dev.yaml"]);
        Assert.All(result.Value, dto => Assert.True(dto.HasPassphrase));
        Assert.All(result.Value, dto => Assert.Null(dto.Passphrase));
    }

    [Fact]
    public async Task Save_RejectsHostsThatAreNotHostNames()
    {
        var result = await new SaveClientCertificates(_store, _current).ExecuteAsync("dev.yaml", [new ClientCertificateDto("https://api.test/", "a.pfx")]);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task For_ResolvesThePathsOfTheMatchingCertificate()
    {
        _saved = new Dictionary<string, IReadOnlyList<ClientCertificateSetting>>
        {
            ["dev.yaml"] = [new ClientCertificateSetting("api.test", "certs/a.pem", "certs/a.key", "pw")],
        };

        var found = await ClientCertificates.ForAsync(_store, Root, "dev.yaml", new Uri("https://api.test/x"), default);
        var none = await ClientCertificates.ForAsync(_store, Root, "dev.yaml", new Uri("http://api.test/x"), default);

        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "certs/a.pem")), found.Value!.CertificatePath);
        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "certs/a.key")), found.Value.KeyPath);
        Assert.Equal("pw", found.Value.Passphrase);
        Assert.Null(none.Value);
    }
}
