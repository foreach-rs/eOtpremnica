namespace ForEach.Infrastructure.External.eOtpremnica.Models;

/// <summary>
/// What the register returns when it accepts a submission.
/// </summary>
/// <param name="DocumentId">
/// The register's own identifier for the queued request, falling back to the requestId the caller
/// chose when the reply does not name one. That fallback is what makes a resend after a timeout
/// findable: the caller can ask about the id it sent.
/// </param>
/// <param name="RawStatus">
/// The register's own word, unmapped. A host translates it into whatever its column holds — and
/// keeping the raw string is what lets it recognise a status this library has never heard of
/// rather than turning it into a guess.
/// </param>
/// <param name="HttpStatusCode">The status of the reply.</param>
/// <param name="ResponseBody">The reply as the register sent it, for the host to store.</param>
public sealed record DespatchAdviceReceipt(
    string DocumentId, string RawStatus, int HttpStatusCode, string ResponseBody);

/// <summary>
/// What the register says about a document now.
/// </summary>
/// <param name="RawStatus">
/// Unmapped, for the same reason as on the receipt. "DocumentRequest.Pending" when the changes
/// page says nothing about this document — which is genuinely what the register means by not
/// mentioning it yet, and is not the same as an error.
/// </param>
/// <param name="HttpStatusCode">The status of the reply.</param>
/// <param name="ResponseBody">The reply as the register sent it, for the host to store.</param>
public sealed record DespatchAdviceStatusReport(
    string RawStatus, int HttpStatusCode, string ResponseBody);
