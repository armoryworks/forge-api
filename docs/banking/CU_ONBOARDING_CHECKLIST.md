# Frontier CU — ACH Origination Onboarding Checklist

> Bring this to the business-liaison meeting. Every question maps to a Forge setting or a
> Phase-B/C build decision (BANK-002). The liaison's vehicle for most of this is the **ACH
> origination agreement** — ask for it up front; expect light underwriting (they're extending
> you ACH credit risk, so financials questions are normal).

## 1. Identifiers — fill the Banking settings group verbatim

| Ask the liaison | Forge setting |
|---|---|
| "What value goes in the file header's **Immediate Destination**?" (their 9-digit routing) | `banking.nacha.immediate-destination` |
| "What is our **Immediate Origin**?" (usually 1 + EIN, sometimes CU-assigned) | `banking.nacha.immediate-origin` |
| "What **Company ID** goes on batch headers?" (usually 1 + EIN) | `banking.nacha.company-id` |
| "What **Company Name** appears on the receiver's statement?" (16 chars) | `banking.nacha.company-name` |
| "What's the **ODFI routing** for trace numbers?" (first 8 digits of their routing, but confirm) | `banking.nacha.originating-dfi` |
| Destination/origin display names for the header | `banking.nacha.immediate-destination-name` / `-origin-name` |

## 2. File format questions (one of these can change code — ask it first)

- **"Do you want balanced or unbalanced files?"** ⚠️ Forge currently writes **unbalanced
  credits-only files (service class 220)** — the common business-origination setup where the
  CU auto-creates the offset to your account. If they require **balanced** files, we add one
  offsetting debit entry per batch (service class 200) — a small, known change. This is the
  single technical answer that affects code.
- "**CCD** as the SEC code for vendor payments — approved?" (`banking.nacha.entry-class-code`;
  PPD only if we ever pay sole proprietors' personal accounts)
- "NACHA format version expectations?" (Forge writes the standard 94-char/004010-era layout —
  universally accepted; just confirm no addenda requirements for CCD)
- "Will you **validate a test file** before the first live submission?" (Forge can generate
  with the Test flag — ask how they want to receive it)

## 3. Operations

- **Cutoff times** — daily deadline for next-day settlement; do they offer same-day ACH and
  at what windows/fees? (drives the effective-entry-date guidance in the batch dialog)
- **Prenote policy** — required? What return window do they observe (modern rule is 3 banking
  days)? (`banking.require-prenote`; the verify step in Forge is manual after the window)
- **Exposure limit** — what daily/per-file cap will they set in the agreement?
  (`banking.exposure-limit` should mirror it so Forge blocks a file the CU would reject)

## 4. Delivery channel — decides Phase B's shape

- "How do business originators **submit files**? Portal upload, SFTP, or both?"
  - Portal upload → **Forge Phase A works day one** (generate → download → upload → release).
  - SFTP available → ask for: host, credentials process, directory layout, file-naming
    convention, PGP requirements → that spec IS the Phase-B build ticket.
- "How are **returns and NOCs delivered**?" (portal report? file on the SFTP drop? format?)
  → that spec is the Phase-C build ticket (Forge already stores per-entry trace numbers as
  the join key).

## 5. While you're there (BANK-001 + general)

- Confirm **statement export formats** in business online banking — OFX/QFX or CSV both work
  for Forge's statement import; ask if transaction exports carry FITIDs (OFX does).
- Dual-control expectations on their side (some portals enforce a second approver for ACH —
  fine; Forge enforces its own create≠release regardless).
- Fees: per-file, per-entry, returns, same-day premium — for the cost picture, not Forge.

## After the meeting

1. Populate the Banking settings group (Admin → Settings → Banking) from §1–§3.
2. Enable `CAP-BANK-NACHA`, enter vendor bank accounts (dual control), run a prenote batch.
3. If they offered SFTP: hand the channel spec back into BANK-002 Phase B.
4. If they want a test file: assemble a batch and send the generated file through their
   validation before first live release.
