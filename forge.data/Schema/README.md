# forge.data/Schema

`forge-schema.sql` is the **declarative database schema**, embedded into `forge.data` and applied at
boot by `SchemaBootstrapper` (full schema on a fresh DB; no-op on an existing one). It replaced EF
Core migrations in the db cutover (2026-06-17).

**It is a generated artifact — do not hand-edit.** It is the assembled output of the `forge-db`
repo's `schema/` tree (extension → tables → FKs → indexes → functions → triggers, in dependency
order), which is the source of truth for the schema.

## Changing the schema

1. Make the change in the **forge-db** repo's `schema/` tree (one object per file).
2. Regenerate this file:

   ```bash
   forge-db assemble --repo <path-to-forge-db> --out forge.data/Schema/forge-schema.sql
   ```

3. Update the EF entity/`OnModelCreating` mapping in forge-api to match (EF is a lean query-mapping
   layer; it no longer generates migrations).
4. Run the test suite — the Postgres-backed collection (`PostgresFixture`) applies this schema and
   runs real queries against it, so any EF-model-vs-schema drift fails there.

The `schema-drift-check` workflow asserts this file stays in sync with forge-db.
