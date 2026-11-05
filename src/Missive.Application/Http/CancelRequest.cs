namespace Missive.Application.Http;

/// <summary>Cancels a request started with <see cref="SendRequest"/>.</summary>
public sealed class CancelRequest(InFlightRequests inFlight)
{
    /// <summary>Returns whether a request with this id was being sent.</summary>
    public bool Execute(string id) => inFlight.Cancel(id);
}
