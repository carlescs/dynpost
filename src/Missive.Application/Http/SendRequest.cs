using Missive.Application.Common;
using Missive.Application.Requests;
using Missive.Domain.Http;

namespace Missive.Application.Http;

/// <summary>
/// Sends the request currently in the editor (saved or not) and returns the response. The request
/// can be cancelled with <see cref="CancelRequest"/> using the same id.
/// </summary>
public sealed class SendRequest(IHttpSender sender, InFlightRequests inFlight)
{
    // Environments are not supported yet, so no variables are defined.
    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>();

    /// <param name="id">An id chosen by the caller, used to cancel the request.</param>
    public async Task<Result<ResponseDto>> ExecuteAsync(string id, RequestDto request, CancellationToken cancellationToken = default)
    {
        // The name doesn't matter for sending; an unnamed request can still be tried out.
        var named = string.IsNullOrWhiteSpace(request.Name) ? request with { Name = "Untitled" } : request;
        var definition = RequestMapper.ToDomain(named);
        if (definition.IsFailure)
        {
            return definition.Error!;
        }

        var prepared = RequestPreparation.Prepare(definition.Value, NoVariables);
        if (!prepared.IsValid)
        {
            return new Error(ErrorCodes.InvalidRequest, string.Join(" ", prepared.Errors));
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!inFlight.TryStart(id, cancellation))
        {
            return HttpErrors.AlreadySending(id);
        }

        try
        {
            var response = await sender.SendAsync(prepared.Request!, cancellation.Token);
            return response.Map(ToDto);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return HttpErrors.Cancelled();
        }
        finally
        {
            inFlight.Finish(id);
        }
    }

    private static ResponseDto ToDto(HttpResponseData response)
    {
        var contentType = response.Headers
            .FirstOrDefault(header => string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            .Value;
        var (mediaType, charset) = ResponseBodyDecoder.ParseContentType(contentType);
        var (body, encoding) = ResponseBodyDecoder.Decode(response.Body, mediaType, charset);

        return new ResponseDto(
            response.StatusCode,
            response.ReasonPhrase ?? string.Empty,
            response.Headers.Select(header => new KeyValueDto(header.Key, header.Value)).ToList(),
            mediaType,
            body,
            encoding,
            response.Body.LongLength,
            response.Truncated,
            (long)Math.Round(response.Duration.TotalMilliseconds));
    }
}
