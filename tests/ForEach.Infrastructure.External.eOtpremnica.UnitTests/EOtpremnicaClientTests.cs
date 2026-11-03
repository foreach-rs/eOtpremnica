using System.Net;
using ForEach.Infrastructure.External.eOtpremnica;

namespace ForEach.Infrastructure.External.eOtpremnica.UnitTests;

/// <summary>
/// The client against a fake handler: what it puts on the wire, and — more to the point — how it
/// sorts the ways a call can end. A submission that may have reached the register is Unknown and
/// must never read as a retryable failure; the same loss on a read is an ordinary retry.
/// </summary>
public class EOtpremnicaClientTests
{
    private const string RequestId = "REQ-1";
    private static readonly DateOnly Day = new(2026, 11, 3);

    // ---- Submission on the wire ----

    [Fact]
    public async Task A_submission_is_a_multipart_post_with_the_key_the_request_id_and_the_file()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json("{}")));

        await Client(handler).SendDespatchAdviceAsync("<xml/>", RequestId);

        var sent = Assert.Single(handler.Requests);

        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://register.test/public/documents/requests", sent.Uri.ToString());
        Assert.Equal("k", sent.ApiKey);

        var requestIdPart = Assert.Single(sent.Parts, p => p.Name == "RequestId");
        Assert.Equal(RequestId, requestIdPart.Text);

        var file = Assert.Single(sent.Parts, p => p.Name == "File");
        Assert.Equal($"despatch-advice-{RequestId}.xml", file.FileName);
        Assert.Equal("text/xml", file.ContentType);
        Assert.Equal("<xml/>", file.Text);
    }

    // ---- Submission replies ----

    [Fact]
    public async Task An_accepted_submission_carries_the_registers_id_and_status_and_keeps_the_body()
    {
        const string body = """{"id":"REG-1","status":"DocumentRequest.Pending"}""";
        var handler = new FakeHandler((_, _) => Task.FromResult(Json(body)));

        var result = await Client(handler).SendDespatchAdviceAsync("<xml/>", RequestId);

        Assert.True(result.IsSuccess);
        Assert.Equal("REG-1", result.Value!.DocumentId);
        Assert.Equal("DocumentRequest.Pending", result.Value.RawStatus);
        Assert.Equal(body, result.Value.ResponseBody);
        Assert.Equal(body, result.ResponseBody);
    }

    [Fact]
    public async Task A_reply_naming_no_id_falls_back_to_the_request_id_and_pending()
    {
        // The fallback is what makes a resend after a timeout findable: ask about the id we sent.
        var handler = new FakeHandler((_, _) => Task.FromResult(Json("""{"accepted":true}""")));

        var result = await Client(handler).SendDespatchAdviceAsync("<xml/>", RequestId);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequestId, result.Value!.DocumentId);
        Assert.Equal("DocumentRequest.Pending", result.Value.RawStatus);
    }

    [Fact]
    public async Task A_5xx_on_a_submission_is_unknown_and_not_worth_retrying()
    {
        // The register was reached and failed inside it: the request may be queued.
        var handler = new FakeHandler((_, _) => Task.FromResult(Reply(503, "down")));

        var result = await Client(handler).SendDespatchAdviceAsync("<xml/>", RequestId);

        Assert.Equal(EOtpremnicaOutcome.Unknown, result.Outcome);
        Assert.Equal(503, result.HttpStatusCode);
        Assert.Equal("down", result.ResponseBody);
        Assert.False(result.IsWorthRetrying);
    }

    [Fact]
    public async Task A_5xx_on_a_status_read_is_an_ordinary_transient_failure()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Reply(503, "down")));

        var result = await Client(handler).GetStatusAsync(RequestId, Day);

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.Equal(EOtpremnicaFailureKind.Transient, result.Error!.Kind);
        Assert.True(result.IsWorthRetrying);
    }

    [Fact]
    public async Task A_401_on_a_submission_is_a_configuration_failure()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Reply(401, "no")));

        var result = await Client(handler).SendDespatchAdviceAsync("<xml/>", RequestId);

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.Equal(EOtpremnicaFailureKind.Configuration, result.Error!.Kind);
    }

    [Fact]
    public async Task A_400_on_a_submission_is_the_callers_mistake_and_quotes_the_body()
    {
        var handler = new FakeHandler((_, _) =>
            Task.FromResult(Reply(400, "Element 'cbc:ID' is missing")));

        var result = await Client(handler).SendDespatchAdviceAsync("<xml/>", RequestId);

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.Equal(EOtpremnicaFailureKind.CallerMistake, result.Error!.Kind);
        Assert.Contains("Element 'cbc:ID' is missing", result.FailureMessage, StringComparison.Ordinal);
    }

    // ---- Timeouts and transport ----

    [Fact]
    public async Task A_submission_that_times_out_is_unknown()
    {
        var result = await Client(Hanging()).SendDespatchAdviceAsync("<xml/>", RequestId);

        Assert.Equal(EOtpremnicaOutcome.Unknown, result.Outcome);
        Assert.Contains("nije odgovorila na vreme", result.FailureMessage, StringComparison.Ordinal);
        Assert.False(result.IsWorthRetrying);
    }

    [Fact]
    public async Task A_status_read_that_times_out_is_a_retryable_transport_failure()
    {
        var result = await Client(Hanging()).GetStatusAsync(RequestId, Day);

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.NotNull(result.TransportFailure);
        Assert.True(result.IsWorthRetrying);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    public async Task A_connection_that_never_opened_is_a_retryable_failure_even_on_a_submission(
        HttpRequestError error)
    {
        // Nothing was sent, so nothing can be queued; Unknown here would forbid a safe resend.
        var result = await Client(Throwing(error)).SendDespatchAdviceAsync("<xml/>", RequestId);

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.NotNull(result.TransportFailure);
        Assert.True(result.IsWorthRetrying);
    }

    [Fact]
    public async Task A_connection_cut_after_it_opened_is_unknown_on_a_submission()
    {
        var result = await Client(Throwing(HttpRequestError.Unknown))
            .SendDespatchAdviceAsync("<xml/>", RequestId);

        Assert.Equal(EOtpremnicaOutcome.Unknown, result.Outcome);
        Assert.False(result.IsWorthRetrying);
    }

    [Theory]
    [InlineData(HttpRequestError.Unknown)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    public async Task Any_transport_error_on_a_status_read_is_a_transport_failure(
        HttpRequestError error)
    {
        var result = await Client(Throwing(error)).GetStatusAsync(RequestId, Day);

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.NotNull(result.TransportFailure);
        Assert.True(result.IsWorthRetrying);
    }

    [Fact]
    public async Task The_callers_own_cancellation_propagates_and_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(handler).SendDespatchAdviceAsync("<xml/>", RequestId, cts.Token));
    }

    // ---- Status walk ----

    [Fact]
    public async Task The_status_walk_moves_to_the_next_page_until_the_document_turns_up()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.Query.EndsWith("page=0")
                ? Json(Entries("OTHER", "DocumentRequest.Pending"))
                : Json(Entries(RequestId, "DocumentRequest.Succeeded"))));

        var result = await Client(handler).GetStatusAsync(RequestId, Day);

        Assert.True(result.IsSuccess);
        Assert.Equal("DocumentRequest.Succeeded", result.Value!.RawStatus);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Equal("?date=2026-11-03&page=0", handler.Requests[0].Uri.Query);
        Assert.Equal("?date=2026-11-03&page=1", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task An_empty_page_ends_the_walk_and_the_document_reads_as_pending()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(
            request.RequestUri!.Query.EndsWith("page=0")
                ? Json(Entries("OTHER", "DocumentRequest.Pending"))
                : Json("""{"items":[]}""")));

        var result = await Client(handler).GetStatusAsync(RequestId, Day);

        Assert.True(result.IsSuccess);
        Assert.Equal("DocumentRequest.Pending", result.Value!.RawStatus);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.Query.EndsWith("page=2"));
    }

    [Fact]
    public async Task A_page_that_never_empties_is_bounded_by_the_page_limit()
    {
        var handler = new FakeHandler((_, _) =>
            Task.FromResult(Json(Entries("OTHER", "DocumentRequest.Pending"))));

        var result = await Client(handler).GetStatusAsync(RequestId, Day);

        Assert.True(result.IsSuccess);
        Assert.Equal("DocumentRequest.Pending", result.Value!.RawStatus);
        Assert.Equal(EOtpremnicaClient.MaxStatusPages, handler.Requests.Count);
    }

    // ---- Probe and cancel ----

    [Fact]
    public async Task A_probe_reads_one_page_and_returns_the_body()
    {
        const string body = """{"items":[]}""";
        var handler = new FakeHandler((_, _) => Task.FromResult(Json(body)));

        var result = await Client(handler).ProbeAsync(Day);

        Assert.True(result.IsSuccess);
        Assert.Equal(body, result.Value);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("?date=2026-11-03&page=0", sent.Uri.Query);
    }

    [Fact]
    public async Task A_probe_refused_with_401_is_a_configuration_failure()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Reply(401, "no")));

        var result = await Client(handler).ProbeAsync(Day);

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.Equal(EOtpremnicaFailureKind.Configuration, result.Error!.Kind);
    }

    [Fact]
    public async Task Cancelling_is_refused_locally_without_any_http_request()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json("{}")));

        var result = await Client(handler).CancelAsync(RequestId, "pogrešan primalac");

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.Equal(EOtpremnicaFailureKind.CallerMistake, result.Error!.Kind);
        Assert.Equal(0, result.Error.HttpStatusCode);
        Assert.Empty(handler.Requests);
    }

    // ---- Helpers ----

    private static EOtpremnicaClient Client(FakeHandler handler) =>
        new(new HttpClient(handler),
            new EOtpremnicaCredentials
            {
                BaseUrl = "https://register.test/",
                ApiKey = "k",
                TimeoutSeconds = 1
            });

    private static FakeHandler Hanging() =>
        new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        });

    private static FakeHandler Throwing(HttpRequestError error) =>
        new((_, _) => throw new HttpRequestException(error, "refused", null));

    private static string Entries(string requestId, string status) =>
        $$"""{"items":[{"requestId":"{{requestId}}","status":"{{status}}"}]}""";

    private static HttpResponseMessage Json(string body) => Reply(200, body);

    private static HttpResponseMessage Reply(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    /// <summary>One part of a multipart body, read before the request is disposed.</summary>
    private sealed record Part(string? Name, string? FileName, string? ContentType, string Text);

    private sealed record Recorded(
        HttpMethod Method, Uri Uri, string? ApiKey, IReadOnlyList<Part> Parts);

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The client disposes the request after sending, so everything asserted is read now.
            var parts = new List<Part>();

            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    parts.Add(new Part(
                        part.Headers.ContentDisposition?.Name?.Trim('"'),
                        part.Headers.ContentDisposition?.FileName?.Trim('"'),
                        part.Headers.ContentType?.MediaType,
                        await part.ReadAsStringAsync(CancellationToken.None)));
                }
            }

            var apiKey = request.Headers.TryGetValues("Api-key", out var values)
                ? values.Single()
                : null;

            Requests.Add(new Recorded(request.Method, request.RequestUri!, apiKey, parts));

            return await respond(request, cancellationToken);
        }
    }
}
