namespace Missive.Application.About;

// Wire format of the about message. Keep in sync with src/Missive.Client/src/app/models/messages.ts.

public sealed record AboutDto(string Version, string Runtime, string OperatingSystem);
