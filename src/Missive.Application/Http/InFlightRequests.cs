using System.Collections.Concurrent;

namespace Missive.Application.Http;

/// <summary>Requests being sent, by the id the UI gave them, so they can be cancelled. Registered as a singleton.</summary>
public sealed class InFlightRequests
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new(StringComparer.Ordinal);

    internal bool TryStart(string id, CancellationTokenSource cancellation) => _requests.TryAdd(id, cancellation);

    internal void Finish(string id) => _requests.TryRemove(id, out _);

    /// <summary>Cancels the request with this id; returns <c>false</c> if it isn't being sent.</summary>
    internal bool Cancel(string id)
    {
        if (!_requests.TryGetValue(id, out var cancellation))
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
