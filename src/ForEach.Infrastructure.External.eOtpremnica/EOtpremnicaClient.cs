using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using ForEach.Infrastructure.External.eOtpremnica.Models;

namespace ForEach.Infrastructure.External.eOtpremnica;

/// <summary>
/// The Serbian central register of electronic delivery notes, per the official technical guide.
///
/// Confirmed from the guide: the host, the <c>Api-key</c> header, and that submission is a
/// multipart upload of a UBL file to <c>POST public/documents/requests</c> with a
/// <c>RequestId</c> field — not a JSON body. Acceptance there only means the request was queued;
/// the outcome arrives later as a DocumentRequest status, which is why sending and confirming are
/// two calls and why a host has to poll.
///
/// No logging, no retries, no caching: those are the host's, and a library that decided them would
/// be deciding them for every product that takes it.
/// </summary>
public sealed class EOtpremnicaClient(HttpClient httpClient, EOtpremnicaCredentials credentials)
{
    private const string RequestsPath = "public/documents/requests";
    private const string RequestChangesPath = "public/documents/requests/changes";

    /// <summary>Lower-case "key" is deliberate — that is what the guide specifies.</summary>
    private const string ApiKeyHeader = "Api-key";

    /// <summary>
    /// How many pages of one day's changes <see cref="GetStatusAsync"/> will walk before giving
    /// up and reporting Pending. A bound rather than "until empty" alone, because the page shape
    /// is not pinned down by the guide and a reader that never saw an empty page would otherwise
    /// loop. Twenty pages is far more than one host's documents on one day.
    /// </summary>
    public const int MaxStatusPages = 20;

