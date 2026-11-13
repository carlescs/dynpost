namespace Dynpost.Application.About;

// Wire format of the about message. Keep in sync with src/Dynpost.Client/src/app/models/messages.ts.

public sealed record AboutDto(string Version, string Runtime, string OperatingSystem);
