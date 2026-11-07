using Missive.Application.Common;
using Missive.Application.Interchange;

namespace Missive.Application.Http;

/// <summary>Writes the body of a recent response to a file, byte for byte as it was received.</summary>
public sealed class SaveResponseBody(ResponseBodies bodies, IExternalFiles files)
{
    /// <summary>The file name suggested for the body of the response to send <paramref name="id"/>.</summary>
    public Result<string> SuggestedFileName(string id) =>
        bodies.Find(id) is { } body ? body.FileName : NotAvailable();

    /// <summary>
    /// Writes the body to <paramref name="destination"/>, the path the user chose (the save dialog
    /// has already asked before replacing it), and returns the path written.
    /// </summary>
    public async Task<Result<string>> ExecuteAsync(string id, string destination, CancellationToken cancellationToken = default)
    {
        if (bodies.Find(id) is not { } body)
        {
            return NotAvailable();
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            return new Error(ErrorCodes.InvalidPath, "Choose a file to save the response to.");
        }

        var written = await files.WriteBytesAsync(destination, body.Content, cancellationToken);
        return written.IsSuccess ? destination : written.Error!;
    }

    private static Error NotAvailable() =>
        new(ErrorCodes.NotFound, "The response is no longer available; send the request again to save it.");
}