    /// <summary>
    /// Submit a despatch advice.
    /// </summary>
    /// <param name="requestId">
    /// The caller's own idempotency key. The register tracks the queued request by it, so a resend
    /// after a timeout carrying the same value is findable rather than a second document.
    /// </param>
    /// <param name="xml">The UBL document, as <see cref="Ubl.DespatchAdviceUblWriter"/> wrote it.</param>
    /// <param name="ct">Cancels the call. A cancellation by the caller propagates; a timeout does not.</param>
    public async Task<EOtpremnicaResult<DespatchAdviceReceipt>> SendDespatchAdviceAsync(
        string xml, string requestId, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(requestId), "RequestId" }
        };

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/xml");

        // The file name is required by the multipart form: the register keys off RequestId, but a
        // nameless part is rejected by its model binder.
        content.Add(file, "File", $"despatch-advice-{requestId}.xml");

        using var request = new HttpRequestMessage(HttpMethod.Post, credentials.Endpoint(RequestsPath))
        {
            Content = content
        };

        var exchange = await ExchangeAsync(request, changesState: true, ct);

        if (!exchange.IsSuccess) return Carry<string, DespatchAdviceReceipt>(exchange);

        var body = exchange.Value!;

        // The register identifies the queued request by the id we chose, so that is what is
        // tracked when it names none of its own; the despatch advice gets an id once processing
        // succeeds.
        var documentId =
            EOtpremnicaResponseReader.ReadString(body, "id", "Id", "requestId", "RequestId")
            ?? requestId;

        var rawStatus =
            EOtpremnicaResponseReader.ReadString(body, "status", "Status")
            ?? "DocumentRequest.Pending";

        return EOtpremnicaResult<DespatchAdviceReceipt>.Ok(
            new DespatchAdviceReceipt(documentId, rawStatus, exchange.HttpStatusCode ?? 200, body),
            body, exchange.HttpStatusCode);
    }

    /// <summary>
    /// What the register says about one document.
    /// </summary>
    /// <param name="documentId">The id the register tracks the request by — the requestId the caller sent, or the id in the receipt.</param>
    /// <param name="onDate">
    /// The day to read changes for. There is no per-request status endpoint — the register exposes
    /// changes for a whole day, and the caller's request is located within that page.
    ///
    /// Passed in rather than read from a clock here, because which day it is is a business question
    /// this library cannot answer: it would answer it in UTC, and for two hours every evening that
    /// is yesterday in Belgrade.
    /// </param>
    /// <param name="ct">Cancels the call. A cancellation by the caller propagates; a timeout does not.</param>
    public async Task<EOtpremnicaResult<DespatchAdviceStatusReport>> GetStatusAsync(
        string documentId, DateOnly onDate, CancellationToken ct = default)
    {
        EOtpremnicaResult<string>? last = null;

        // The day's changes are paged, and a busy day puts this document on a later page. Reading
        // only the first would report Pending forever for a document the register had long since
        // processed, which is the quiet failure this whole class is written to avoid. So: walk the
        // pages until the document turns up, until a page carries no entries, or until the bound.
        for (var page = 0; page < MaxStatusPages; page++)
        {
            var exchange = await ReadChangesPageAsync(onDate, page, ct);

            if (!exchange.IsSuccess) return Carry<string, DespatchAdviceStatusReport>(exchange);

            last = exchange;

            var body = exchange.Value!;

            var status = EOtpremnicaResponseReader.FindRequestStatus(body, documentId);

            if (status is not null)
            {
                return EOtpremnicaResult<DespatchAdviceStatusReport>.Ok(
                    new DespatchAdviceStatusReport(status, exchange.HttpStatusCode ?? 200, body),
                    body, exchange.HttpStatusCode);
            }

            if (!EOtpremnicaResponseReader.HasEntries(body)) break;
        }

        // Nothing on any page mentions this document, which is what the register means by "not
        // processed yet" — and is not an error. The body kept is the last page read.
        var lastBody = last!.Value!;

        return EOtpremnicaResult<DespatchAdviceStatusReport>.Ok(
            new DespatchAdviceStatusReport(
                "DocumentRequest.Pending", last.HttpStatusCode ?? 200, lastBody),
            lastBody, last.HttpStatusCode);
    }

    private async Task<EOtpremnicaResult<string>> ReadChangesPageAsync(
        DateOnly onDate, int page, CancellationToken ct)
    {
        var date = onDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pageNumber = page.ToString(CultureInfo.InvariantCulture);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            credentials.Endpoint($"{RequestChangesPath}?date={date}&page={pageNumber}"));

        return await ExchangeAsync(request, changesState: false, ct);
    }

    /// <summary>
    /// One authenticated call, for a "test the connection" button.
    /// </summary>
    /// <remarks>
    /// A real request rather than a reachability check: what somebody needs to know after typing a
    /// key is whether the far end ACCEPTS it, and a URL that resolves proves nothing about
    /// credentials. Reading one day of changes is the cheapest call that proves the key without
    /// putting anything into the register.
    /// </remarks>
    public Task<EOtpremnicaResult<string>> ProbeAsync(
        DateOnly today, CancellationToken ct = default) =>
        // One page, not a status lookup: a probe asks whether the key works, and walking every
        // page of a busy day to look for a document that does not exist would answer that twenty
        // times over. The value is the page body as the register sent it.
        ReadChangesPageAsync(today, 0, ct);

    /// <summary>
    /// Withdrawal, which this library does not do — and says so rather than pretending.
    ///
    /// A cancellation is not a call on the requests endpoint: it is itself a UBL Application
    /// Response submitted through the same endpoint. So sending one is a document to build, and
    /// until it is built, a caller that got a success here would record a withdrawal that never
    /// happened.
    /// </summary>
    public Task<EOtpremnicaResult<DespatchAdviceStatusReport>> CancelAsync(
        string documentId, string reason, CancellationToken ct = default) =>
        Task.FromResult(EOtpremnicaResult<DespatchAdviceStatusReport>.Fail(
            EOtpremnicaError.Local(
                "Povlačenje otpremnice zahteva slanje Application Response dokumenta, što još "
                + "nije podržano.",
                [])));

    /// <summary>
    /// One request, and the decision this whole library exists to get right.
    /// </summary>
    /// <param name="changesState">
    /// True for a submission. A timeout — or a 5xx, which means the register was reached and
    /// failed inside it — then produces <see cref="EOtpremnicaOutcome.Unknown"/> rather than a
    /// failure: the request may be queued, and a resend would be a second record of goods that
    /// left once. False for a read, where the same timeout is an ordinary retryable failure.
    /// </param>
    /// <param name="request">The request to send; the key and Accept headers are added here.</param>
    /// <param name="ct">The caller's token, kept apart from the timeout so the two can be told apart.</param>
    private async Task<EOtpremnicaResult<string>> ExchangeAsync(
        HttpRequestMessage request, bool changesState, CancellationToken ct)
    {
        request.Headers.Add(ApiKeyHeader, credentials.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        try
        {
            // The configured timeout, applied per call: HttpClient.Timeout is fixed when the client
            // is created, and a host that lets somebody edit the number while the service runs
            // needs it enforced on the request they typed it for.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);

            timeout.CancelAfter(Timeout);

            using var response = await httpClient.SendAsync(request, timeout.Token);

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
                return EOtpremnicaResult<string>.Ok(body, body, code);

            if (changesState && code >= 500)
            {
                return EOtpremnicaResult<string>.Unknown(
                    $"eOtpremnica returned {code}: {Summarise(body)}", code, body);
            }

            return EOtpremnicaResult<string>.Fail(
                new EOtpremnicaError(code, null, Summarise(body), []), body);
        }
        // OperationCanceledException rather than only TaskCanceledException: SendAsync throws the
        // latter on a timeout, but a timeout while the body is still being read throws the base
        // type, and a submission whose reply was cut off is exactly the case that must come back
        // as Unknown rather than as an exception.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return changesState
                ? EOtpremnicaResult<string>.Unknown("eOtpremnica nije odgovorila na vreme.")
                : EOtpremnicaResult<string>.Transport("eOtpremnica nije odgovorila na vreme.");
        }
        catch (HttpRequestException exception)
        {
            var message = $"eOtpremnica nije dostupna: {exception.Message}";

            // A connection that never opened changed nothing, whatever the request was for: the
            // name did not resolve, the port refused, the TLS handshake failed. Nothing was sent,
            // so nothing can be queued, and the right answer is a retryable failure — an Unknown
            // here would forbid the resend and leave the host polling for a document the register
            // never received. A connection cut after it opened is the other case: the register may
            // have read the request, and on a submission that is Unknown.
            return changesState && !NeverConnected(exception)
                ? EOtpremnicaResult<string>.Unknown(message)
                : EOtpremnicaResult<string>.Transport(message);
        }
    }

    private static bool NeverConnected(HttpRequestException exception) =>
        exception.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError;

    private TimeSpan Timeout =>
        TimeSpan.FromSeconds(Math.Clamp(credentials.TimeoutSeconds, 1, 600));

    /// <summary>
    /// Carries a failure across a change of type, Unknown included.
    ///
    /// Its own method because losing the Unknown here is the one mistake in this file that costs
    /// something: it would turn "the register may have it" into "it failed", which reads as
    /// permission to send again.
    /// </summary>
    private static EOtpremnicaResult<TTo> Carry<TFrom, TTo>(EOtpremnicaResult<TFrom> result) =>
        result.Outcome switch
        {
            EOtpremnicaOutcome.Unknown => EOtpremnicaResult<TTo>.Unknown(
                result.FailureMessage, result.HttpStatusCode, result.ResponseBody),

            _ => result.Error is { } error
                ? EOtpremnicaResult<TTo>.Fail(error, result.ResponseBody)
                : EOtpremnicaResult<TTo>.Transport(result.FailureMessage)
        };

    private static string Summarise(string body) => body.Length <= 300 ? body : body[..300] + "…";
}
