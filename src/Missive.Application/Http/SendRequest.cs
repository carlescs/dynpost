using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Requests;
using Missive.Domain.Http;

namespace Missive.Application.Http;

/// <summary>
/// Sends the request currently in the editor (saved or not) and returns the response. The request
/// can be cancelled with <see cref="CancelRequest"/> using the same id. <c>{{variable}}</c>
/// references are resolved with the selected environment.
/// </summary>
public sealed class SendRequest(IHttpSender sender, InFlightRequests inFlight, ActiveEnvironment environment)
{
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

        var variables = await environment.LoadAsync(cancellationToken);
        if (!variables.IsSuccess)
        {
            return variables.Error!;
        }

        var prepared = RequestPreparation.Prepare(definition.Value, variables.Value.Values);
        if (!prepared.IsValid)
        {
            var hint = MissingVariablesHint(prepared.MissingVariables, variables.Value);
            return new Error(ErrorCodes.InvalidRequest, string.Join(" ", hint is null ? prepared.Errors : [.. prepared.Errors, hint]));
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

    /// <summary>Explains where the values of undefined variables should come from.</summary>
    private static string? MissingVariablesHint(IReadOnlyList<string> missing, ActiveVariables variables)
    {
        if (missing.Count == 0)
        {
            return null;
        }

        if (variables.EnvironmentName is null)
        {
            return "No environment is selected.";
        }

        var unsetSecrets = missing.Where(variables.UnsetSecrets.Contains).ToList();
        return unsetSecrets.Count > 0
            ? $"Secret values are kept on each computer and not shared through git: enter {string.Join(", ", unsetSecrets)} in the environment '{variables.EnvironmentName}'."
            : $"Add them to the environment '{variables.EnvironmentName}'.";
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
