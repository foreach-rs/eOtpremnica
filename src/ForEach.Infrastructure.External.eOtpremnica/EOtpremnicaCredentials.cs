namespace ForEach.Infrastructure.External.eOtpremnica;

/// <summary>
/// What the register needs on every call: an address and a key.
///
/// The key goes in an <c>Api-key</c> header — lower-case "key" is deliberate, that is what the
/// official technical guide specifies — and HTTPS is the whole of the protection. Their design
/// rather than a simplification here, and the same arrangement SEF uses.
///
/// The environment is part of the credentials rather than a flag beside them, because the key and
/// the address belong together: a demo key against production is refused with the same 401 as a
/// revoked one.
/// </summary>
public sealed class EOtpremnicaCredentials
{
    /// <summary>
    /// The live register. A despatch advice sent here is a real record.
    ///
    /// The production host is the demo one without "demo" — stated here rather than left to a
    /// deployer to type, because the two differ by four characters and the wrong one is a
    /// document filed in the wrong place or a key rejected for no visible reason.
    /// </summary>
    public const string ProductionBaseUrl = "https://api.eotpremnica.mfin.gov.rs";

    /// <summary>
    /// The demo register. A separate account, a separate key, and nothing sent to it is real —
    /// which is what makes it the only place to try a document shape for the first time.
    /// </summary>
    public const string DemoBaseUrl = "https://api.demoeotpremnica.mfin.gov.rs";

    /// <summary>The register's address — <see cref="ProductionBaseUrl"/> or <see cref="DemoBaseUrl"/>.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Sent as the `Api-key` header. A secret, and treated as one by whoever binds it.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Sixty seconds by default. Generous on purpose: a submission that times out is the one
    /// failure with no safe recovery, so waiting is cheaper than not knowing.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Both present. Nothing here checks whether the key is CORRECT — only the register can say
    /// that, and it says it as a 401. What this answers is whether a call is worth making at all,
    /// so a host can run dark until somebody has been issued a key.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Whether this is the live register, by address. Worth asking before a destructive test.
    /// </summary>
    public bool IsProduction =>
        BaseUrl.TrimEnd('/').Equals(ProductionBaseUrl, StringComparison.OrdinalIgnoreCase);

    /// <summary>One endpoint's address, with the slashes sorted out once.</summary>
    public Uri Endpoint(string path) => new($"{BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
}
