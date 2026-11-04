namespace Missive.Domain.Requests;

/// <summary>A query parameter or header. Disabled entries are kept but not sent.</summary>
public sealed record KeyValueEntry(string Key, string Value, bool Enabled = true);
