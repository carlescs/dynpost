namespace Dynypost.Application.About;

// Wire format of the about message. Keep in sync with src/Dynypost.Client/src/app/models/messages.ts.

public sealed record AboutDto(string Version, string Runtime, string OperatingSystem);
