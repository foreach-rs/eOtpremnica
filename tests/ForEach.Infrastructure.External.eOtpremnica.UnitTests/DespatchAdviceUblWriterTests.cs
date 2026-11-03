using System.Xml.Linq;
using ForEach.Infrastructure.External.eOtpremnica;
using ForEach.Infrastructure.External.eOtpremnica.Ubl;

namespace ForEach.Infrastructure.External.eOtpremnica.UnitTests;

/// <summary>
/// The UBL document asserted element by element, and the refusals asserted by what they say. The
/// element order is the schema's, so a reordering here is a document the register rejects.
/// </summary>
public class DespatchAdviceUblWriterTests
{
    private static readonly XNamespace Doc =
        "urn:oasis:names:specification:ubl:schema:xsd:DespatchAdvice-2";
    private static readonly XNamespace Cac =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static readonly DespatchAdviceUblOptions Options = new("CUSTOM-1");

    // ---- Document shape ----

    [Fact]
    public void The_output_starts_with_an_xml_declaration_for_utf8()
    {
        var xml = Write(Advice());

        Assert.StartsWith("<?xml", xml, StringComparison.Ordinal);
        Assert.Contains("UTF-8", xml.Split('\n')[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_root_is_in_the_despatch_advice_namespace_with_cac_and_cbc_declared()
    {
        var root = Root(Advice());

        Assert.Equal(Doc + "DespatchAdvice", root.Name);
        Assert.Equal("cac", root.GetPrefixOfNamespace(Cac));
        Assert.Equal("cbc", root.GetPrefixOfNamespace(Cbc));
    }

    [Fact]
    public void Root_children_come_in_schema_order_with_the_optional_ones_in_place()
    {
        var root = Root(Advice() with { Note = "n", OrderReference = "ORD-1" });

        Assert.Equal(
            ["CustomizationID", "ID", "IssueDate", "DespatchAdviceTypeCode", "Note",
             "OrderReference", "DespatchSupplierParty", "DeliveryCustomerParty", "Shipment",
             "DespatchLine"],
            Names(root));
    }

    [Fact]
    public void Note_and_order_reference_are_absent_when_not_given()
    {
        var root = Root(Advice());

        Assert.Equal(
            ["CustomizationID", "ID", "IssueDate", "DespatchAdviceTypeCode",
             "DespatchSupplierParty", "DeliveryCustomerParty", "Shipment", "DespatchLine"],
            Names(root));
    }

    [Fact]
    public void The_order_reference_wraps_the_id()
    {
        var root = Root(Advice() with { OrderReference = "ORD-1" });

        Assert.Equal("ORD-1", root.Element(Cac + "OrderReference")!.Element(Cbc + "ID")!.Value);
    }

    // ---- Shipment ----

    [Fact]
    public void The_shipment_never_has_a_carrier_party_of_its_own()
    {
        var shipment = Shipment(Advice() with { CarrierName = "Prevoz doo" });

        Assert.Null(shipment.Element(Cac + "CarrierParty"));
    }

    [Fact]
    public void Carrier_vehicle_and_driver_share_one_stage_in_schema_order_before_delivery()
    {
        var shipment = Shipment(Advice() with
        {
            CarrierName = "Prevoz doo",
            VehiclePlate = "BG-123-AB",
            DriverName = "Petar Petrović"
        });

        var stage = Assert.Single(shipment.Elements(Cac + "ShipmentStage"));

        Assert.Equal(["CarrierParty", "TransportMeans", "DriverPerson"], Names(stage));
        Assert.Equal("Prevoz doo",
            stage.Element(Cac + "CarrierParty")!.Element(Cac + "PartyName")!
                .Element(Cbc + "Name")!.Value);
        Assert.Equal("BG-123-AB",
            stage.Element(Cac + "TransportMeans")!.Element(Cac + "RoadTransport")!
                .Element(Cbc + "LicensePlateID")!.Value);
        Assert.Equal("Petar Petrović",
            stage.Element(Cac + "DriverPerson")!.Element(Cbc + "FirstName")!.Value);

        Assert.True(Names(shipment).IndexOf("ShipmentStage") < Names(shipment).IndexOf("Delivery"));
    }

    [Fact]
    public void A_driver_alone_still_gets_a_stage()
    {
        var shipment = Shipment(Advice() with { DriverName = "Petar Petrović" });

        var stage = Assert.Single(shipment.Elements(Cac + "ShipmentStage"));

        Assert.Equal(["DriverPerson"], Names(stage));
    }

    [Fact]
    public void With_no_carrier_vehicle_or_driver_there_is_no_stage()
    {
        Assert.Null(Shipment(Advice()).Element(Cac + "ShipmentStage"));
    }

    [Fact]
    public void Delivery_has_the_address_then_a_one_day_period_on_the_delivery_date()
    {
        var delivery = Shipment(Advice()).Element(Cac + "Delivery")!;

        Assert.Equal(["DeliveryAddress", "EstimatedDeliveryPeriod"], Names(delivery));

        var period = delivery.Element(Cac + "EstimatedDeliveryPeriod")!;

        Assert.Equal("2026-11-04", period.Element(Cbc + "StartDate")!.Value);
        Assert.Equal("2026-11-04", period.Element(Cbc + "EndDate")!.Value);
    }

    [Fact]
    public void No_despatch_from_means_no_despatch_element()
    {
        var delivery = Shipment(Advice() with { DespatchFrom = null }).Element(Cac + "Delivery")!;

        Assert.Null(delivery.Element(Cac + "Despatch"));
    }

    [Fact]
    public void A_despatch_from_with_every_field_blank_means_no_despatch_element()
    {
        var blank = new DespatchAddress(" ", null, "", "RS");

        var delivery = Shipment(Advice() with { DespatchFrom = blank }).Element(Cac + "Delivery")!;

        Assert.Null(delivery.Element(Cac + "Despatch"));
    }

    [Fact]
    public void A_real_despatch_from_is_written_as_a_despatch_address()
    {
        var from = new DespatchAddress("Magacinska 5", "Beograd", "11000");

        var delivery = Shipment(Advice() with { DespatchFrom = from }).Element(Cac + "Delivery")!;

        var address = delivery.Element(Cac + "Despatch")!.Element(Cac + "DespatchAddress")!;

        Assert.Equal("Magacinska 5", address.Element(Cbc + "StreetName")!.Value);
    }

    // ---- Lines ----

    [Fact]
    public void Each_line_has_its_children_in_order_with_an_order_line_reference()
    {
        var line = Assert.Single(Root(Advice()).Elements(Cac + "DespatchLine"));

        Assert.Equal(["ID", "DeliveredQuantity", "OrderLineReference", "Item"], Names(line));
    }

    [Fact]
    public void The_order_line_reference_is_the_hosts_when_set_and_the_line_number_when_not()
    {
        var advice = Advice() with
        {
            Lines =
            [
                new DespatchLine("Prva", "kom", 1m, "OL-7"),
                new DespatchLine("Druga", "kom", 1m),
                new DespatchLine("Treća", "kom", 1m, "  ")
            ]
        };

        var ids = Root(advice).Elements(Cac + "DespatchLine")
            .Select(l => l.Element(Cac + "OrderLineReference")!.Element(Cbc + "LineID")!.Value)
            .ToList();

        Assert.Equal(["OL-7", "2", "3"], ids);
    }

    [Fact]
    public void A_quantity_keeps_every_decimal_it_was_given()
    {
        Assert.Equal(("KGM", "0.12345"), Quantity("kg", 0.12345m));
    }

    [Fact]
    public void A_whole_quantity_has_no_decimal_point()
    {
        Assert.Equal(("H87", "500"), Quantity("kom", 500m));
    }

    // ---- Parties ----

    [Fact]
    public void A_party_carries_identification_name_address_and_legal_entity()
    {
        var party = Root(Advice()).Element(Cac + "DespatchSupplierParty")!.Element(Cac + "Party")!;

        Assert.Equal("100000001",
            party.Element(Cac + "PartyIdentification")!.Element(Cbc + "ID")!.Value);
        Assert.Equal("Pošiljalac doo",
            party.Element(Cac + "PartyName")!.Element(Cbc + "Name")!.Value);
        Assert.Equal("RS",
            party.Element(Cac + "PostalAddress")!.Element(Cac + "Country")!
                .Element(Cbc + "IdentificationCode")!.Value);

        var legal = party.Element(Cac + "PartyLegalEntity")!;

        Assert.Equal("Pošiljalac doo", legal.Element(Cbc + "RegistrationName")!.Value);
        Assert.Equal("12345678", legal.Element(Cbc + "CompanyID")!.Value);
    }

    [Fact]
    public void A_party_with_only_a_registration_number_is_accepted_and_omits_the_vat_id()
    {
        var advice = Advice() with { Despatcher = Despatcher() with { VatNumber = null } };

        var party = Root(advice).Element(Cac + "DespatchSupplierParty")!.Element(Cac + "Party")!;

        Assert.Null(party.Element(Cac + "PartyIdentification"));
    }

    [Fact]
    public void A_party_with_no_registration_number_omits_the_company_id()
    {
        var advice = Advice() with { Despatcher = Despatcher() with { RegistrationNumber = null } };

        var legal = Root(advice).Element(Cac + "DespatchSupplierParty")!.Element(Cac + "Party")!
            .Element(Cac + "PartyLegalEntity")!;

        Assert.Null(legal.Element(Cbc + "CompanyID"));
    }

    // ---- Refusals ----

    [Fact]
    public void A_blank_number_is_refused()
    {
        AssertRefused(Advice() with { Number = " " }, "broja");
    }

    [Fact]
    public void An_advice_with_no_lines_is_refused()
    {
        AssertRefused(Advice() with { Lines = [] }, "bez stavki");
    }

    [Fact]
    public void An_unknown_unit_is_refused_naming_the_unit_and_the_known_ones()
    {
        var advice = Advice() with { Lines = [new DespatchLine("Roba", "bucket", 1m)] };

        var detail = Assert.Single(Refused(advice).Error!.Details);

        Assert.Contains("bucket", detail, StringComparison.Ordinal);
        Assert.Contains(UblUnits.Known, detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void A_quantity_that_is_not_positive_is_refused(int quantity)
    {
        var advice = Advice() with { Lines = [new DespatchLine("Roba", "kom", quantity)] };

        AssertRefused(advice, "količinu");
    }

    [Fact]
    public void A_blank_despatcher_name_is_refused()
    {
        AssertRefused(
            Advice() with { Despatcher = Despatcher() with { Name = "" } },
            "Pošiljalac nije imenovan");
    }

    [Fact]
    public void A_blank_consignee_name_is_refused()
    {
        AssertRefused(
            Advice() with { Consignee = Consignee() with { Name = "" } },
            "Primalac nije imenovan");
    }

    [Fact]
    public void A_despatcher_with_no_identifier_at_all_is_refused()
    {
        var advice = Advice() with
        {
            Despatcher = Despatcher() with { VatNumber = null, RegistrationNumber = null }
        };

        AssertRefused(advice, "Pošiljalac nema identifikator");
    }

    [Fact]
    public void A_despatcher_with_only_a_registration_number_passes_the_identifier_check()
    {
        var advice = Advice() with
        {
            Despatcher = Despatcher() with { VatNumber = null, RegistrationNumber = "12345678" }
        };

        Assert.True(DespatchAdviceUblWriter.Write(advice, Options).IsSuccess);
    }

    [Fact]
    public void Every_problem_in_one_advice_is_reported_in_one_result()
    {
        var advice = Advice() with
        {
            Number = "",                                                                  // 1
            Despatcher = Despatcher() with { Name = " " },                                // 2
            Consignee = Consignee() with { VatNumber = null, RegistrationNumber = null }, // 3
            Lines = [new DespatchLine("Roba", "bucket", 0m)]                              // 4 and 5
        };

        Assert.Equal(5, Refused(advice).Error!.Details.Count);
    }

    // ---- Helpers ----

    private static (string Unit, string Value) Quantity(string unit, decimal quantity)
    {
        var advice = Advice() with { Lines = [new DespatchLine("Roba", unit, quantity)] };

        var element = Root(advice).Element(Cac + "DespatchLine")!.Element(Cbc + "DeliveredQuantity")!;

        return (element.Attribute("unitCode")!.Value, element.Value);
    }

    private static void AssertRefused(DespatchAdvice advice, string fragment)
    {
        var details = Refused(advice).Error!.Details;

        Assert.Contains(details, d => d.Contains(fragment, StringComparison.Ordinal));
    }

    private static EOtpremnicaResult<string> Refused(DespatchAdvice advice)
    {
        var result = DespatchAdviceUblWriter.Write(advice, Options);

        Assert.False(result.IsSuccess);
        Assert.Equal(EOtpremnicaFailureKind.CallerMistake, result.Error!.Kind);
        Assert.Equal(0, result.Error.HttpStatusCode);

        return result;
    }

    private static string Write(DespatchAdvice advice)
    {
        var result = DespatchAdviceUblWriter.Write(advice, Options);

        Assert.True(result.IsSuccess, result.FailureMessage);

        return result.Value!;
    }

    private static XElement Root(DespatchAdvice advice) => XDocument.Parse(Write(advice)).Root!;

    private static XElement Shipment(DespatchAdvice advice) =>
        Root(advice).Element(Cac + "Shipment")!;

    /// <summary>The distinct child names, in first-appearance order.</summary>
    private static List<string> Names(XElement element) =>
        element.Elements().Select(e => e.Name.LocalName).Distinct().ToList();

    private static DespatchParty Despatcher() =>
        new("Pošiljalac doo", "100000001", "12345678",
            new DespatchAddress("Ulica 1", "Beograd", "11000"));

    private static DespatchParty Consignee() =>
        new("Primalac doo", "100000002", "87654321",
            new DespatchAddress("Ulica 2", "Novi Sad", "21000"));

    private static DespatchAdvice Advice() =>
        new("OTP-1",
            new DateOnly(2026, 11, 3),
            new DateOnly(2026, 11, 4),
            Despatcher(),
            Consignee(),
            new DespatchAddress("Dostavna 3", "Niš", "18000"),
            [new DespatchLine("Roba", "kom", 2m)]);
}
