

namespace ForEach.Infrastructure.External.eOtpremnica.UnitTests;

/// <summary>
/// Reading the register's replies.
///
/// Every other eOtpremnica test in this project runs against a fake client that supplies the status
/// directly, so the parsing that finds it in a real reply was exercised by nothing. That is the
/// half where being wrong is quiet: a search that matches nothing returns null, the caller keeps
/// the raw body, and the delivery note reads as Pending — indefinitely, and looking exactly like a
/// register that has not answered yet.
///
/// The bodies below are shapes rather than captures. The guide does not pin the response down,
/// which is why the code searches, so the tests say what the search must survive rather than
/// pretending to know what arrives.
/// </summary>
public class EOtpremnicaResponseReaderTests
{
    [Fact]
    public void The_status_beside_our_id_is_the_one_returned()
    {
        const string body = """
            { "documentId": "REQ-1", "status": "Accepted" }
            """;

        Assert.Equal("Accepted", EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-1"));
    }

    [Fact]
    public void A_changes_page_holding_several_documents_answers_about_ours()
    {
        // The ordinary case, and the one that makes this a search rather than a lookup: a status
        // poll returns the register's recent changes, not just our document. Reading the first
        // status in the body would answer about whichever document happened to come back first —
        // and would look right whenever ours was first, which is most of the time in testing.
        const string body = """
            { "items": [
                { "documentId": "REQ-1", "status": "Rejected" },
                { "documentId": "REQ-2", "status": "Accepted" },
                { "documentId": "REQ-3", "status": "Pending" }
            ] }
            """;

        Assert.Equal("Accepted", EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-2"));
        Assert.Equal("Rejected", EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-1"));
    }

    [Fact]
    public void The_entry_is_found_however_deeply_it_is_nested()
    {
        // The shape is not fixed, so the depth is not either. A reader assuming the items sit at
        // the root would report Pending forever the day the register wraps them in an envelope.
        const string body = """
            { "data": { "page": { "entries": [
                { "documentId": "REQ-9", "status": "Accepted" }
            ] } } }
            """;

        Assert.Equal("Accepted", EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-9"));
    }

    [Fact]
    public void An_id_on_a_child_of_the_entry_is_not_matched_to_its_parents_status()
    {
        // The limit of the search, stated because it is not obvious and the failure is the silent
        // one. Depth costs nothing, but the id and the status have to be on the *same* object: the
        // inner object here matches the id and carries no status, and the outer one carries the
        // status and has no string property equal to the id, so nothing matches and the note reads
        // as Pending.
        //
        // Left as it is rather than taught to look at ancestors. No reply of this shape has been
        // seen, matching a parent's status to a child's id is exactly how the wrong document's
        // status gets reported, and this test is where somebody who does meet that shape will find
        // the answer already written down.
        const string body = """
            { "entries": [ { "request": { "id": "REQ-9" }, "status": "Accepted" } ] }
            """;

        Assert.Null(EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-9"));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("Status")]
    [InlineData("requestStatus")]
    [InlineData("RequestStatus")]
    public void Any_of_the_four_spellings_of_the_status_field_is_read(string field)
    {
        var body = $$"""
            { "documentId": "REQ-1", "{{field}}": "Accepted" }
            """;

        Assert.Equal("Accepted", EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-1"));
    }

    [Fact]
    public void An_id_matching_in_a_different_case_still_matches()
    {
        // Request ids are echoed back by a system that is not ours, and casing is exactly the sort
        // of thing that differs between the upload reply and the changes page.
        const string body = """
            { "documentId": "req-1", "status": "Accepted" }
            """;

        Assert.Equal("Accepted", EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-1"));
    }

    [Fact]
    public void A_body_that_never_mentions_our_document_reads_as_no_answer()
    {
        const string body = """
            { "items": [ { "documentId": "REQ-OTHER", "status": "Accepted" } ] }
            """;

        Assert.Null(EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-1"));
    }

    [Fact]
    public void An_entry_for_our_document_with_no_status_reads_as_no_answer()
    {
        // Not the same as absent, and it must not be reported as one: returning some other
        // document's status here would be worse than returning nothing.
        const string body = """
            { "items": [
                { "documentId": "REQ-1" },
                { "documentId": "REQ-2", "status": "Rejected" }
            ] }
            """;

        Assert.Null(EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("{ not json at all")]
    public void A_reply_that_is_not_json_reads_as_no_answer_rather_than_throwing(string body)
    {
        // A proxy error page is the realistic one. Throwing here would turn a transient gateway
        // failure into a failed status poll, and the caller keeps the raw body either way.
        Assert.Null(EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-1"));
    }

    [Fact]
    public void A_status_that_is_not_a_string_is_not_read_as_one()
    {
        // A numeric status code is a different protocol, not this one. Reading it would hand the
        // status map a value it has no case for, which reads as Sent rather than as unknown.
        const string body = """
            { "documentId": "REQ-1", "status": 3 }
            """;

        Assert.Null(EOtpremnicaResponseReader.FindRequestStatus(body, "REQ-1"));
    }

    // ---- the root-level reader --------------------------------------------------------------------

    [Fact]
    public void The_first_named_field_present_on_the_root_is_returned()
    {
        const string body = """
            { "id": "REQ-1", "requestId": "REQ-2" }
            """;

        Assert.Equal("REQ-1", EOtpremnicaResponseReader.ReadString(body, "id", "requestId"));
        Assert.Equal("REQ-2", EOtpremnicaResponseReader.ReadString(body, "requestId", "id"));
    }

    [Fact]
    public void A_nested_field_is_not_mistaken_for_a_root_one()
    {
        // Deliberately unlike FindRequestStatus. This reads an id the register puts at the top of
        // its reply; something with the same name further down is a different thing, and taking it
        // would file the document under an id the register never issued.
        const string body = """
            { "data": { "id": "REQ-INNER" } }
            """;

        Assert.Null(EOtpremnicaResponseReader.ReadString(body, "id"));
    }

    [Fact]
    public void A_plain_text_id_reads_as_no_answer_rather_than_throwing()
    {
        Assert.Null(EOtpremnicaResponseReader.ReadString("REQ-1", "id"));
        Assert.Null(EOtpremnicaResponseReader.ReadString("[1,2,3]", "id"));
    }

    [Fact]
    public void A_blank_document_id_matches_nothing_rather_than_the_first_document_with_a_blank_field()
    {
        // The matcher looks for an object holding *any* string property equal to the id. Remove the
        // blank-id guard and an empty id matches the first object with an empty-string property
        // anywhere in the response — and the status returned then belongs to a different despatch
        // advice entirely.
        //
        // That is worse than failing: the caller records it against this delivery note. A note
        // never accepted by the tax authority would be marked with whatever the neighbouring
        // document's status happened to be, and nothing in the response or the log would disagree.
        //
        // RefreshEOtpremnicaStatusCommand refuses a note with no document id, so this cannot be
        // reached from the application today. Pinned because the guard is the last line rather than
        // the only one, and because of what it costs when it is not there.
        const string body = """
            [
              { "documentId": "", "status": "Accepted" },
              { "documentId": "OT-0001/2026", "status": "Pending" }
            ]
            """;

        Assert.Null(EOtpremnicaResponseReader.FindRequestStatus(body, string.Empty));
        Assert.Null(EOtpremnicaResponseReader.FindRequestStatus(body, "   "));

        // And the ordinary case still works, so this is not passing by matching nothing at all.
        Assert.Equal("Pending", EOtpremnicaResponseReader.FindRequestStatus(body, "OT-0001/2026"));
    }
}
