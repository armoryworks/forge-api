# EDI Core — License Analysis + Real-Translator Plan

> **Status: IMPLEMENTED (2026-06-13, owner-ratified "pull in and implement EDI.Net").**
> `X12EdiService : IEdiService` is live behind the existing scaffold: EDI.Net (`indice.Edi`
> 1.12.0, MIT, NuGet) owns inbound deserialization; `X12DocumentWriter` renders the outbound
> set with hand-built envelopes (fixed-width ISA, computed SE/GE/IEA counts — the same
> exact-control pattern as NachaFileGenerator; the test suite parses rendered documents back
> through EDI.Net to prove the halves agree). Transport remains the mock channel — the Phase-B
> seam (SFTP/AS2/VAN) per trading partner.

## 1. What already exists (important — found 2026-06-13)

Forge already has a complete **EDI module scaffold** (built ~2026-05-28, gated
`CAP-CROSS-INTEG-EDI`, `Features/Edi/` + `EdiController`):

- `EdiTradingPartner` (ISA/GS identities, format, transport config, auto-process rules),
  `EdiMapping` (per-partner field mapping profiles), `EdiTransaction` (direction, transaction
  set, control number, status lifecycle, retry, 997-ack flag, raw payload).
- Full CRUD + receive/send/retry endpoints, `PollEdiInboundJob`, connection test.
- **The translation itself is `IEdiService`, implemented ONLY by `MockEdiService`**
  (`forge.integrations`) — the standard interface + real + mock pattern with the real
  implementation missing.

**Therefore the ratified work item is exactly: a real `X12EdiService : IEdiService` whose
parse/generate internals come from the OSS core.** No new entities, capability, or endpoints —
the scaffold is the config-driven shell the ratification describes.

## 2. License analysis (the mandatory gate)

| Project | Language | License | Verdict |
|---|---|---|---|
| **EDI.Net** (indice-co) | .NET | **MIT** | ✅ **Recommended.** Embeddable, forkable, vendorable. Active (v2.0.0-beta04 Oct 2025, 49 releases). Attribute-driven serializer for X12/EDIFACT/TRADACOMS; ships an X12 850 sample model. .NET 8+. |
| BOTS | Python | **GPLv3** | ❌ Cannot embed (copyleft + wrong runtime). Only viable as an external gateway process — contradicts the minimal-core direction. |
| ediFabric (github mirrors) | .NET | Proprietary trial / restrictive | ❌ The free GitHub copies are old trial versions under non-OSS terms. Avoid. |
| OpenAS2 | Java | BSD | ➖ Transport (AS2) only, not translation. Future-relevant only if a partner mandates AS2. |

**Recommendation (~85%):** integrate **EDI.Net via NuGet first** (MIT — no vendoring
obligation); vendor/fork only if we end up patching grammar internals. Forking before hitting a
limitation buys maintenance burden with no benefit; MIT lets us flip to a vendored copy at any
time.

## 3. Pared scope — the document set Forge needs

| X12 | Direction | Existing seam in `IEdiService` |
|---|---|---|
| **850** Purchase Order | inbound | `ParseTransactionAsync` → `ProcessTransactionAsync` (→ draft SalesOrder) |
| **855** PO Ack | outbound | `GeneratePoAckAsync(salesOrderId, …)` |
| **856** ASN | outbound | `GenerateAsnAsync(shipmentId, …)` |
| **810** Invoice | outbound | `GenerateInvoiceEdiAsync(invoiceId, …)` |
| **997** Functional Ack | outbound | `Generate997Async(inboundTransactionId, …)` |

OUT of scope: HIPAA sets, EDIFACT (until a partner demands it), AS2 transport (the module's
`EdiTransportMethod` already abstracts transport; start with the existing poll/manual channels).

## 4. What shipped (2026-06-13)

1. ✅ NuGet `indice.Edi` 1.12.0; `Edi850Interchange` inbound model (mirrors EDI.Net's own X12
   reference model; PO106/PO107 product-ID pairs, N1 loops, BEG).
2. ✅ `X12EdiService : IEdiService` (`forge.api/Features/Edi/`, scoped — owns the request
   DbContext; registered on the REAL DI branch, `MockEdiService` stays on the mock branch).
3. ✅ 850 → Draft SalesOrder: partner-identity validation (ISA sender must match the partner —
   wrong-sender fails loudly), duplicate-BEG03 guard (no double orders on retransmission),
   part resolution by PO107 against `Part.PartNumber` (unresolved numbers kept in line notes
   for human completion), `AutoProcess` flag wired in ReceiveEdiDocument.
4. ✅ 997 generated + persisted + linked on successful processing (inbound → Acknowledged).
5. ✅ Outbound 855 (BAK/PO1/ACK/CTT), 856 (BSN/HL S-O-I/LIN/SN1), 810 (BIG/IT1/TDS), 997
   (AK1/AK9); per-partner monotonic interchange control numbers.
6. ✅ 11 tests: golden 850→SO incl. part resolution, 997 linkage, duplicate/wrong-sender/
   no-customer/malformed error paths, idempotent reprocess, envelope structure proofs,
   810 round-trip through EDI.Net's parser, transport delegation.

### Update 2026-06-13 — transport shipped
Per-partner SFTP transport is DONE: `SftpEdiTransportService` (SSH.NET) selected by
`EdiTransportFactory` from the partner's method; typed admin dialog fields (password encrypted,
`Forge.EdiTransport` purpose); the 30-minute inbound poll job transports for real; polled files
rename `.processed`. Partner onboarding is values-entry.

## Part-number translation — SHIPPED 2026-06-13 (was the one remaining EDI build item)

**What works today:** an inbound 850 line resolves its part by EXACT match of the partner's
product ID (PO107, preferring the BP qualifier) against `Part.PartNumber`. When the numbers
match — e.g. the partner orders by our catalog numbers — lines land fully resolved.

**Built:** per-partner part-number translation. `IEdiPartNumberMapService` stores typed rows
(partner number → our number) in the conventional `EdiMapping` row's `ValueTranslationsJson`
(no migration); `X12EdiService.ProcessTransactionAsync` translates each 850 line's partner
number to our number BEFORE the `Part.PartNumber` match. Misses are unchanged — the line is
still created with both numbers in the notes (`Unresolved partner part number: X (mapped to Y,
not found)` when a mapping exists but its target doesn't). Endpoints:
`GET/PUT /edi/trading-partners/{id}/part-number-map` (typed rows, each resolved against the
catalog) + `POST .../import` (two-column CSV upsert by partner number, header synonyms +
positional fallback). UI: a Part-Number Map dialog (⇄ row action on each partner) with a typed
grid + CSV import — no JSON, per the standing rule; unresolved targets are flagged.

**Remaining extension (only if a partner needs it):** UOM / pack-quantity translation (they
order "1 case = 24 each"). The simple number swap covers the common case; the richer mapping
waits on a real partner spec so we build the right shape.

### Out of scope (unchanged)
- Inbound 855/856 (if we ever SELL via EDI to a customer who acknowledges).
- AS2/VAN transports until a partner mandates one (the factory is the seam).
