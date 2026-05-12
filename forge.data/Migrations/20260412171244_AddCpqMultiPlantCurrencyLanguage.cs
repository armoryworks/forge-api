using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCpqMultiPlantCurrencyLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    decimal_places = table.Column<int>(type: "integer", nullable: false),
                    is_base_currency = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plants",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    company_location_id = table.Column<int>(type: "integer", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plants", x => x.id);
                    table.ForeignKey(
                        name: "fk_plants_company_locations_company_location_id",
                        column: x => x.company_location_id,
                        principalTable: "company_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_configurators",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    base_part_id = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    validation_rules_json = table.Column<string>(type: "text", nullable: true),
                    base_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    pricing_formula_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_configurators", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_configurators_parts_base_part_id",
                        column: x => x.base_part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supported_languages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    native_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    completion_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supported_languages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "translated_labels",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    language_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    context = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_approved = table.Column<bool>(type: "boolean", nullable: false),
                    translated_by_id = table.Column<int>(type: "integer", nullable: true),
                    translated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translated_labels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_currency_id = table.Column<int>(type: "integer", nullable: false),
                    to_currency_id = table.Column<int>(type: "integer", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_exchange_rates_currencies_from_currency_id",
                        column: x => x.from_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_rates_currencies_to_currency_id",
                        column: x => x.to_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inter_plant_transfers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_plant_id = table.Column<int>(type: "integer", nullable: false),
                    to_plant_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_by_id = table.Column<int>(type: "integer", nullable: true),
                    received_by_id = table.Column<int>(type: "integer", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inter_plant_transfers", x => x.id);
                    table.ForeignKey(
                        name: "fk_inter_plant_transfers__plants_from_plant_id",
                        column: x => x.from_plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inter_plant_transfers__plants_to_plant_id",
                        column: x => x.to_plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configurator_options",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    configurator_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    option_type = table.Column<int>(type: "integer", nullable: false),
                    values_json = table.Column<string>(type: "text", nullable: false),
                    pricing_rule_json = table.Column<string>(type: "text", nullable: true),
                    bom_impact_json = table.Column<string>(type: "text", nullable: true),
                    routing_impact_json = table.Column<string>(type: "text", nullable: true),
                    depends_on_option_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    help_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    default_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configurator_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_configurator_options__product_configurators_configurator_id",
                        column: x => x.configurator_id,
                        principalTable: "product_configurators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_configurations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    configurator_id = table.Column<int>(type: "integer", nullable: false),
                    configuration_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    selections_json = table.Column<string>(type: "text", nullable: false),
                    computed_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    generated_bom_json = table.Column<string>(type: "text", nullable: true),
                    generated_routing_json = table.Column<string>(type: "text", nullable: true),
                    quote_id = table.Column<int>(type: "integer", nullable: true),
                    part_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_configurations", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_configurations__product_configurators_configurator_id",
                        column: x => x.configurator_id,
                        principalTable: "product_configurators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_configurations__quotes_quote_id",
                        column: x => x.quote_id,
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_product_configurations_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "inter_plant_transfer_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_id = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    from_location_id = table.Column<int>(type: "integer", nullable: true),
                    to_location_id = table.Column<int>(type: "integer", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inter_plant_transfer_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_inter_plant_transfer_lines__parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inter_plant_transfer_lines_inter_plant_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalTable: "inter_plant_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_configurator_options_configurator_id",
                table: "configurator_options",
                column: "configurator_id");

            migrationBuilder.CreateIndex(
                name: "ix_currencies_code",
                table: "currencies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_currencies_is_active",
                table: "currencies",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_effective_date",
                table: "exchange_rates",
                column: "effective_date");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_from_currency_id_to_currency_id_effective_da~",
                table: "exchange_rates",
                columns: new[] { "from_currency_id", "to_currency_id", "effective_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_to_currency_id",
                table: "exchange_rates",
                column: "to_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfer_lines_part_id",
                table: "inter_plant_transfer_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfer_lines_transfer_id",
                table: "inter_plant_transfer_lines",
                column: "transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_from_plant_id",
                table: "inter_plant_transfers",
                column: "from_plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_received_by_id",
                table: "inter_plant_transfers",
                column: "received_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_shipped_by_id",
                table: "inter_plant_transfers",
                column: "shipped_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_status",
                table: "inter_plant_transfers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_to_plant_id",
                table: "inter_plant_transfers",
                column: "to_plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inter_plant_transfers_transfer_number",
                table: "inter_plant_transfers",
                column: "transfer_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plants_code",
                table: "plants",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plants_company_location_id",
                table: "plants",
                column: "company_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_plants_is_active",
                table: "plants",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurations_configuration_code",
                table: "product_configurations",
                column: "configuration_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_configurations_configurator_id",
                table: "product_configurations",
                column: "configurator_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurations_part_id",
                table: "product_configurations",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurations_quote_id",
                table: "product_configurations",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurators_base_part_id",
                table: "product_configurators",
                column: "base_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_configurators_is_active",
                table: "product_configurators",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_supported_languages_code",
                table: "supported_languages",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supported_languages_is_active",
                table: "supported_languages",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_translated_labels_key_language_code",
                table: "translated_labels",
                columns: new[] { "key", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translated_labels_language_code",
                table: "translated_labels",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_translated_labels_translated_by_id",
                table: "translated_labels",
                column: "translated_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configurator_options");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "inter_plant_transfer_lines");

            migrationBuilder.DropTable(
                name: "product_configurations");

            migrationBuilder.DropTable(
                name: "supported_languages");

            migrationBuilder.DropTable(
                name: "translated_labels");

            migrationBuilder.DropTable(
                name: "currencies");

            migrationBuilder.DropTable(
                name: "inter_plant_transfers");

            migrationBuilder.DropTable(
                name: "product_configurators");

            migrationBuilder.DropTable(
                name: "plants");
        }
    }
}
