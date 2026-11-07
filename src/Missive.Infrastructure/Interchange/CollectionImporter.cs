using Missive.Application.Common;
using Missive.Application.Interchange;

namespace Missive.Infrastructure.Interchange;

/// <summary>
/// Recognizes and reads the formats Missive imports: Postman collections (v2.0, v2.1) and
/// environments, Insomnia exports (v4), HAR archives and OpenAPI 3.x / Swagger 2.0 descriptions.
/// </summary>
public sealed class CollectionImporter : ICollectionImporter
{
    public const string SupportedFormats =
        "Postman collections (v2.0, v2.1) and environments, Insomnia exports (v4), HAR files and OpenAPI 3 or Swagger 2 descriptions";

    public Result<ParsedCollection> Parse(string text, string fileName)
    {
        var root = InterchangeDocument.Parse(text, out var error);
        if (root is null)
        {
            return new Error(ErrorCodes.InvalidFile, error ?? "The file is empty.");
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name.Length == 0)
        {
            name = "Imported";
        }

        if (PostmanFormat.IsCollection(root))
        {
            return PostmanFormat.ImportCollection(root, name);
        }

        if (PostmanFormat.IsEnvironment(root))
        {
            return PostmanFormat.ImportEnvironment(root);
        }

        if (InsomniaFormat.IsExport(root))
        {
            return InsomniaFormat.Import(root, name);
        }

        if (HarFormat.IsHar(root))
        {
            return HarFormat.Import(root, fileName);
        }

        if (OpenApiFormat.IsOpenApi(root) || OpenApiFormat.IsSwagger(root))
        {
            return OpenApiFormat.Import(root, fileName);
        }

        return new Error(ErrorCodes.InvalidFile, $"Missive doesn't recognize this file. It can import {SupportedFormats}.");
    }
}

/// <summary>Exports Postman collections (v2.1), with a Postman environment file per environment.</summary>
public sealed class PostmanCollectionExporter : ICollectionExporter
{
    public string Id => "postman";

    public string Name => "Postman collection v2.1";

    public string Extension => ".postman_collection.json";

    public ExportOutput Export(RequestCollection collection) => PostmanFormat.Export(collection);
}

/// <summary>Exports Insomnia v4 export files, which hold the environments too.</summary>
public sealed class InsomniaExporter : ICollectionExporter
{
    public string Id => "insomnia";

    public string Name => "Insomnia v4";

    public string Extension => ".insomnia.json";

    public ExportOutput Export(RequestCollection collection) => InsomniaFormat.Export(collection);
}

/// <summary>Reads and writes files the user picked, anywhere on disk.</summary>
public sealed class LocalExternalFiles : IExternalFiles
{
    /// <summary>Import files bigger than this are refused, to keep the app responsive.</summary>
    public const long MaxImportSize = 50 * 1024 * 1024;

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public async Task<Result<string>> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new Error(ErrorCodes.NotFound, $"The file '{path}' does not exist.");
            }

            if (info.Length > MaxImportSize)
            {
                return new Error(ErrorCodes.InvalidFile, $"The file is too large to import (over {MaxImportSize / (1024 * 1024)} MB).");
            }

            // Detects a byte order mark (UTF-16 exports exist); UTF-8 otherwise.
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Error(ErrorCodes.Io, $"Could not read '{path}': {ex.Message}");
        }
    }

    public async Task<Result<Unit>> WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, content, Workspaces.WorkspaceFiles.Utf8NoBom, cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Error(ErrorCodes.Io, $"Could not write '{path}': {ex.Message}");
        }
    }

    public async Task<Result<Unit>> WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(path, content, cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Error(ErrorCodes.Io, $"Could not write '{path}': {ex.Message}");
        }
    }
}
