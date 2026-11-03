namespace ForEach.Infrastructure.External.eOtpremnica.Ubl;

/// <summary>
/// The unit codes a despatch advice may carry.
///
/// Same table as the eFaktura writer's, and for the same reason: what leaves the warehouse is
/// declared to the same authority in the same codebook.
/// </summary>
public static class UblUnits
{
    /// <summary>
    /// Unit spelling to the code SEF accepts. **Every code here is on SEF's list of twenty-five**
    /// — see `SefUnitCatalogue`, read from the authority on 2026-08-28.
    ///
    /// `cm` and `pak` used to be here, mapped to CMT and XPK. Both are valid UN/ECE Recommendation
    /// 20 codes and neither has ever been on SEF's list, so a line in either unit was refused with
    /// nothing in the application able to explain it. They are gone rather than remapped: `cm` has
    /// no SEF equivalent at all, and turning `pak` into `set` would be the code deciding what
    /// physically left the warehouse.
    ///
    /// Deliberately generous about spelling — "kom" and "kom." are the same unit and both appear
    /// in real documents.
    /// </summary>
    private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kom"] = "H87",
        ["kom."] = "H87",
        ["pcs"] = "H87",
        ["piece"] = "H87",
        ["kg"] = "KGM",
        ["g"] = "GRM",
        ["m"] = "MTR",
        ["m2"] = "MTK",
        ["m3"] = "MTQ",
        ["km"] = "KMT",
        ["l"] = "LTR",
        ["t"] = "TNE",
        ["h"] = "HUR",
        ["set"] = "XKI",
        ["komplet"] = "KT",
        ["par"] = "PR"
    };

    /// <summary>The spellings accepted, for a message shown to whoever typed something else.</summary>
    public static string Known => string.Join(", ", Codes.Keys);

    /// <summary>Whether the unit has a code, spelled in any of the accepted ways.</summary>
    public static bool IsKnown(string? unit) =>
        !string.IsNullOrWhiteSpace(unit) && Codes.ContainsKey(unit.Trim());

    /// <summary>
    /// The code, or null for a unit with none.
    ///
    /// Null rather than a fallback to "piece": H87 for an unrecognised unit would declare five
    /// hundred kilograms as five hundred items on a document that says what physically left a
    /// warehouse.
    /// </summary>
    public static string? Code(string? unit) =>
        unit is not null && Codes.TryGetValue(unit.Trim(), out var code) ? code : null;
}
