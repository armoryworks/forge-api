# Remediation test suite (TDD burn-down of audit findings)

Test-first remediation of the defects/gaps found by the `forge-analysis` audit.
Each finding becomes a test that asserts the **correct** behavior (per
`docs/domain/definition-of-correct.md`); the test fails today (RED), and the fix
makes it pass (GREEN). The test then stays as a permanent regression guard.

## The RED-pending convention (so the green CI gate is never broken)

A finding's test is written **before** the fix, but committing a *failing* test
would red `dotnet test`. So a not-yet-fixed test is marked:

```csharp
[Fact(Skip = "RED: <FINDING-ID> — <one-line what's wrong>. Remove Skip when <fix>.")]
```

- It **compiles** (so the build stays green) and **documents the contract**.
- It is **skipped**, not failing, so CI stays green.
- **Burn-down = remove the `Skip` + implement until it passes**, in the *same*
  commit/PR. Never commit a non-skipped test that is RED.

A `Skip`'d test that references a `<FINDING-ID>` is the live "still broken" list:
`grep -rn 'Skip = "RED' forge.tests/Remediation` is your remaining backlog.

## Naming + layout

```
Remediation/
  BACKLOG.md                         ← the prioritized finding → test → status map
  <Area>/<Handler>RemediationTests.cs
```

- One test class per handler / surface (e.g. `ConvertQuoteToOrderRemediationTests`).
- Method names state the *correct* behavior: `ConvertQuoteToOrder_carries_quote_Notes_onto_the_order`.
- Put the finding id in the `Skip` reason **and** the class `<summary>`, with the
  `file.cs:line` of the defect, for traceability back to the audit.

## Layer → test type

| Finding kind | Test | Where |
|---|---|---|
| Handler business logic | xUnit + Moq on repos | `Remediation/<Area>/...` (here) |
| Endpoint / cross-cut | xUnit + `WebApplicationFactory` | integration tests |
| EF/DB invariant (FK, unique, transaction) | xUnit + `TestDbContextFactory` | here |
| UI component logic | Vitest `*.spec.ts` | `forge-ui` repo |
| Cross-area flow / journey | Cypress E2E | `forge-ui/cypress/e2e` |
| WCAG / a11y | Cypress + axe | `npm run test:a11y` |

## Workflow per finding

1. Pick the next finding from `BACKLOG.md` (HIGH severity first).
2. Read the finding + the relevant `definition-of-correct` clause → that's the assertion.
3. Write the test asserting the correct behavior, `[Fact(Skip="RED: …")]`. Verify it
   compiles (`dotnet build`). It captures the defect precisely.
4. Implement the minimal fix. Remove the `Skip`. `dotnet test` green.
5. Flip the row in `BACKLOG.md` to ✅ and commit test + fix together.

> Writing the test often **sharpens** the finding (e.g. AUDIT-S3 "5 header fields
> dropped" turned out to be a single `Notes` drop + a separate SO-edit UI gap, once
> checked against `Quote.cs`). That refinement is a feature of test-first, not a bug.

Source of truth for findings: `E:/dev/forge-analysis/findings/` (and the phase-41
coverage merge once the audit completes); for "correct": `docs/domain/definition-of-correct*.md`.
