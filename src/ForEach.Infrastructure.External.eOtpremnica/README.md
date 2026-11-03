# ForEach.Infrastructure.External.eOtpremnica

Everything that knows the Serbian central register of electronic delivery notes — its public API
and the shape of a UBL 2.1 Despatch Advice — in one library with no dependencies, so more than one
product can register a despatch.

Sibling of `ForEach.Infrastructure.External.eFaktura`: same ministry, same `Api-key` style, same
result model. A host that has integrated one should recognise this without reading it twice.

## What it does not do

- **It persists nothing and retries nothing.** A result says whether retrying is safe; when to do
  it belongs to somebody's job scheduler.
- **It reads no clock.** `GetStatusAsync` and `ProbeAsync` are handed the date to ask about,
  because which day it is is a business question — answered in UTC it is yesterday for two hours
  every evening in Belgrade.
- **It decides no policy.** Which delivery notes may be registered, which numbers they carry, when
  a withdrawal is allowed — the host's.

## The three outcomes

```csharp
var result = await client.SendDespatchAdviceAsync(ubl, requestId);

if (result.IsSuccess) return result.Value;
if (result.Outcome == EOtpremnicaOutcome.Unknown) return PollBy(requestId);  // never resend
if (result.IsWorthRetrying) return Retry(result);
Show(result.FailureMessage);
```

`Unknown` is the one that matters, and this register produces it in one more case than SEF does: a
**5xx on a submission**. The register was reached and failed inside it, so a request accepted just
before it fell over cannot be told from one that never ran. A timeout on a submission is `Unknown`
for the same reason. Every failure on a *read* is a plain `Failed`.

`EOtpremnicaError.Kind` sorts refusals into the three responses a host has:

| Kind | HTTP | What to do |
|---|---|---|
| `Configuration` | 401, 403 | the key is wrong, expired, or from the other environment — a person must act |
| `CallerMistake` | 0, 400, 404, 409, 422 | the document or the request was wrong — fix it; 0 is a refusal decided locally |
| `Transient` | 408, 429, 5xx, anything unknown | retry later |

## Two calls, not one

Accepting a submission means the request was **queued**, not that the despatch advice was
registered. The outcome arrives later as a DocumentRequest status, so a host polls:

```csharp
var status = await client.GetStatusAsync(receipt.DocumentId, BusinessToday);
```

There is no per-document status endpoint. The register exposes changes for a whole day, in pages,
and the caller's request is located within them — which is why `GetStatusAsync` takes a date, and
why `EOtpremnicaResponseReader.FindRequestStatus` matches the id inside a page rather than reading
the first status it finds. Two documents in one page is the ordinary case.

`GetStatusAsync` walks the day's pages from `page=0` until the document turns up, until a page
carries no entries, or until `EOtpremnicaClient.MaxStatusPages`. A day that never mentions the
document yields `DocumentRequest.Pending`. That is what the register means by not mentioning it,
and it is not an error. `ProbeAsync` reads one page only — it asks whether the key works, not
where a document is — and returns that page's body.

## Transport failures on a submission

A submission that got no answer is `Unknown` **unless the connection never opened**. A name that
did not resolve, a refused port or a failed TLS handshake sent nothing, so nothing can be queued,
and the result is a plain retryable `Failed` with `TransportFailure` set. A connection cut after it
opened, or a timeout, is `Unknown`: the register may have read the request.

## Withdrawal

`CancelAsync` refuses, by name. A cancellation is not a parameter on the requests endpoint: it is
itself a UBL Application Response submitted through the same endpoint, so sending one is a document
to build. Until it is built, returning success would record a withdrawal that never happened.

## The document

`DespatchAdviceUblWriter.Write` follows the UBL 2.1 schema: element order, the carrier on
`cac:ShipmentStage` rather than on `cac:Shipment` (which has no such child), and a
`cac:OrderLineReference` on every line because the schema requires one — the host's order line
when `DespatchLine.OrderLineId` is set, the despatch line's own number otherwise. A party must be
named and must carry a PIB or a registration number; which of the two the register wants is the
host's rule.

The ministry's field-by-field mapping has not been transcribed. Validate a generated document
against the demo register before relying on it.

## Shared UBL code

`UblUnits`, the namespace constants, and the party and address writers are **duplicated** between
this library and the eFaktura one rather than shared, and that is a decision rather than an
oversight. A shared package would be the first ForEach library with a dependency, would need its
own versioning discipline, and the two systems' customizations already diverge — different
`CustomizationID`s, different party requirements. Revisit when a third UBL consumer appears, or
when the same change has to be made twice.
