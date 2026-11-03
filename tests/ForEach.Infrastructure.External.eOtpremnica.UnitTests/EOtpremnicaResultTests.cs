using ForEach.Infrastructure.External.eOtpremnica;

namespace ForEach.Infrastructure.External.eOtpremnica.UnitTests;

/// <summary>
/// The three ways a call to the register ends, and the one this register produces more often than
/// its sibling does.
///
/// Registering a despatch is not idempotent: send the same advice twice and the register holds two
/// records of goods that left once. So a timeout on a submission is not a failure — it is an
/// unknown — and so, here, is a 5xx: the register was reached and failed inside it, and a request
/// accepted just before it fell over cannot be told from one that never ran.
/// </summary>
public class EOtpremnicaResultTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void A_key_problem_is_configuration(int status) =>
        Assert.Equal(EOtpremnicaFailureKind.Configuration, Error(status).Kind);

    [Theory]
    [InlineData(400)]   // UBL that does not validate
    [InlineData(404)]   // a document the register does not know
    [InlineData(409)]
    [InlineData(422)]
    public void A_document_or_a_request_the_register_declined_is_ours_to_fix(int status)
    {
        var error = Error(status);

        Assert.Equal(EOtpremnicaFailureKind.CallerMistake, error.Kind);

        // And never retried: the same document would be sent again, and declined again.
        Assert.False(EOtpremnicaResult<string>.Fail(error).IsWorthRetrying);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(418)]   // nobody wrote a row for this one; retrying is the cheaper mistake
    public void A_register_side_problem_is_worth_waiting_out(int status)
    {
        var error = Error(status);

        Assert.Equal(EOtpremnicaFailureKind.Transient, error.Kind);
        Assert.True(EOtpremnicaResult<string>.Fail(error).IsWorthRetrying);
    }

    [Fact]
    public void An_unknown_outcome_is_never_retried()
    {
        // The rule this type exists for. The advice may be queued; sending it again would put a
        // second record of the same despatch in a state register.
        var result = EOtpremnicaResult<string>.Unknown("eOtpremnica nije odgovorila na vreme.");

        Assert.Equal(EOtpremnicaOutcome.Unknown, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsWorthRetrying);
    }

    [Fact]
    public void An_unknown_outcome_keeps_what_the_register_said_and_the_status_it_said_it_with()
    {
        // A 5xx on a submission is Unknown, and the host stores the body against the submission
        // row — so somebody reconciling by hand next morning can see what came back rather than
        // only that something did.
        var result = EOtpremnicaResult<string>.Unknown(
            "eOtpremnica returned 503: …", httpStatusCode: 503, responseBody: "{\"error\":\"busy\"}");

        Assert.Equal(503, result.HttpStatusCode);
        Assert.Contains("busy", result.ResponseBody!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_that_never_reached_the_register_is_a_plain_failure()
    {
        // The other half of the distinction: nothing was sent, so nothing was registered, and
        // trying again is both safe and the right thing to do.
        var result = EOtpremnicaResult<string>.Transport("No such host is known.");

        Assert.Equal(EOtpremnicaOutcome.Failed, result.Outcome);
        Assert.True(result.IsWorthRetrying);
        Assert.Null(result.Error);
    }

    [Fact]
    public void A_refusal_decided_here_reads_as_itself_rather_than_as_an_HTTP_answer()
    {
        // Status zero means no request was made. Printing "eOtpremnica 0:" in front of the message
        // would attribute this application's own refusal to the register.
        var error = EOtpremnicaError.Local(
            "Povlačenje otpremnice zahteva Application Response dokument.", []);

        Assert.Equal(EOtpremnicaFailureKind.CallerMistake, error.Kind);
        Assert.StartsWith("Povlačenje", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("eOtpremnica 0", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_response_body_is_kept_even_on_success()
    {
        // The host stores it. "What did the register actually say when this was registered" has
        // one correct answer months later.
        var result = EOtpremnicaResult<string>.Ok(
            "value", responseBody: """{"id":"REQ-1"}""", httpStatusCode: 200);

        Assert.True(result.IsSuccess);
        Assert.Contains("REQ-1", result.ResponseBody!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_environment_is_part_of_the_credentials_rather_than_a_flag_beside_them()
    {
        var demo = new EOtpremnicaCredentials
        {
            BaseUrl = EOtpremnicaCredentials.DemoBaseUrl, ApiKey = "k"
        };

        var live = new EOtpremnicaCredentials
        {
            BaseUrl = EOtpremnicaCredentials.ProductionBaseUrl, ApiKey = "k"
        };

        Assert.False(demo.IsProduction);
        Assert.True(live.IsProduction);

        // And the demo host is not accidentally the production one: they differ by four characters,
        // which is exactly the kind of constant a copy-paste gets wrong.
        Assert.NotEqual(EOtpremnicaCredentials.DemoBaseUrl, EOtpremnicaCredentials.ProductionBaseUrl);

        Assert.True(demo.IsConfigured);
        Assert.False(new EOtpremnicaCredentials { BaseUrl = EOtpremnicaCredentials.DemoBaseUrl }.IsConfigured);
    }

    [Fact]
    public void An_endpoint_address_survives_a_trailing_slash_on_either_side()
    {
        var credentials = new EOtpremnicaCredentials
        {
            BaseUrl = "https://gateway.local/eot/", ApiKey = "k"
        };

        // A base carrying a path keeps it. `new Uri(base, relative)` would eat "eot" — which is the
        // trap the HRC-side request-shape tests were written for.
        Assert.Equal(
            "https://gateway.local/eot/public/documents/requests",
            credentials.Endpoint("/public/documents/requests").ToString());
    }

    private static EOtpremnicaError Error(int status) => new(status, null, "…", []);
}
