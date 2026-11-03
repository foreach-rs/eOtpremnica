using System.Xml.Linq;
using ForEach.Infrastructure.External.eOtpremnica.Ubl;

namespace ForEach.Infrastructure.External.eOtpremnica.UnitTests;

/// <summary>
/// A7. Which document type the register is told this is.
///
/// **A return to a supplier is not an otpremnica with a minus on it.** The goods travel the other
/// way, it is a separate obligation under the pravilnik, and the register tells the two apart by a
/// code in one element. Sending a return under the despatch code is the worst kind of wrong: it is
/// *accepted*, so it registers a delivery that did not happen and leaves the return unregistered,
/// and nothing anywhere reports an error.
///
/// The code for a return is not in this library and is deliberately not guessed — it is on the
/// ministry's list beside the customization id, and both are dated revisions. So it is configured,
/// and a return written without it is refused by name rather than silently downgraded to a
/// despatch.
/// </summary>
public class DespatchAdviceKindTests
{
    private static readonly DespatchAdviceUblOptions Plain = new("urn:test:1");

    private static readonly DespatchAdviceUblOptions WithReturnCode = new("urn:test:1", "632");

    [Fact]
    public void A_despatch_carries_the_despatch_code()
    {
        var xml = XDocument.Parse(Written(Advice(), Plain));

        Assert.Equal("9", TypeCode(xml));
    }

    [Fact]
    public void A_return_carries_the_configured_code_rather_than_the_despatch_one()
    {
        var xml = XDocument.Parse(
            Written(Advice() with { Kind = DespatchAdviceKind.Return }, WithReturnCode));

        Assert.Equal("632", TypeCode(xml));
    }

    [Fact]
    public void A_return_with_no_configured_code_is_refused_rather_than_sent_as_a_despatch()
    {
        var result = DespatchAdviceUblWriter.Write(
            Advice() with { Kind = DespatchAdviceKind.Return }, Plain);

        Assert.False(result.IsSuccess);

        // Named, and it says where the code comes from. The person reading this refusal is being
        // asked to look something up in a pravilnik, which is not a thing to leave them guessing.
        Assert.Contains(
            result.Error!.Details,
            detail => detail.Contains("povraćaj", StringComparison.Ordinal));
    }

    [Fact]
    public void A_despatch_is_unaffected_by_the_missing_return_code()
    {
        // The check must not leak onto the ordinary case: every document sent today is a despatch,
        // and a rule that asked them all for a return code would stop the integration working.
        var result = DespatchAdviceUblWriter.Write(Advice(), Plain);

        Assert.True(result.IsSuccess);
    }

    // ---- fixture -----------------------------------------------------------------------------

    private static string Written(DespatchAdvice advice, DespatchAdviceUblOptions options)
    {
        var result = DespatchAdviceUblWriter.Write(advice, options);

        Assert.True(result.IsSuccess, result.Error?.Message ?? result.TransportFailure);

        return result.Value!;
    }

    private static string? TypeCode(XDocument xml) =>
        xml.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "DespatchAdviceTypeCode")
            ?.Value;

    private static DespatchAdvice Advice() => new(
        "OTP-1/2026",
        new DateOnly(2026, 8, 31),
        new DateOnly(2026, 8, 31),
        new DespatchParty("HRC d.o.o.", "100000001", "20000001", Address()),
        new DespatchParty("Kafić Centar", "100000002", "20000002", Address()),
        Address(),
        [new DespatchLine("Šećer 5g", "kg", 500m)]);

    private static DespatchAddress Address() =>
        new("Knez Mihailova", "Beograd", "11000");
}
