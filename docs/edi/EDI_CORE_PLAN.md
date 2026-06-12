# EDI Core — License Analysis + Real-Translator Plan

> **Status:** analysis complete; implementation awaiting owner confirmation of the fork target
> (§10.4 ratification: "config-driven minimal core pared from an OSS translator fork").
>
> ⚠️ **Open item:** the owner referenced an OSS project from an out-of-band discussion whose
> name was never recorded in-session. The analysis below stands on its own; if the remembered
> project differs from the recommendation, only the translator internals change.

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

## 4. Implementation plan (one focused session once the fork target is ratified)

1. NuGet `indice.Edi`; attribute-annotated grammar POCOs for 850/810/855/856/997 (start from
   EDI.Net's own X12_850 test model).
2. `X12EdiService : IEdiService` in `forge.integrations` — same DI switch as every other
   integration (`MOCK_INTEGRATIONS` keeps `MockEdiService` for tests/dev).
3. Wire `ProcessTransactionAsync` 850-handling to draft-SalesOrder creation using the partner's
   `EdiMapping` profile (partner part number → our part via the mapping rows).
4. 997 generation on successful inbound parse (the scaffold already tracks `IsAcknowledged`).
5. Tests: golden-file 850 → SO; 810 render → re-parse round-trip; malformed-interchange →
   transaction Failed with error message (the existing retry path).
