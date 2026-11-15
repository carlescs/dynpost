using Dynpost.Application.Common;

namespace Dynpost.Application.Http;

/// <summary>Error codes and messages for sending requests.</summary>
public static class HttpErrors
{
    public const string NetworkErrorCode = "http.network_error";
    public const string TimeoutCode = "http.timeout";
    public const string CancelledCode = "http.cancelled";
    public const string AlreadySendingCode = "http.already_sending";

    public static Error NetworkError(string host, string details) =>
        new(NetworkErrorCode, string.IsNullOrWhiteSpace(details)
            ? $"Could not reach {host}."
            : $"Could not reach {host}: {details.Trim()}");

    public static Error Timeout(TimeSpan timeout) =>
        new(TimeoutCode, timeout < TimeSpan.FromSeconds(1)
            ? $"No response within {timeout.TotalMilliseconds:0} ms. The timeout can be changed on the request's Settings tab."
            : $"No response within {timeout.TotalSeconds:0.#} seconds. The timeout can be changed on the request's Settings tab.");

    public static Error Cancelled() => new(CancelledCode, "The request was cancelled.");

    public static Error AlreadySending(string id) =>
        new(AlreadySendingCode, $"A request with id '{id}' is already being sent.");
}
