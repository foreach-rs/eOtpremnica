namespace ForEach.Infrastructure.External.eOtpremnica.Models;

/// <summary>
/// What the register says has become of a submitted request, in its own vocabulary.
///
/// Its own words rather than a host's: the register reports on a <em>DocumentRequest</em>, and
/// three states is all it has. A host that models delivery notes as sent / accepted / rejected
/// translates these itself, because what "Pending" means for its own column — still sent, or not
/// yet anything — is its decision and not this library's.
/// </summary>
public enum DespatchAdviceStatus
{
    /// <summary>
    /// Queued for processing. The register has the document; the outcome is not known yet.
    ///
    /// The important one: it is not a failure, and treating it as one invites a resend and a
    /// second record of goods that left once.
    /// </summary>
    Pending,

    /// <summary>Processed and registered.</summary>
    Succeeded,

    /// <summary>
    /// A processing error, with the reason in the response's `description` field — which is why a
    /// host stores the whole response body rather than only the status.
    /// </summary>
    Failed
}
