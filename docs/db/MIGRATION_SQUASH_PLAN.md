# DB Cutover Plan — Migration Squash (+ optional Atlas declarative move)

> **Status: EXECUTED.** Phase 1 (the squash) merged — forge-api `a8260a75`, PR #18 — and the
> rebaseline deploy succeeded (the boot reconciler collapsed the live `__EFMigrationsHistory` to the
> baseline, data intact). Phase 2 (declarative cutover) is now **built in `forge-db`** (PR #1) using
> **[stripe/pg-schema-diff](https://github.com/stripe/pg-schema-diff)**, NOT Atlas — see §5 and
> `forge-db/docs/DESIGN.md`. Prod deploy hold otherwise stands. The rest of this doc is preserved as
> the original decision record.
>
> **Context:** `forge-api` had **132 EF Core migrations** (2026-04-10 → 2026-06-13, **121 MB**
> of Designer/snapshot files). The squash was deferred because **Armory Plastics is testing on
> live data** and forge-api runs `MigrateAsync()` on boot — see [[schema-migration-direction]].

---

## 1. The risk this plan exists to neutralize

A naive squash **breaks the Armory Plastics boot.** Their `__EFMigrationsHistory` holds the 132
old migration IDs. After collapsing to one `InitialBaseline`, EF sees that baseline as *pending*
(its ID isn't in their history) and `MigrateAsync()` tries to `CREATE TABLE` everything →
**fails: tables already exist.** Any plan that ignores this corrupts their boot.

Two facts make it tractable:
- **The schema is identical either way** — a squash is a no-op on schema *if* the baseline
  reproduces exactly what the 132-chain produced. That equivalence is the central proof (§3.3).
- **The boot path already self-heals** (`Program.cs` ~1236–1348 + `MigrationSchemaVerifier`):
  when history is *missing*, it verifies each migration's objects against `information_schema`
  and marks present ones applied without re-running. The squash needs that logic extended to
  the *stale-history* case (§3.1).

---

## 2. Scope options (the decision)

| Option | What | Risk | Recommend |
|---|---|---|---|
| **A. Squash now, Atlas later** | Phase 1 only: collapse to one EF baseline + safe boot reconciliation, proven against an Armory Plastics clone. Atlas deferred to post-go-live. | Low | ✅ Memory says Atlas is "later, once schema settles" — and it hasn't. Phase 1 produces the canonical SQL Atlas needs anyway, so it's the prerequisite either way. |
| **B. Squash + Atlas now** | Phase 1 then immediately stand up Atlas as the source of truth, retire EF migrations. | High | One cutover instead of two, but layers an unproven declarative toolchain onto a live-data customer in the same change window. |
| **C. Squash only, stay on EF** | Phase 1, keep EF migrations forever; drop the Atlas direction. | Low | Forgoes schema-as-code; otherwise fine. |

Phase 1 is **identical** under A, B, and C — so this plan details Phase 1 fully and sketches
Phase 2 (§5). The scope choice only decides whether/when Phase 2 happens.

---

## 3. Phase 1 — EF migration squash

### 3.1 Extend the boot reconciliation (do this FIRST, before squashing)

Today the self-heal in `Program.cs` triggers only when `!appliedList.Any()` (history empty/
missing). Add the **stale-history** branch:

- Compute `staleApplied = appliedList.Where(id => !allMigrations.Contains(id))` — applied IDs the
  assembly no longer knows. After a squash this is the 132 old IDs.
- **Signature:** `staleApplied` is non-empty.
- **Guard:** verify every assembly migration not yet in history (`assemblyNotApplied`) is present
  via `MigrationSchemaVerifier`. The baseline must verify present. If it does **not**, do NOT
  rewrite — log loudly and leave history untouched (fail safe; never risk a destructive apply
  against a mismatched schema).
- **Action (one transaction):**
  1. `CREATE TABLE __EFMigrationsHistory_pre_squash AS SELECT * FROM __EFMigrationsHistory`
     — back up the old rows so a rollback can restore them (§3.6).
  2. `DELETE` the stale rows.
  3. `INSERT` the verified assembly migration IDs (the baseline).
  4. Genuinely-unverified migrations (none, post-squash; some in future re-squashes) stay out of
     history → `MigrateAsync()` applies only those.
- This generalizes: it makes the squash safe AND makes any *future* re-squash safe.

Ship this with unit/integration tests: a DB seeded with stale history + present schema reconciles
to the baseline and applies nothing; a DB with stale history but a *missing* object does NOT
rewrite and surfaces the mismatch.

### 3.2 Generate the baseline

1. Confirm the model snapshot is current (it is — last migration applied, build green).
2. Delete the **132 timestamped** migration files (`*_*.cs` + `*.Designer.cs`) **and**
   `AppDbContextModelSnapshot.cs`. **Keep `MigrationSchemaVerifier.cs`** (a helper, not a
   migration).
3. `dotnet ef migrations add InitialBaseline -p forge.data -s forge.api` → one migration + a
   fresh snapshot. Kills the 121 MB sprawl.
4. `dotnet build -warnaserror` + full test suite green.

### 3.3 Prove schema-equivalence (the hard gate — do not skip)

The baseline must reproduce the 132-chain's schema *exactly*, or Armory Plastics' DB silently
diverges from the model.

1. Fresh scratch Postgres. Check out **pre-squash**, `dotnet ef database update` (runs all 132),
   then `pg_dump --schema-only --no-owner --no-privileges` → `schema_old.sql`.
2. Fresh scratch Postgres. **Post-squash**, `dotnet ef database update` (runs the baseline),
   `pg_dump …` → `schema_new.sql`.
3. Compare. `pg_dump` ordering isn't guaranteed identical, so use a **semantic** diff —
   `migra` (djrobstep) or `atlas schema diff` between the two databases — not a raw text diff.
   **Acceptance: zero differences.** Investigate any delta before proceeding (a real delta means
   a config/migration carried a manual SQL step the model doesn't reproduce).

### 3.4 Rehearse against an Armory Plastics CLONE (never the live DB)

1. Owner provides a `pg_dump` of the Armory Plastics DB (full: schema + data). **The squashed
   build never connects to the live DB.**
2. Restore the dump to a scratch Postgres.
3. Point the squashed forge-api at the scratch DB; boot. Expect the `[DB-LIFECYCLE]` logs to
   report: stale history detected → baseline verified present → history reconciled →
   `MigrateAsync` applies nothing.
4. Verify: `__EFMigrationsHistory` now holds exactly the baseline row;
   `__EFMigrationsHistory_pre_squash` holds the 132 old rows; representative table row counts and
   spot-checked data unchanged.

### 3.5 Dev databases

Per owner: wiping **dev** DBs is acceptable. Local stacks just recreate (`RECREATE_DB=true` or
drop+recreate). The reconciliation matters only for existing-data installs (Armory Plastics).

### 3.6 Rollback

- **Before reconcile fires:** revert the squash commit on `main`; the 132 migrations are in git
  history. Nothing on any DB changed.
- **After a successful reconcile on an install:** redeploying the pre-squash build would see 132
  "pending" against a baseline-only history and fail — so rollback restores the old history from
  `__EFMigrationsHistory_pre_squash` (the §3.1 backup), then redeploys the old build. Documented,
  reversible, no data loss.

### 3.7 Acceptance criteria (Phase 1 done)

- One `InitialBaseline` migration; Migrations dir back to a few KB; build `-warnaserror` + full
  suite (InMemory + PG atomicity) green.
- §3.3 semantic schema diff empty.
- §3.4 clone rehearsal: reconciles, applies nothing, data intact, history backed up.
- Deployment still **held** (separate owner gate).

---

## 4. What stays true regardless

- **Still dark.** The squash is pure schema-tooling; CAP-ACCT-FULLGL and the rest are unaffected.
- **Keep configs canonical.** The accounting `acct_*` configs already use explicit
  constraint/index names; that's what lets the baseline export to clean SQL (and feed Atlas).
  Keep new configs the same.

---

## 5. Phase 2 — declarative cutover (BUILT — see `forge-db`)

The declarative db-project now lives in `armoryworks/forge-db` (PR #1). What shipped, vs. this
section's original Atlas sketch:

1. **Engine: [stripe/pg-schema-diff](https://github.com/stripe/pg-schema-diff)** (MIT, no
   account/registration) — **not Atlas**. Atlas was the first choice (candidate list below), but its
   free tier gates `CREATE EXTENSION`/`FUNCTION`/`TRIGGER` behind `atlas login` ("available to
   logged-in users only") — unacceptable for an open-source self-host stack. pg-schema-diff handles
   our `vector` + identity columns + functions + triggers natively.
2. **Seed:** `pg_dump --schema-only --no-owner --no-privileges` of a scratch DB built from this
   squash's `InitialBaseline`, split one-object-per-file into `forge-db/schema/`.
3. **EF's new role: option (a) below was chosen** — EF keeps the entity mapping (prefer attributes),
   stops generating migrations; a one-directional **CI drift-check** (still TODO) will assert the EF
   model conforms to forge-db's `schema/`. (We did NOT scaffold the model from the schema — option b.)
4. **Boot path (target, owner-gated, not yet cut over):** deploy-time `Forge.Db apply` with a
   read-only boot (`verify`, refuse-on-drift), replacing `MigrateAsync()`.

Original candidate ranking (historical, from [[schema-migration-direction]]): **Atlas** (declarative,
first-class Postgres), `migra` (diff engine), pgquarrel/apgdiff; change-based tools
(Sqitch/Flyway/Liquibase) were explicitly **not** the target. The outcome differs (pg-schema-diff)
for the registration reason in #1 — migra is also now deprecated and pgquarrel unmaintained. Full
rationale + decision table in `forge-db/docs/DESIGN.md` (§4, §7, §9).

---

## 6. Open decisions for the owner

1. **Scope** — A (recommended), B, or C (§2).
2. **Armory Plastics dump** — Phase 1 §3.4 needs a `pg_dump` of their DB; coordinate when.
3. **BOMLine-vs-BomRevision costing authority** — if it restructures costing tables, it's a
   normal post-baseline migration; just know the baseline isn't permanently frozen.
