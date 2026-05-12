using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyToVendorPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "vendor_parts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            // Backfill: each VendorPart inherits its currency from its
            // most-common tier currency (mode). Defaulting to USD covers
            // the no-tiers case via the column default. We use a CTE to
            // rank tiers per vendor_part, then update from the top rank.
            migrationBuilder.Sql(@"
                WITH tier_currency_counts AS (
                    SELECT
                        vendor_part_id,
                        currency,
                        COUNT(*) AS n,
                        ROW_NUMBER() OVER (
                            PARTITION BY vendor_part_id
                            ORDER BY COUNT(*) DESC, currency
                        ) AS rn
                    FROM vendor_part_price_tiers
                    WHERE currency IS NOT NULL AND currency <> ''
                    GROUP BY vendor_part_id, currency
                )
                UPDATE vendor_parts vp
                SET currency = tcc.currency
                FROM tier_currency_counts tcc
                WHERE tcc.vendor_part_id = vp.id
                  AND tcc.rn = 1
                  AND tcc.currency <> 'USD';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currency",
                table: "vendor_parts");
        }
    }
}
