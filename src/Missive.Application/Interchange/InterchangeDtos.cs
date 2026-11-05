namespace Missive.Application.Interchange;

/// <summary>An export format the UI can offer.</summary>
public sealed record ExportFormatDto(string Id, string Name, string Extension);

/// <summary>The outcome of an export.</summary>
/// <param name="Path">The main file that was written.</param>
/// <param name="Files">Every file written, the main one first.</param>
public sealed record ExportResultDto(
    string Path,
    IReadOnlyList<string> Files,
    int Requests,
    int Environments,
    IReadOnlyList<string> Warnings);

/// <summary>The outcome of an import.</summary>
/// <param name="Format">The format the file was read as.</param>
/// <param name="Folder">The folder created for the imported requests, or <c>null</c> when the file had none.</param>
/// <param name="Environments">The ids of the environments created.</param>
public sealed record ImportResultDto(
    string Format,
    string? Folder,
    int Requests,
    int Folders,
    IReadOnlyList<string> Environments,
    IReadOnlyList<string> Warnings);
