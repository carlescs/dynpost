using System.Text.Json.Nodes;
using Dynpost.Application.History;
using Dynpost.Application.Interchange;
using Dynpost.Domain.Requests;

namespace Dynpost.Infrastructure.Interchange;

/// <summary>
/// Writes HAR 1.2 logs: one entry per request, resolved with the selected environment (secret
/// values and credentials masked), with the latest response kept in its history. Requests that
/// can't be resolved are skipped and reported; requests without history get an empty response.
/// </summary>
internal static class HarExportFormat
{
    /// <summary>Stands in for the time of requests that have no response in their history, so exports stay stable.</summary>
    private static readonly DateTimeOffset NeverSent = DateTimeOffset.UnixEpoch;

    public static ExportOutput Export(RequestCollection collection)
    {
        var warnings = new List<string>();
        var entries = new JsonArray();
        var withoutResponse = 0;
        foreach (var exchange in collection.Exchanges)
        {
            if (exchange.Request is not { } request)
            {
                warnings.Add($"Skipped '{exchange.Path}': {exchange.Error}");
                continue;
            }

            if (exchange.Latest is null)
            {
                withoutResponse++;
            }

            entries.Add(Entry(exchange.Name, request, exchange.Latest));
        }

        if (withoutResponse > 0)
        {
            warnings.Add(withoutResponse == 1
                ? "1 request has no response in its history; its entry has an empty response."
                : $"{withoutResponse} requests have no response in their history; their entries have an empty response.");
        }

        var document = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["version"] = "1.2",
                ["creator"] = new JsonObject { ["name"] = "Dynpost", ["version"] = "1.0" },
                ["pages"] = new JsonArray(),
                ["entries"] = entries,
            },
        };
        return new ExportOutput(InterchangeText.Write(document), [], warnings);
    }

    private static JsonObject Entry(string name, CurlCommand request, HistoryEntryDetailsDto? latest)
    {
        var response = latest?.Response;
        var timings = response?.Timings;
        return new JsonObject
        {
            ["startedDateTime"] = (latest?.Entry.SentAt ?? NeverSent).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            ["time"] = response?.DurationMs ?? 0,
            ["comment"] = name,
            ["request"] = Request(request),
            ["response"] = response is null
                ? new JsonObject
                {
                    ["status"] = 0,
                    ["statusText"] = string.Empty,
                    ["httpVersion"] = string.Empty,
                    ["cookies"] = new JsonArray(),
                    ["headers"] = new JsonArray(),
                    ["content"] = new JsonObject { ["size"] = 0, ["mimeType"] = "x-unknown" },
                    ["redirectURL"] = string.Empty,
                    ["headersSize"] = -1,
                    ["bodySize"] = -1,
                }
                : new JsonObject
                {
                    ["status"] = response.Status,
                    ["statusText"] = response.StatusText,
                    ["httpVersion"] = "HTTP/1.1",
                    ["cookies"] = new JsonArray(),
                    ["headers"] = Pairs(response.Headers.Select(h => KeyValuePair.Create(h.Key, h.Value))),
                    ["content"] = Content(response),
                    ["redirectURL"] = response.Headers.FirstOrDefault(h => h.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty,
                    ["headersSize"] = -1,
                    ["bodySize"] = response.Size,
                },
            ["cache"] = new JsonObject(),
            ["timings"] = new JsonObject
            {
                ["dns"] = timings?.Dns ?? -1,
                ["connect"] = timings?.Connect ?? -1,
                ["ssl"] = timings?.Tls ?? -1,
                ["send"] = 0,
                ["wait"] = timings?.Wait ?? response?.DurationMs ?? 0,
                ["receive"] = timings?.Download ?? 0,
            },
        };
    }

    private static JsonObject Content(Dynpost.Application.Http.ResponseDto response)
    {
        var content = new JsonObject
        {
            ["size"] = response.Size,
            ["mimeType"] = response.ContentType ?? "x-unknown",
        };
        if (response.Body.Length > 0)
        {
            content["text"] = response.Body;
            if (response.BodyEncoding == "base64")
            {
                content["encoding"] = "base64";
            }
        }

        return content;
    }

    private static JsonObject Request(CurlCommand request)
    {
        var url = Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ? uri : null;
        var query = url is null
            ? []
            : InterchangeText.SplitQuery(request.Url).Query.Select(q => KeyValuePair.Create(q.Key, q.Value));
        var result = new JsonObject
        {
            ["method"] = request.Method,
            ["url"] = request.Url,
            ["httpVersion"] = request.Http2 ? "HTTP/2" : "HTTP/1.1",
            ["cookies"] = new JsonArray(),
            ["headers"] = Pairs(request.Headers),
            ["queryString"] = Pairs(query),
            ["headersSize"] = -1,
            ["bodySize"] = -1,
        };

        var mimeType = request.Headers.LastOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        switch (request.Body)
        {
            case CurlTextBody text:
                result["postData"] = new JsonObject { ["mimeType"] = mimeType ?? "text/plain", ["text"] = text.Text };
                break;
            case CurlUrlEncodedBody fields:
                result["postData"] = new JsonObject
                {
                    ["mimeType"] = "application/x-www-form-urlencoded",
                    ["params"] = new JsonArray(fields.Fields.Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["value"] = f.Value }).ToArray()),
                };
                break;
            case CurlFormBody fields:
                result["postData"] = new JsonObject
                {
                    ["mimeType"] = "multipart/form-data",
                    ["params"] = new JsonArray(fields.Fields.Select(FormParam).ToArray()),
                };
                break;
            case CurlFileBody file:
                result["postData"] = new JsonObject
                {
                    ["mimeType"] = mimeType ?? "application/octet-stream",
                    ["text"] = string.Empty,
                    ["comment"] = $"The body is the file {Path.GetFileName(file.Path)}.",
                };
                break;
            case null:
                break;
        }

        return result;
    }

    private static JsonNode FormParam(FormField field)
    {
        var param = new JsonObject { ["name"] = field.Name };
        if (field.IsFile)
        {
            param["fileName"] = field.FileName.Length > 0 ? field.FileName : Path.GetFileName(field.Value);
        }
        else
        {
            param["value"] = field.Value;
        }

        if (field.ContentType.Length > 0)
        {
            param["contentType"] = field.ContentType;
        }

        return param;
    }

    private static JsonArray Pairs(IEnumerable<KeyValuePair<string, string>> pairs) =>
        new(pairs.Select(p => (JsonNode)new JsonObject { ["name"] = p.Key, ["value"] = p.Value }).ToArray());
}
