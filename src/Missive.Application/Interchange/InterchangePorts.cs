using Missive.Application.Common;

namespace Missive.Application.Interchange;

/// <summary>Writes a collection in a format other apps can import.</summary>
public interface ICollectionExporter
{
    /// <summary>The id the UI asks for, e.g. <c>postman</c>.</summary>
    string Id { get; }

    /// <summary>A readable name, e.g. <c>Postman collection v2.1</c>.</summary>
    string Name { get; }

    /// <summary>The extension of the main file, with its dot, e.g. <c>.postman_collection.json</c>.</summary>
    string Extension { get; }

    ExportOutput Export(RequestCollection collection);
}

/// <summary>Reads collections exported by other apps, recognizing the format from the content.</summary>
public interface ICollectionImporter
{
    /// <summary>
    /// Reads <paramref name="text"/>, or says why it can't (<see cref="ErrorCodes.InvalidFile"/>
    /// for content that isn't a format Missive knows).
    /// </summary>
    /// <param name="fileName">The file's name, used to name collections that have no name of their own.</param>
    Result<ParsedCollection> Parse(string text, string fileName);
}

/// <summary>Reads and writes files outside the workspace, at paths the user chose.</summary>
public interface IExternalFiles
{
    bool Exists(string path);

    Task<Result<string>> ReadTextAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Writes (or replaces) a UTF-8 text file.</summary>
    Task<Result<Unit>> WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);
}
