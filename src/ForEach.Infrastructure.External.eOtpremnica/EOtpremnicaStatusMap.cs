using ForEach.Infrastructure.External.eOtpremnica.Models;

namespace ForEach.Infrastructure.External.eOtpremnica;

/// <summary>
/// The register's status words, read.
///
/// Confirmed from the technical guide: the changes page reports a DocumentRequest state, spelled
/// with its prefix — "DocumentRequest.Pending" and not "Pending". Matched case-insensitively and
/// trimmed, because a status arriving as "documentrequest.succeeded " is the same answer.
/// </summary>
public static class EOtpremnicaStatusMap
{
    /// <summary>
    /// The status, or null for a word this map does not know.
    ///
    /// Null rather than a fallback, and deliberately so: what to do about an unrecognised status
    /// is a decision with consequences a host owns. On a submission, guessing "queued" is usually
    /// right and guessing "failed" invites a duplicate; on a refresh, guessing anything overwrites
    /// a state the register already gave. This library cannot tell those apart, and the host can.
    /// </summary>
    public static DespatchAdviceStatus? Known(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : raw.Trim().ToLowerInvariant() switch
            {
                "documentrequest.pending" => DespatchAdviceStatus.Pending,
                "documentrequest.succeeded" => DespatchAdviceStatus.Succeeded,
                "documentrequest.failed" => DespatchAdviceStatus.Failed,
                _ => null
            };
}
