using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dynpost.Domain.Http;
using Dynpost.Infrastructure.Certificates;
using Dynpost.Infrastructure.Http;

namespace Dynpost.Infrastructure.Tests.Certificates;

public sealed class ClientCertificateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dynpost-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Store_RoundTripsAndIsGitIgnored()
    {
        var store = new JsonClientCertificateStore();
        var certificates = new Dictionary<string, IReadOnlyList<ClientCertificateSetting>>
        {
            ["dev.yaml"] = [new ClientCertificateSetting("api.test", "certs/client.pfx", Passphrase: "pw"), new ClientCertificateSetting("", "certs/all.pem", "certs/all.key")],
            ["empty.yaml"] = [],
        };

        Assert.True((await store.SaveAsync(_root, certificates)).IsSuccess);
        var loaded = (await store.LoadAsync(_root)).Value;

        Assert.Equal(["dev.yaml"], loaded.Keys);
        Assert.Equal(certificates["dev.yaml"], loaded["dev.yaml"]);
        Assert.Contains(".dynpost/", File.ReadAllText(Path.Combine(_root, ".gitignore")));

        Assert.True((await store.SaveAsync(_root, new Dictionary<string, IReadOnlyList<ClientCertificateSetting>>())).IsSuccess);
        Assert.False(File.Exists(Path.Combine(_root, ".dynpost", JsonClientCertificateStore.FileName)));
    }

    [Fact]
    public void LoadCertificate_ReadsPkcs12AndPemFiles()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=dynpost-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = Path.Combine(_root, "client.pfx");
        File.WriteAllBytes(pfx, certificate.Export(X509ContentType.Pkcs12, "pw"));
        var pem = Path.Combine(_root, "client.pem");
        File.WriteAllText(pem, certificate.ExportCertificatePem());
        var keyFile = Path.Combine(_root, "client.key");
        File.WriteAllText(keyFile, key.ExportPkcs8PrivateKeyPem());

        using var fromPfx = HttpClientSender.LoadCertificate(new ClientCertificate(pfx, Passphrase: "pw")).Value;
        using var fromPem = HttpClientSender.LoadCertificate(new ClientCertificate(pem, keyFile)).Value;

        Assert.True(fromPfx.HasPrivateKey);
        Assert.True(fromPem.HasPrivateKey);
        Assert.Equal(certificate.Thumbprint, fromPem.Thumbprint);

        var wrongPassphrase = HttpClientSender.LoadCertificate(new ClientCertificate(pfx, Passphrase: "nope"));
        Assert.False(wrongPassphrase.IsSuccess);
        Assert.Contains("passphrase", wrongPassphrase.Error!.Message);

        var noKey = HttpClientSender.LoadCertificate(new ClientCertificate(pem));
        Assert.False(noKey.IsSuccess);

        var missing = HttpClientSender.LoadCertificate(new ClientCertificate(Path.Combine(_root, "missing.pfx")));
        Assert.Contains("doesn't exist", missing.Error!.Message);
    }
}
