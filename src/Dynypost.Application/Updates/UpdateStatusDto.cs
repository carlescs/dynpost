namespace Dynypost.Application.Updates;

// Wire format of the update messages, sent to the UI by the bridge. Keep in sync with
// src/Dynypost.Client/src/app/models/messages.ts.

/// <param name="State">
/// <c>unsupported</c> (this copy cannot update itself), <c>upToDate</c>, or <c>ready</c> (an update
/// is downloaded and installs on restart).
/// </param>
/// <param name="CurrentVersion">The running version, when known.</param>
/// <param name="Version">The downloaded version, when <paramref name="State"/> is <c>ready</c>.</param>
public sealed record UpdateStatusDto(string State, string? CurrentVersion, string? Version = null)
{
    public const string Unsupported = "unsupported";
    public const string UpToDate = "upToDate";
    public const string Ready = "ready";
}
