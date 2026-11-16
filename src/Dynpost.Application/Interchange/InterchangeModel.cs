using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;

namespace Dynpost.Application.Interchange;

/// <summary>
/// A folder of requests as exchanged with other apps: a workspace (or part of it) being exported,
/// or a collection read from another app's file.
/// </summary>
public sealed record CollectionFolder(
    string Name,
    IReadOnlyList<CollectionFolder> Folders,
    IReadOnlyList<HttpRequestDefinition> Requests)
{
    public int RequestCount => Requests.Count + Folders.Sum(folder => folder.RequestCount);

    public int FolderCount => Folders.Count + Folders.Sum(folder => folder.FolderCount);

    public bool IsEmpty => Requests.Count == 0 && Folders.Count == 0;
}

/// <summary>Requests with the environments that go with them.</summary>
/// <param name="Root">The top folder; its name is the collection's name.</param>
/// <param name="Environments">
/// Environments to export or import. Secret variables are marked; on export their values are
/// always empty, so secrets never leave this computer.
/// </param>
public sealed record RequestCollection(CollectionFolder Root, IReadOnlyList<EnvironmentDefinition> Environments)
{
    /// <summary>
    /// The requests as sent, in the explorer's order, for formats that record exchanges (see
    /// <see cref="ICollectionExporter.NeedsExchanges"/>); empty for the others.
    /// </summary>
    public IReadOnlyList<ExportedExchange> Exchanges { get; init; } = [];
}

/// <summary>A request as it is sent, with the latest response kept for it, for formats such as HAR.</summary>
/// <param name="Name">The request's name.</param>
/// <param name="Path">The request file's workspace path.</param>
/// <param name="Request">
/// The request resolved with the selected environment, with secret values and credentials masked;
/// <c>null</c> when it can't be resolved (see <paramref name="Error"/>).
/// </param>
/// <param name="Latest">The latest response in its history; <c>null</c> when it has none.</param>
/// <param name="Error">Why the request can't be resolved.</param>
public sealed record ExportedExchange(
    string Name,
    string Path,
    CurlCommand? Request,
    Dynpost.Application.History.HistoryEntryDetailsDto? Latest,
    string? Error = null);

/// <summary>A collection read from another app's file.</summary>
/// <param name="Format">A readable name of the format it was read as, e.g. <c>Postman collection v2.1</c>.</param>
/// <param name="Warnings">What could not be imported, or not exactly.</param>
public sealed record ParsedCollection(string Format, RequestCollection Collection, IReadOnlyList<string> Warnings);

/// <summary>A file written next to an exported collection, such as an environment file.</summary>
/// <param name="FileName">The suggested file name; a free name is used if it is taken.</param>
public sealed record ExportedFile(string FileName, string Content);

/// <summary>What an exporter produced.</summary>
/// <param name="Content">The main file, written where the user chose.</param>
/// <param name="Companions">Further files written in the same folder.</param>
/// <param name="Warnings">What the format could not represent.</param>
public sealed record ExportOutput(string Content, IReadOnlyList<ExportedFile> Companions, IReadOnlyList<string> Warnings);
