using System.Text.Json;

namespace ForEach.Infrastructure.External.eOtpremnica;

/// <summary>
/// Reading the register's replies, which are not a fixed shape.
///
/// The technical guide does not pin the response down, so these search rather than assume a path.
/// That is a deliberate trade and it has a failure mode worth naming: a search that finds nothing
/// returns null, the caller keeps the raw body, and the document reads as Pending — forever, and
/// without an error anywhere. A wrong *path* would look identical to a register that has not
/// answered yet.
///
/// Lifted out of <c>EOtpremnicaClient</c> because it is a rule and the class holding it needs an
/// <c>HttpClient</c>. A host's own tests use a fake client, which means they exercise everything
/// around this and none of it — the fake supplies the status these functions exist to find, so the
/// tests that matter for this file are the ones beside it.
/// </summary>
internal static class EOtpremnicaResponseReader
{
    /// <summary>
    /// The status the register reports for <paramref name="documentId"/>, or null when the body
    /// says nothing about it.
    ///
    /// Matched by finding an object that mentions the id in any string property and then reading a
    /// status from *that* object. Two documents in one changes page is the ordinary case, so
    /// reading the first status in the body would answer about whichever came back first.
    /// </summary>
    public static string? FindRequestStatus(string body, string documentId)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(documentId)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            foreach (var element in Descendants(document.RootElement))
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                var matches = element.EnumerateObject().Any(p =>
                    p.Value.ValueKind == JsonValueKind.String
                    && string.Equals(p.Value.GetString(), documentId, StringComparison.OrdinalIgnoreCase));

                if (!matches) continue;

                foreach (var name in StatusFields)
                {
                    if (element.TryGetProperty(name, out var status)
                        && status.ValueKind == JsonValueKind.String)
                    {
                        return status.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; the caller keeps the raw body.
        }

        return null;
    }

    /// <summary>
    /// The first of <paramref name="names"/> present as a string on the root object, or null.
    /// Root only, unlike the search above — this reads an id the register puts at the top of its
    /// reply, and finding one nested somewhere else would be finding a different thing.
    /// </summary>
    public static string? ReadString(string body, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var name in names)
            {
                if (document.RootElement.TryGetProperty(name, out var property)
                    && property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // A plain-text id or an error page. The raw body is kept by the caller.
        }

        return null;
    }

    /// <summary>
    /// Whether the body carries any entry at all — any object with one of the status fields that
    /// <see cref="FindRequestStatus"/> reads. A page with none is the end of the day's changes,
    /// which is how the status walk knows to stop short of its bound.
    /// </summary>
    public static bool HasEntries(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;

        try
        {
            using var document = JsonDocument.Parse(body);

            return Descendants(document.RootElement).Any(element =>
                element.ValueKind == JsonValueKind.Object
                && StatusFields.Any(name =>
                    element.TryGetProperty(name, out var status)
                    && status.ValueKind == JsonValueKind.String));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly string[] StatusFields =
        ["status", "Status", "requestStatus", "RequestStatus"];

    private static IEnumerable<JsonElement> Descendants(JsonElement element)
    {
        yield return element;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                foreach (var child in Descendants(property.Value))
                    yield return child;
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                foreach (var child in Descendants(item))
                    yield return child;
                break;
        }
    }
}
