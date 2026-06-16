using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <summary>
    /// Re-creates the ledger append-only immutability functions + triggers on
    /// <c>acct_journal_entries</c> and <c>acct_journal_lines</c> (the DB-level companion to
    /// <c>LedgerImmutabilityInterceptor</c>). These objects are raw plpgsql, so EF's model-derived
    /// <c>InitialBaseline</c> does NOT reproduce them — the migration squash would otherwise drop
    /// them. This migration restores them verbatim.
    /// <para>
    /// Idempotent so it is safe everywhere: <c>CREATE OR REPLACE FUNCTION</c> + <c>DROP TRIGGER IF
    /// EXISTS</c> before each <c>CREATE TRIGGER</c>. On a fresh install it runs after InitialBaseline
    /// and creates the objects. On a legacy (pre-squash) install the boot reconciler marks it applied
    /// (the objects already exist from the original AddLedgerImmutabilityTriggers migration), so it
    /// does not re-run.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class RestoreLedgerImmutabilityTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. acct_journal_entries — header immutability (DELETE of Posted/Reversed always blocked;
            //    UPDATE of a Posted header allows only the Posted->Reversed flip + reversed_by_entry_id).
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION acct_journal_entries_immutability()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF (TG_OP = 'DELETE') THEN
        IF OLD.status IN ('Posted', 'Reversed') THEN
            RAISE EXCEPTION
                'Ledger immutability violation: % journal entry % cannot be deleted. Corrections are made via reversing entries only.',
                OLD.status, OLD.id
                USING ERRCODE = 'restrict_violation';
        END IF;
        RETURN OLD;
    END IF;

    -- TG_OP = 'UPDATE'
    IF (OLD.status = 'Posted') THEN
        -- The ONLY permitted mutation is the Posted->Reversed status flip plus
        -- (optionally) populating reversed_by_entry_id. Anything else is rejected.
        IF (NEW.status IS DISTINCT FROM 'Reversed')
           OR (NEW.id                       IS DISTINCT FROM OLD.id)
           OR (NEW.book_id                  IS DISTINCT FROM OLD.book_id)
           OR (NEW.entry_number             IS DISTINCT FROM OLD.entry_number)
           OR (NEW.entry_date               IS DISTINCT FROM OLD.entry_date)
           OR (NEW.fiscal_period_id         IS DISTINCT FROM OLD.fiscal_period_id)
           OR (NEW.fiscal_year_id           IS DISTINCT FROM OLD.fiscal_year_id)
           OR (NEW.source                   IS DISTINCT FROM OLD.source)
           OR (NEW.source_type              IS DISTINCT FROM OLD.source_type)
           OR (NEW.source_id                IS DISTINCT FROM OLD.source_id)
           OR (NEW.idempotency_key          IS DISTINCT FROM OLD.idempotency_key)
           OR (NEW.currency_id              IS DISTINCT FROM OLD.currency_id)
           OR (NEW.memo                     IS DISTINCT FROM OLD.memo)
           OR (NEW.auto_reverse_next_period IS DISTINCT FROM OLD.auto_reverse_next_period)
           OR (NEW.reversal_of_entry_id     IS DISTINCT FROM OLD.reversal_of_entry_id)
           OR (NEW.approved_by              IS DISTINCT FROM OLD.approved_by)
           OR (NEW.posted_by                IS DISTINCT FROM OLD.posted_by)
           OR (NEW.posted_at                IS DISTINCT FROM OLD.posted_at)
        THEN
            RAISE EXCEPTION
                'Ledger immutability violation: posted journal entry % is append-only. The only permitted mutation is the Posted->Reversed flip + reversed_by_entry_id link.',
                OLD.id
                USING ERRCODE = 'restrict_violation';
        END IF;
        RETURN NEW;
    ELSIF (OLD.status = 'Reversed') THEN
        -- A reversed header is fully locked — no further mutation of any kind.
        RAISE EXCEPTION
            'Ledger immutability violation: reversed journal entry % is locked and cannot be modified.',
            OLD.id
            USING ERRCODE = 'restrict_violation';
    END IF;

    -- Draft / PendingApproval / Approved headers remain freely mutable.
    RETURN NEW;
END;
$$;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_acct_journal_entries_immutability ON acct_journal_entries;");
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_acct_journal_entries_immutability
BEFORE UPDATE OR DELETE ON acct_journal_entries
FOR EACH ROW
EXECUTE FUNCTION acct_journal_entries_immutability();");

            // 2. acct_journal_lines — a line's lock follows its header.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION acct_journal_lines_immutability()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    owner_status varchar(20);
    target_entry_id bigint;
BEGIN
    -- The owning header is the same for OLD and NEW (re-parenting a line is
    -- itself a forbidden mutation, caught below), so OLD's FK is authoritative.
    target_entry_id := OLD.journal_entry_id;

    SELECT status INTO owner_status
    FROM acct_journal_entries
    WHERE id = target_entry_id;

    IF (owner_status IN ('Posted', 'Reversed')) THEN
        RAISE EXCEPTION
            'Ledger immutability violation: journal line % on a % entry cannot be %. Corrections are made via reversing entries only.',
            OLD.id, owner_status, lower(TG_OP)
            USING ERRCODE = 'restrict_violation';
    END IF;

    IF (TG_OP = 'DELETE') THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_acct_journal_lines_immutability ON acct_journal_lines;");
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_acct_journal_lines_immutability
BEFORE UPDATE OR DELETE ON acct_journal_lines
FOR EACH ROW
EXECUTE FUNCTION acct_journal_lines_immutability();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_acct_journal_lines_immutability ON acct_journal_lines;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS acct_journal_lines_immutability();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_acct_journal_entries_immutability ON acct_journal_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS acct_journal_entries_immutability();");
        }
    }
}
