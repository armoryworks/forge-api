using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// Files one communication against one thing it was about — a Quote, a Sales
/// Order, a Part, a Customer, a Vendor. A single message can be about several
/// at once; each gets its own row.
/// </summary>
public class CommunicationLink : BaseAuditableEntity
{
    public int CommunicationId { get; set; }
    public Communication Communication { get; set; } = null!;

    /// <summary>Quote | SalesOrder | Part | Customer | Vendor. Matches the repo's EntityType/EntityId convention.</summary>
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }

    /// <summary>
    /// Denormalized from the parent communication.
    ///
    /// <para>This is what makes "never attach to a bare Part" enforceable rather
    /// than merely intended. The same part sells to many customers, so part
    /// history is only meaningful at the customer × part intersection — a
    /// database CHECK refuses a Part link with no party, and the partial index
    /// on (party_id, entity_id) makes that intersection a single seek. Postgres
    /// CHECK constraints cannot reach into the parent row, which is why the
    /// column lives here rather than being read through the join.</para>
    /// </summary>
    public CommunicationPartyType? PartyType { get; set; }
    public int? PartyId { get; set; }

    /// <summary>Entity types a communication may be filed against. A bare Part is legal only alongside a party.</summary>
    public static class Types
    {
        public const string Quote = "Quote";
        public const string SalesOrder = "SalesOrder";
        public const string Part = "Part";
        public const string Customer = "Customer";
        public const string Vendor = "Vendor";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal) { Quote, SalesOrder, Part, Customer, Vendor };

        /// <summary>True for entity types that are meaningless without a party alongside them.</summary>
        public static bool RequiresParty(string entityType) =>
            string.Equals(entityType, Part, StringComparison.Ordinal);
    }
}
