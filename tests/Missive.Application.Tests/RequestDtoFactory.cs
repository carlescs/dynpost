using Missive.Application.Requests;

namespace Missive.Application.Tests;

internal static class RequestDtoFactory
{
    public static RequestDto Create(string name = "Create user", string method = "POST") => new(
        name,
        method,
        "{{baseUrl}}/users",
        [],
        [new KeyValueDto("Content-Type", "application/json")],
        new AuthDto("none"),
        new BodyDto("json", "{}"));
}
