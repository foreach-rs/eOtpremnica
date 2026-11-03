using System.Globalization;
using System.Xml.Linq;

namespace ForEach.Infrastructure.External.eOtpremnica.Ubl;

/// <summary>
/// Writes the UBL 2.1 Despatch Advice the register expects.
///
/// Confirmed from the official technical guide: the register models an e-otpremnica as a UBL
/// Despatch Advice, and the document must carry the CustomizationID it supplies or it fails
/// validation outright.
///
/// The element bindings follow the UBL 2.1 schema (element order and the elements it requires,
/// such as an order line reference on every line) rather than the guide's field-by-field mapping,
/// which runs to a dozen pages and has not been transcribed. Validate a generated document against
/// the demo register before relying on it.
///
/// A pure function on purpose: the payload can be reviewed and corrected long before credentials
/// exist, and the whole document can be asserted in unit tests without a network.
/// </summary>
public static class DespatchAdviceUblWriter
{
    private static readonly XNamespace Despatch =
        "urn:oasis:names:specification:ubl:schema:xsd:DespatchAdvice-2";
    private static readonly XNamespace Cac =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    /// <summary>
    /// The document, or the reasons it cannot be written.
    ///
    /// A refusal carries every problem found rather than the first: whoever fixes it is looking at
    /// one delivery note, and telling them about one line at a time turns a correction into four.
    /// </summary>
    public static EOtpremnicaResult<string> Write(
        DespatchAdvice advice, DespatchAdviceUblOptions options)
    {
        var problems = Check(advice);

        // A return with no configured code is refused here rather than written under the despatch
        // code. The second would be accepted by the register and would mean the wrong thing —
        // a delivery recorded that did not happen, and a return that was never registered.
        if (advice.Kind == DespatchAdviceKind.Return
            && string.IsNullOrWhiteSpace(options.ReturnTypeCode))
        {
            problems.Add(
                "Šifra vrste dokumenta za povraćaj nije podešena, pa se povraćaj ne može poslati "
                + "u registar. Traži se iz pravilnika i upisuje u podešavanja eOtpremnice.");
        }

        if (problems.Count > 0)
        {
            return EOtpremnicaResult<string>.Fail(EOtpremnicaError.Local(
                "Otpremnicu nije moguće pripremiti za registar.", problems));
        }

        var document = new XElement(Despatch + "DespatchAdvice",
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XElement(Cbc + "CustomizationID", options.CustomizationId),
            new XElement(Cbc + "ID", advice.Number),
            new XElement(Cbc + "IssueDate", Iso(advice.IssueDate)),
            // 9 = despatch advice. The code list distinguishes this from a return advice, which
            // travels the other way and is a different obligation — so the code comes from the
            // document's kind rather than from a literal that only ever meant one of them.
            new XElement(Cbc + "DespatchAdviceTypeCode",
                advice.Kind == DespatchAdviceKind.Return ? options.ReturnTypeCode! : "9"));

        if (!string.IsNullOrWhiteSpace(advice.Note))
            document.Add(new XElement(Cbc + "Note", advice.Note));

        if (!string.IsNullOrWhiteSpace(advice.OrderReference))
        {
            document.Add(new XElement(Cac + "OrderReference",
                new XElement(Cbc + "ID", advice.OrderReference)));
        }

        document.Add(Party(Cac + "DespatchSupplierParty", advice.Despatcher));
        document.Add(Party(Cac + "DeliveryCustomerParty", advice.Consignee));

        var shipment = new XElement(Cac + "Shipment", new XElement(Cbc + "ID", advice.Number));

        var delivery = new XElement(Cac + "Delivery",
            Address(Cac + "DeliveryAddress", advice.DeliverTo),
            new XElement(Cac + "EstimatedDeliveryPeriod",
                new XElement(Cbc + "StartDate", Iso(advice.DeliveryDate)),
                new XElement(Cbc + "EndDate", Iso(advice.DeliveryDate))));

        // Omitted entirely when the host does not know where the goods left from. An address block
        // with no address in it is still a claim about a place.
        if (advice.DespatchFrom is { } from && !IsEmpty(from))
        {
            delivery.Add(new XElement(Cac + "Despatch",
                Address(Cac + "DespatchAddress", from)));
        }

        // Everything about the journey lives on one cac:ShipmentStage, in the schema's order:
        // CarrierParty, then TransportMeans, then DriverPerson. cac:Shipment itself has no
        // CarrierParty child in UBL 2.1 — a carrier written there fails schema validation — and
        // the stage is written whenever any of the three is known, so a driver with no named
        // carrier and no plate is not silently dropped.
        var stage = new XElement(Cac + "ShipmentStage");

        if (!string.IsNullOrWhiteSpace(advice.CarrierName))
        {
            stage.Add(new XElement(Cac + "CarrierParty",
                new XElement(Cac + "PartyName",
                    new XElement(Cbc + "Name", advice.CarrierName))));
        }

        if (!string.IsNullOrWhiteSpace(advice.VehiclePlate))
        {
            stage.Add(new XElement(Cac + "TransportMeans",
                new XElement(Cac + "RoadTransport",
                    new XElement(Cbc + "LicensePlateID", advice.VehiclePlate))));
        }

        if (!string.IsNullOrWhiteSpace(advice.DriverName))
        {
            stage.Add(new XElement(Cac + "DriverPerson",
                new XElement(Cbc + "FirstName", advice.DriverName)));
        }

        if (stage.HasElements) shipment.Add(stage);

        shipment.Add(delivery);
        document.Add(shipment);

        var lineNumber = 1;

        foreach (var line in advice.Lines)
        {
            var id = lineNumber++;

            // cac:OrderLineReference is mandatory on every UBL 2.1 despatch line, order or no
            // order. The host's order line when it has one; this line's own number when it does
            // not, so the element is there and says something true rather than invented.
            var orderLineId = string.IsNullOrWhiteSpace(line.OrderLineId)
                ? id.ToString(CultureInfo.InvariantCulture)
                : line.OrderLineId.Trim();

            document.Add(new XElement(Cac + "DespatchLine",
                new XElement(Cbc + "ID", id),
                new XElement(Cbc + "DeliveredQuantity",
                    new XAttribute("unitCode", UblUnits.Code(line.Unit)!), Number(line.Quantity)),
                new XElement(Cac + "OrderLineReference",
                    new XElement(Cbc + "LineID", orderLineId)),
                new XElement(Cac + "Item",
                    new XElement(Cbc + "Name", line.Description))));
        }

        // XDocument.ToString() drops the declaration, so it is written explicitly: the register
        // receives a file, and a file that does not say its encoding is read with a guess.
        var xml = new XDocument(new XDeclaration("1.0", "UTF-8", null), document);

        return EOtpremnicaResult<string>.Ok(xml.Declaration + Environment.NewLine + xml);
    }

