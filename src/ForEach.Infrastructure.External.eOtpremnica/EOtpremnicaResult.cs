namespace ForEach.Infrastructure.External.eOtpremnica;

/// <summary>
/// How a call to the register ended, in the three states a caller must tell apart.
/// </summary>
public enum EOtpremnicaOutcome
{
    /// <summary>The register accepted the call.</summary>
    Succeeded,

    /// <summary>It did not happen. Safe to try again once whatever caused it is dealt with.</summary>
    Failed,

    /// <summary>
    /// Nobody knows. A submission timed out, the connection was cut, or the register answered 5xx
    /// after reading the request — the despatch advice may be queued there and may not, and the
    /// one thing a caller must not do is send it again.
    /// </summary>
    Unknown
}

/// <summary>What a caller should DO about a failure.</summary>
public enum EOtpremnicaFailureKind
{
    /// <summary>The API key, or the wrong environment. It repeats until somebody changes it.</summary>
    Configuration,

    /// <summary>The document or the request was wrong. The register read it and declined it.</summary>
    CallerMistake,

    /// <summary>Register-side or a rate limit. The next attempt will probably work.</summary>
    Transient
}

/// <summary>
/// What the register says when it refuses.
/// </summary>
/// <param name="Details">
/// Its validation messages, verbatim and in order. On a rejected despatch advice this is the whole
/// of the useful content — a summary of it tells the person who has to fix the document nothing.
/// </param>
/// <param name="HttpStatusCode">The status the register answered with, or zero for a refusal decided locally.</param>
/// <param name="Code">The register's own error code, or null when it sent none.</param>
/// <param name="Message">One sentence describing the refusal.</param>
public sealed record EOtpremnicaError(
    int HttpStatusCode, string? Code, string Message, IReadOnlyList<string> Details)
{
    /// <summary>
    /// A refusal decided here rather than by the register — a document that never left this
    /// machine. Status zero because there was no request, and inventing a 400 would put a status
    /// in the stored response body that the register never sent.
    /// </summary>
    public static EOtpremnicaError Local(string message, IReadOnlyList<string> details) =>
        new(0, "InvalidDocument", message, details);

    /// <summary>What a caller should do about this refusal, derived from the status.</summary>
    public EOtpremnicaFailureKind Kind => HttpStatusCode switch
    {
        // Never sent, and wrong here. Retrying would present the same document again.
        0 => EOtpremnicaFailureKind.CallerMistake,

        401 or 403 => EOtpremnicaFailureKind.Configuration,

        // The register read the request and declined it — a document that does not validate, or an
        // action its state does not allow. Sending it again sends the same thing.
        400 or 404 or 409 or 422 => EOtpremnicaFailureKind.CallerMistake,

        408 or 429 => EOtpremnicaFailureKind.Transient,

        // Note that a 5xx is Transient as a *kind* but produces an Unknown outcome on a
        // submission: the register was reached and failed inside it, so a request accepted just
        // before it fell over cannot be told from one that never ran. The kind says "this will
        // probably work later"; the outcome says "do not assume it did not happen".
        >= 500 => EOtpremnicaFailureKind.Transient,

        // Same reasoning as the other libraries: an unrecognised failure is retried, because a
        // wasted attempt costs one scheduled run and giving up costs a despatch nobody registered.
        _ => EOtpremnicaFailureKind.Transient
    };

    /// <summary>The refusal as one line: status, code, message, then the details.</summary>
    public override string ToString()
    {
        if (HttpStatusCode == 0)
            return Details.Count == 0 ? Message : $"{Message} — {string.Join("; ", Details)}";

        var head = Code is { Length: > 0 } code
            ? $"eOtpremnica {HttpStatusCode} ({code}): {Message}"
            : $"eOtpremnica {HttpStatusCode}: {Message}";

        return Details.Count == 0 ? head : $"{head} — {string.Join("; ", Details)}";
    }
}

/// <summary>
/// The outcome of one call to the register.
///
/// Its own type rather than FluentResults, like the other ForEach libraries: no third-party
/// dependency, so a host on a different result type adapts it in a dozen lines. Deliberately the
/// same shape as SEF's — the two systems are siblings, and a host that has integrated one should
/// recognise the other without reading it twice.
/// </summary>
/// <remarks>
/// <see cref="ResponseBody"/> is kept on every outcome, success included, because the host stores
/// it against the submission: "what did the register actually say" is a question with one correct
/// answer months later.
/// </remarks>
public sealed class EOtpremnicaResult<T>
{
    private EOtpremnicaResult(
        EOtpremnicaOutcome outcome, T? value, EOtpremnicaError? error, string? transportFailure,
        int? httpStatusCode, string? responseBody)
    {
        Outcome = outcome;
        Value = value;
        Error = error;
        TransportFailure = transportFailure;
        HttpStatusCode = httpStatusCode;
        ResponseBody = responseBody;
    }

    /// <summary>How the call ended.</summary>
    public EOtpremnicaOutcome Outcome { get; }

    /// <summary>What the call returned. Set only on success.</summary>
    public T? Value { get; }

    /// <summary>The register's own refusal. Null on success, and null when nothing answered.</summary>
    public EOtpremnicaError? Error { get; }

    /// <summary>Set when the call did not get an answer — timeout, DNS, TLS, a cut connection.</summary>
    public string? TransportFailure { get; }

    /// <summary>The status the register answered with. Null when nothing answered.</summary>
    public int? HttpStatusCode { get; }

    /// <summary>Whatever came back, unparsed. Kept on every outcome; see the remarks above.</summary>
    public string? ResponseBody { get; }

    /// <summary>Whether the call succeeded.</summary>
    public bool IsSuccess => Outcome == EOtpremnicaOutcome.Succeeded;

    /// <summary>
    /// One sentence, in the register's words where there are any. The host writes this onto the
    /// submission row and shows it to whoever pressed send.
    /// </summary>
    public string FailureMessage => Error?.ToString() ?? TransportFailure ?? "";

    /// <summary>
    /// Whether trying again is safe and worth it. False for Unknown, always: the despatch advice
    /// may be queued at the register, and a second one is a second record of goods that left once.
    /// </summary>
    public bool IsWorthRetrying =>
        Outcome == EOtpremnicaOutcome.Failed
        && (TransportFailure is not null || Error?.Kind == EOtpremnicaFailureKind.Transient);

    /// <summary>A call that succeeded.</summary>
    public static EOtpremnicaResult<T> Ok(T value, string? responseBody = null, int? httpStatusCode = null) =>
        new(EOtpremnicaOutcome.Succeeded, value, null, null, httpStatusCode, responseBody);

    /// <summary>A call the register answered with a refusal.</summary>
    public static EOtpremnicaResult<T> Fail(EOtpremnicaError error, string? responseBody = null) =>
        new(EOtpremnicaOutcome.Failed, default, error, null, error.HttpStatusCode, responseBody);

    /// <summary>A call that never got an answer, on a request that changed nothing.</summary>
    public static EOtpremnicaResult<T> Transport(string message) =>
        new(EOtpremnicaOutcome.Failed, default, null, message, null, null);

    /// <summary>
    /// A call whose effect is unknown: a submission that timed out, or one the register answered
    /// 5xx to after reading it. Everything a host does about it — polling by requestId, refusing
    /// to resend — hangs off this one distinction.
    /// </summary>
    public static EOtpremnicaResult<T> Unknown(
        string message, int? httpStatusCode = null, string? responseBody = null) =>
        new(EOtpremnicaOutcome.Unknown, default, null, message, httpStatusCode, responseBody);
}
