using Dynpost.Application.Common;
using Dynpost.Application.Environments;

namespace Dynpost.Infrastructure.Environments;

/// <summary>
/// Keeps the values requests captured from responses in <c>.dynpost/captures.yaml</c>, which is
/// git-ignored: one mapping of variable names to values per environment file name.
/// </summary>
/// <example>
/// <code>
/// dev.yaml:
///   token: eyJhbGciOi...
/// </code>
/// </example>
public sealed class YamlCapturedValueStore : ICapturedValueStore
{
    public const string FileName = "captures.yaml";

    private readonly YamlValuesFile _file = new(
        FileName,
        "# Values captured from responses on this computer. Dynpost keeps this file out of git.\n",
        "captured values");

    public Task<Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> LoadAsync(
        string root,
        CancellationToken cancellationToken = default) =>
        _file.LoadAsync(root, cancellationToken);

    public Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values,
        CancellationToken cancellationToken = default) =>
        _file.SaveAsync(root, values, cancellationToken);
}
