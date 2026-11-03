namespace ForEach.Infrastructure.External.eOtpremnica.Ubl;

/// <summary>
/// An address as the document states it.
/// </summary>
/// <param name="Street">Street name, or null when the host does not know it.</param>
/// <param name="City">City name, or null.</param>
/// <param name="PostalZone">Postal code, or null.</param>
/// <param name="CountryCode">
/// ISO 3166-1 alpha-2. Carried rather than assumed, even though every address on a domestic
/// despatch advice is RS: a library that hardcoded it would be wrong the first time goods leave
/// for a warehouse across the border, and wrong silently.
/// </param>
public sealed record DespatchAddress(
    string? Street, string? City, string? PostalZone, string CountryCode = "RS");

/// <summary>
/// A party on the document — who sent the goods, and who receives them.
/// </summary>
/// <param name="Name">The registered name. Required; a party with no name is nobody.</param>
/// <param name="VatNumber">
/// The identifier the register knows them by. Which identifiers are acceptable — PIB, JBKJS, a
/// foreign registration — is the host's rule and is applied before mapping; this library only
/// refuses a party with no identifier at all, neither this nor <paramref name="RegistrationNumber"/>.
/// </param>
/// <param name="RegistrationNumber">The company registration number (matični broj), or null.</param>
/// <param name="Address">The party's registered address.</param>
public sealed record DespatchParty(
    string Name, string? VatNumber, string? RegistrationNumber, DespatchAddress Address);

/// <summary>
/// One line: what left, how much of it, and in what unit.
/// </summary>
/// <param name="Description">What the goods are, as the host names them.</param>
/// <param name="Unit">
/// As the host spells it. The writer translates it into a UN/ECE Recommendation 20 code and
/// refuses a spelling it does not know, because declaring five hundred kilograms as five hundred
/// items is a record of a despatch that did not happen the way it is written down.
/// </param>
/// <param name="Quantity">How much left. Must be positive; a despatch advice reports goods that went.</param>
/// <param name="OrderLineId">
/// The line of the order this despatch line fulfils. UBL 2.1 requires every despatch line to
/// reference an order line (<c>cac:OrderLineReference/cbc:LineID</c> is mandatory in the schema),
/// so when the host has none the writer falls back to the despatch line's own number — which is
/// what the register's customization expects on a delivery note that answers no order.
/// </param>
public sealed record DespatchLine(
    string Description, string Unit, decimal Quantity, string? OrderLineId = null);

/// <summary>
/// Which way the goods are moving, which is what the register's document-type code says.
///
/// **A return to a supplier is not an otpremnica with a minus on it.** It travels the other way,
/// it is a separate obligation under the pravilnik, and the register distinguishes the two by
/// code. Sending a return under the despatch code registers a delivery that did not happen and
/// leaves the return unregistered — two errors from one wrong literal.
/// </summary>
public enum DespatchAdviceKind
{
    /// <summary>Goods leaving us for a customer. Code 9 on the UBL list.</summary>
    Despatch,

    /// <summary>
    /// Goods going back to the supplier they came from — povraćaj robe dobavljaču.
    ///
    /// **The code for this is not in this library and is deliberately not guessed.** It is on the
    /// ministry's list beside the customization id, both of which are dated revisions; a wrong
    /// three-digit literal here would produce a document the register accepts as something else.
    /// It is configured, and writing a return without it is refused by name.
    /// </summary>
    Return
}

/// <summary>
/// Everything the writer needs, and nothing about the host that produced it.
/// </summary>
/// <param name="Number">The document number. Becomes <c>cbc:ID</c>, by which the register knows it.</param>
/// <param name="IssueDate">When the document was issued.</param>
/// <param name="DeliveryDate">
/// When the goods travel. Stated as an estimated delivery period of one day, which is how the
/// register's customization expresses it on a document with no window of its own.
/// </param>
/// <param name="Despatcher">Who sent the goods.</param>
/// <param name="Consignee">Who receives them.</param>
/// <param name="DeliverTo">
/// Where the goods are going, which is not always the consignee's registered address — a customer
/// invoiced at head office takes delivery at a café.
/// </param>
/// <param name="Lines">What left. At least one.</param>
/// <param name="DespatchFrom">
/// Where they left from, or null when the host does not know. Null omits the element rather than
/// writing an empty one: an address block with no address in it is a claim about a place.
/// </param>
/// <param name="CarrierName">The carrier, when the goods do not travel on the despatcher's own vehicle.</param>
/// <param name="VehiclePlate">The vehicle's licence plate, or null.</param>
/// <param name="DriverName">The driver, or null.</param>
/// <param name="OrderReference">The customer's order this despatch answers, or null.</param>
/// <param name="Note">Free text carried on the document, or null.</param>
/// <param name="Kind">
/// Defaulted to <c>Despatch</c>, which is what every caller today sends and what the register
/// means by an otpremnica. A return has to be asked for.
/// </param>
public sealed record DespatchAdvice(
    string Number,
    DateOnly IssueDate,
    DateOnly DeliveryDate,
    DespatchParty Despatcher,
    DespatchParty Consignee,
    DespatchAddress DeliverTo,
    IReadOnlyList<DespatchLine> Lines,
    DespatchAddress? DespatchFrom = null,
    string? CarrierName = null,
    string? VehiclePlate = null,
    string? DriverName = null,
    string? OrderReference = null,
    string? Note = null,
    DespatchAdviceKind Kind = DespatchAdviceKind.Despatch);

/// <summary>
/// What is true of every document this host registers rather than of one despatch.
/// </summary>
/// <param name="CustomizationId">
/// The register's identifier for the document profile. Versioned by the ministry and configured by
/// the host, because the day it changes is a configuration change and not a release of this
/// library — and a document carrying the wrong one fails validation outright.
/// </param>
/// <param name="ReturnTypeCode">
/// The register's document-type code for a return, when the host has been told what it is.
///
/// **Null is the honest default and is why this is not a hardcoded literal.** The code lives on
/// the ministry's list beside the customization id above, it is a dated revision like that one,
/// and this library does not know it. A guessed three-digit code would not fail — it would
/// register the movement as some other kind of document, which is worse than a refusal because
/// nobody would look for it.
/// </param>
public sealed record DespatchAdviceUblOptions(
    string CustomizationId, string? ReturnTypeCode = null);