    /// <summary>
    /// Everything wrong with the document, checked before a single element is written.
    ///
    /// There is no money on a despatch advice, so these are structural: what left, how much of it,
    /// in a unit that has a code, from and to somebody the register can identify.
    /// </summary>
    private static List<string> Check(DespatchAdvice advice)
    {
        var problems = new List<string>();

        // cbc:ID identifies this document in the register. Only an issued note has a number, so a
        // blank one means a draft slipped through — and the alternative to refusing is a register
        // entry that can never be matched to the document it came from.
        if (string.IsNullOrWhiteSpace(advice.Number))
            problems.Add("Dokument bez broja ne može biti prijavljen registru.");

        if (advice.Lines.Count == 0)
            problems.Add("Otpremnica bez stavki ne može biti prijavljena registru.");

        foreach (var line in advice.Lines.Where(line => UblUnits.Code(line.Unit) is null))
        {
            problems.Add(
                $"Jedinica mere \"{line.Unit}\" nema UN/ECE oznaku (stavka: {line.Description}). "
                + $"Podržane: {UblUnits.Known}.");
        }

        foreach (var line in advice.Lines.Where(line => line.Quantity <= 0))
        {
            problems.Add(
                $"Stavka \"{line.Description}\" ima količinu {Number(line.Quantity)} — otpremnica "
                + "prijavljuje robu koja je stvarno otišla.");
        }

        // A party the register cannot identify makes the document a statement about nobody. Which
        // identifiers count is the host's rule; that there is one at all is this library's.
        if (string.IsNullOrWhiteSpace(advice.Despatcher.Name))
            problems.Add("Pošiljalac nije imenovan.");

        if (string.IsNullOrWhiteSpace(advice.Consignee.Name))
            problems.Add("Primalac nije imenovan.");

        if (!HasIdentifier(advice.Despatcher))
            problems.Add("Pošiljalac nema identifikator (PIB ili matični broj).");

        if (!HasIdentifier(advice.Consignee))
            problems.Add("Primalac nema identifikator (PIB ili matični broj).");

        return problems;
    }

    private static bool HasIdentifier(DespatchParty party) =>
        !string.IsNullOrWhiteSpace(party.VatNumber)
        || !string.IsNullOrWhiteSpace(party.RegistrationNumber);

    private static bool IsEmpty(DespatchAddress address) =>
        string.IsNullOrWhiteSpace(address.Street)
        && string.IsNullOrWhiteSpace(address.City)
        && string.IsNullOrWhiteSpace(address.PostalZone);

    private static XElement Address(XName wrapper, DespatchAddress address) =>
        new(wrapper,
            Optional(Cbc + "StreetName", address.Street),
            Optional(Cbc + "CityName", address.City),
            Optional(Cbc + "PostalZone", address.PostalZone),
            new XElement(Cac + "Country",
                new XElement(Cbc + "IdentificationCode", address.CountryCode)));

    private static XElement? Optional(XName name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value);

    private static XElement Party(XName wrapper, DespatchParty details)
    {
        var party = new XElement(Cac + "Party");

        if (!string.IsNullOrWhiteSpace(details.VatNumber))
        {
            party.Add(new XElement(Cac + "PartyIdentification",
                new XElement(Cbc + "ID", details.VatNumber)));
        }

        party.Add(new XElement(Cac + "PartyName", new XElement(Cbc + "Name", details.Name)));

        party.Add(Address(Cac + "PostalAddress", details.Address));

        var legalEntity = new XElement(Cac + "PartyLegalEntity",
            new XElement(Cbc + "RegistrationName", details.Name));

        if (!string.IsNullOrWhiteSpace(details.RegistrationNumber))
            legalEntity.Add(new XElement(Cbc + "CompanyID", details.RegistrationNumber));

        party.Add(legalEntity);

        return new XElement(wrapper, party);
    }

    /// <summary>
    /// Invariant with a dot, and never rounding: "0.####" silently rounded anything past four
    /// decimals, declaring a quantity of goods that was never dispatched.
    /// </summary>
    private static string Number(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string Iso(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
