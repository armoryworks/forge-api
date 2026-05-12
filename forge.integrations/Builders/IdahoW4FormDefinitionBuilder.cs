using System.Text.Json;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations.Builders;

/// <summary>
/// Hardcoded builder for Idaho Form ID W-4 (Employee's Withholding Allowance Certificate).
/// EFO00307, revision 04-28-2025 — Idaho State Tax Commission.
/// Produces a pixel-perfect ComplianceFormDefinition matching the actual PDF layout:
///   - Thin black bordered box around entire form
///   - 2-column header (IDAHO / State Tax Commission | Form ID W-4 / subtitle)
///   - Inline horizontal withholding status checkboxes (A/B/C)
///   - Lines 1-2 with dot leaders and amount inputs
///   - SSN right-aligned, blue-shaded input
///   - Personal info grid with blue-shaded inputs (name, address, city/state/zip)
///   - Declaration text + signature/date row
///   - Page 2: instructions, pay period table, 9-line worksheet, contact info
/// Only content below the cut line on page 1 is rendered as the fillable form.
/// </summary>
public class IdahoW4FormDefinitionBuilder : IStateFormDefinitionBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public string StateCode => "ID";

    public string BuilderVersion => "v2";

    public string Build(PdfExtractionResult rawResult)
    {
        var formStyles = PdfMetricsCalculator.Compute(rawResult);

        // ─── Font sizes (Idaho form uses slightly larger than federal W-4) ───
        formStyles["gov-font"] = "9px";
        formStyles["gov-font-sm"] = "8px";
        formStyles["gov-font-md"] = "10px";
        formStyles["gov-font-lg"] = "12px";
        formStyles["gov-font-title"] = "14px";
        formStyles["gov-font-footer"] = "8px";

        // ─── Header element sizes ───
        formStyles["gov-header-number-size"] = "16px";
        formStyles["gov-header-year-size"] = "12px";
        formStyles["gov-header-padding"] = "6px 10px";
        formStyles["gov-header-label-size"] = "8px";

        // ─── Line heights ───
        formStyles["gov-line-height"] = "1.3";
        formStyles["gov-line-height-tight"] = "1.15";
        formStyles["gov-line-height-relaxed"] = "1.5";

        // ─── Borders (thin black throughout — between cells, around entire form) ───
        formStyles["gov-border-heavy"] = "1px";
        formStyles["gov-border-normal"] = "1px";
        formStyles["gov-border-color"] = "#000";

        // ─── Layout (no step-label sidebar — Idaho uses simple bordered table structure) ───
        formStyles["gov-step-label-pct"] = "0%";
        formStyles["gov-header-left-pct"] = "40%";
        formStyles["gov-header-right-pct"] = "60%";
        formStyles["gov-step-label-padding"] = "0";
        formStyles["gov-step-content-padding"] = "0";
        formStyles["gov-grid-cell-padding"] = "2px 6px";
        formStyles["gov-input-height"] = "22px";
        formStyles["gov-input-padding"] = "2px 4px";
        formStyles["gov-amount-input-height"] = "22px";
        formStyles["gov-amount-col-width"] = "140px";
        formStyles["gov-field-label-padding-top"] = "2px";
        formStyles["gov-tip-padding"] = "8px 10px";
        formStyles["gov-checkbox-size"] = "12px";
        formStyles["gov-radio-size"] = "12px";
        formStyles["gov-radio-border-width"] = "1px";

        // ─── Withholding status (inline horizontal radio row) ───
        formStyles["gov-filing-status-gap"] = "8px";
        formStyles["gov-filing-status-padding"] = "4px 0";
        formStyles["gov-radio-gap"] = "3px";
        formStyles["gov-radio-padding"] = "2px 0";
        formStyles["gov-radio-line-height"] = "1.4";

        // ─── Amount lines ───
        formStyles["gov-amount-line-gap"] = "4px";
        formStyles["gov-amount-line-padding"] = "2px 0";
        formStyles["gov-amount-dots-min-width"] = "30px";
        formStyles["gov-amount-dots-max-width"] = "100px";
        formStyles["gov-amount-field-padding"] = "2px 4px";
        formStyles["gov-amount-label-min-width"] = "24px";

        // ─── Checkbox ───
        formStyles["gov-checkbox-line-padding"] = "3px 0";
        formStyles["gov-checkbox-inline-gap"] = "12px";
        formStyles["gov-checkbox-inline-padding"] = "4px 0";

        // ─── Signature ───
        formStyles["gov-signature-gap"] = "12px";
        formStyles["gov-signature-font-size"] = "18px";
        formStyles["gov-signature-height"] = "30px";
        formStyles["gov-signature-date-width"] = "40%";

        // ─── Instructions & worksheets ───
        formStyles["gov-inst-text-line-height"] = "1.5";
        formStyles["gov-ws-line-padding"] = "4px 0";
        formStyles["gov-ws-num-width"] = "18px";

        // ─── Colors (Idaho uses light blue/gray shaded input backgrounds) ───
        formStyles["gov-shading-color"] = "rgba(180, 210, 240, 0.3)";
        formStyles["gov-shading-color-light"] = "rgba(180, 210, 240, 0.2)";
        formStyles["gov-shading-color-dark"] = "rgba(140, 180, 220, 0.12)";

        var definition = new Dictionary<string, object?>
        {
            ["formType"] = "StateWithholding_ID",
            ["builderVersion"] = BuilderVersion,
            ["title"] = "ID W-4",
            ["formNumber"] = "Form ID W-4",
            ["revision"] = "04-28-2025",
            ["agency"] = "Idaho State Tax Commission",
            ["formLayout"] = "government",
            ["maxWidth"] = "850px",
            ["formStyles"] = formStyles,
            ["pages"] = new List<object>
            {
                BuildPage1(rawResult),
                BuildPage2(rawResult),
            },
        };

        return JsonSerializer.Serialize(definition, JsonOptions);
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 1 — Employee's Withholding Allowance Certificate
    //  (Only content below the cut line — the fillable portion)
    // ════════════════════════════════════════════════════════════════

    private static Dictionary<string, object?> BuildPage1(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "page1",
            ["title"] = "Page 1",
            ["sections"] = new List<object>
            {
                BuildFormHeader(),
                BuildWithholdingStatus(raw),
                BuildAllowancesAndAdditional(raw),
                BuildSsnField(raw),
                BuildPersonalInfoNameRow(raw),
                BuildPersonalInfoAddressRow(raw),
                BuildPersonalInfoCityStateZipRow(raw),
                BuildDeclaration(),
                BuildSignature(raw),
                BuildPage1Footer(),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 1 — Header (bordered, 2-column)
    //  Left (40%): "IDAHO" large bold, "State Tax Commission" below
    //  Right (60%): "Form ID W-4" large bold, subtitle below
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildFormHeader()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_header",
            ["title"] = "Form ID W-4",
            ["layout"] = "form-header",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "header_left",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-weight:bold;line-height:1.2;padding:6px 10px\">"
                             + "<span style=\"font-size:18px;letter-spacing:1px\">IDAHO</span><br>"
                             + "<span style=\"font-size:10px\">State Tax Commission</span></div>",
                    ["gridColumn"] = "left",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "header_right",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"text-align:right;padding:6px 10px\">"
                             + "<span style=\"font-size:16px;font-weight:bold\">Form ID W-4</span><br>"
                             + "<span style=\"font-size:11px;font-weight:bold\">Employee\u2019s Withholding Allowance Certificate</span></div>",
                    ["gridColumn"] = "right",
                },
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 2 — Withholding Status
    //  Bold label + INLINE horizontal checkboxes A/B/C
    //  Square checkbox style, all on one line
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildWithholdingStatus(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_withholding_status",
            ["title"] = "Withholding Status",
            ["layout"] = "section",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "withholding_status_label",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-size:9px;font-weight:bold;padding:4px 6px 2px 6px\">"
                             + "WITHHOLDING STATUS (see information above)</div>",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "withholding_status") ?? "id_w4_withholdingStatus",
                    ["type"] = "radio",
                    ["label"] = "Withholding Status",
                    ["fieldLayout"] = "filing-status",
                    ["checkboxStyle"] = "square",
                    ["inlineLayout"] = true,
                    ["required"] = true,
                    ["options"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["value"] = "Single",
                            ["label"] = "A  (Single)",
                        },
                        new Dictionary<string, object?>
                        {
                            ["value"] = "Married",
                            ["label"] = "B  (Married)",
                        },
                        new Dictionary<string, object?>
                        {
                            ["value"] = "MarriedSingleRate",
                            ["label"] = "C  (Married, but withhold at Single rate)",
                        },
                    },
                },
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROWS 3-4 — Lines 1 and 2 (allowances + additional amount)
    //  Each line: number + text + dot leaders + input box at right
    //  Line 2 has dollar sign prefix
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildAllowancesAndAdditional(PdfExtractionResult raw)
    {
        var line1 = Ann(raw, 1, "f1_01") ?? "id_w4_totalAllowances";
        var line2 = Ann(raw, 1, "f1_02") ?? "id_w4_additionalAmount";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_allowances",
            ["title"] = "Allowances and Additional Withholding",
            ["layout"] = "section",
            ["fields"] = new List<object>
            {
                AmountLine(line1,
                    "1. Total number of Idaho allowances you\u2019re claiming",
                    "1", null, required: true),
                AmountLine(line2,
                    "2. Additional amount (if any) you need withheld from each paycheck (Enter whole dollars)",
                    "2", "$"),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 5 — SSN (right-aligned label + blue-shaded input)
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildSsnField(PdfExtractionResult raw)
    {
        var ssn = Ann(raw, 1, "f1_03") ?? Ann(raw, 1, "ssn") ?? "id_w4_ssn";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_ssn",
            ["title"] = "",
            ["layout"] = "section",
            ["sectionStyle"] = "text-align:right",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "ssn_label",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-size:8px;font-weight:bold;padding:4px 6px 1px 0;text-align:right\">"
                             + "Your Social Security number (required)</div>",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = ssn,
                    ["type"] = "ssn",
                    ["label"] = "Social Security Number",
                    ["placeholder"] = "XXX-XX-XXXX",
                    ["maxlength"] = 11,
                    ["required"] = true,
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "1",
                    ["gridRow"] = "1",
                    ["width"] = "200px",
                    ["align"] = "right",
                    ["shaded"] = true,
                },
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 6A — Personal info: First name + Last name
    //  Labels ABOVE inputs, thin black border between cells,
    //  blue-shaded input backgrounds
    //  Grid: ~60% first name | ~40% last name
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildPersonalInfoNameRow(PdfExtractionResult raw)
    {
        var firstName = Ann(raw, 1, "f1_04") ?? Ann(raw, 1, "first") ?? "id_w4_firstName";
        var lastName = Ann(raw, 1, "f1_05") ?? Ann(raw, 1, "last") ?? "id_w4_lastName";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_name_row",
            ["title"] = "",
            ["layout"] = "section",
            ["gridColumns"] = "3fr 2fr",
            ["fields"] = new List<object>
            {
                GridCell(firstName, "text", "First name and middle initial", "1", "1",
                    autocomplete: "given-name", shaded: true, required: true),
                GridCell(lastName, "text", "Last name", "2", "1",
                    autocomplete: "family-name", shaded: true, required: true),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 6B — Personal info: Current mailing address (full width)
    //  Label ABOVE input, blue-shaded input
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildPersonalInfoAddressRow(PdfExtractionResult raw)
    {
        var address = Ann(raw, 1, "f1_06") ?? Ann(raw, 1, "address") ?? "id_w4_address";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_address_row",
            ["title"] = "",
            ["layout"] = "section",
            ["fields"] = new List<object>
            {
                GridCell(address, "text", "Current mailing address", "1", "1",
                    autocomplete: "street-address", shaded: true, required: true),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 6C — Personal info: City / State / ZIP Code
    //  Labels ABOVE inputs, thin borders between cells,
    //  blue-shaded inputs
    //  Grid: ~50% city | ~25% state | ~25% zip
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildPersonalInfoCityStateZipRow(PdfExtractionResult raw)
    {
        var city = Ann(raw, 1, "f1_07") ?? Ann(raw, 1, "city") ?? "id_w4_city";
        var state = Ann(raw, 1, "f1_08") ?? Ann(raw, 1, "state") ?? "id_w4_state";
        var zip = Ann(raw, 1, "f1_09") ?? Ann(raw, 1, "zip") ?? "id_w4_zip";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_city_state_zip_row",
            ["title"] = "",
            ["layout"] = "section",
            ["gridColumns"] = "2fr 1fr 1fr",
            ["fields"] = new List<object>
            {
                GridCell(city, "text", "City", "1", "1",
                    autocomplete: "address-level2", shaded: true, required: true),
                GridCell(state, "text", "State", "2", "1",
                    autocomplete: "address-level1", maxlength: 2, shaded: true, required: true),
                GridCell(zip, "text", "ZIP Code", "3", "1",
                    autocomplete: "postal-code", maxlength: 10, shaded: true, required: true),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 7 — Declaration text
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildDeclaration()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_declaration",
            ["title"] = "",
            ["layout"] = "section",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "declaration_text",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-size:9px;line-height:1.4;padding:6px\">"
                             + "Under penalties of perjury, I declare that to the best of my knowledge and belief "
                             + "I can claim the number of withholding allowances on line 1 above.</div>",
                },
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 8 — Signature (60%) + Date (40%)
    //  Labels ABOVE inputs, blue-shaded inputs, bordered between
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildSignature(PdfExtractionResult raw)
    {
        var signature = Ann(raw, 1, "f1_10") ?? Ann(raw, 1, "signature") ?? "id_w4_signature";
        var signDate = Ann(raw, 1, "f1_11") ?? Ann(raw, 1, "date") ?? "id_w4_signDate";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_signature",
            ["title"] = "",
            ["layout"] = "section",
            ["gridColumns"] = "3fr 2fr",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = signature,
                    ["type"] = "signature",
                    ["label"] = "Signature",
                    ["hint"] = "This form is not valid unless you sign it.",
                    ["required"] = true,
                    ["fieldLayout"] = "signature-field",
                    ["shaded"] = true,
                },
                new Dictionary<string, object?>
                {
                    ["id"] = signDate,
                    ["type"] = "date",
                    ["label"] = "Date",
                    ["required"] = true,
                    ["fieldLayout"] = "signature-date",
                    ["shaded"] = true,
                },
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  ROW 9 — Page 1 footer
    //  "EFO00307  04-28-2025" (left) | "Page 1 of 2" (right)
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildPage1Footer()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_page1_footer",
            ["title"] = "",
            ["layout"] = "form-footer",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "footer_p1",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"display:flex;justify-content:space-between;font-size:8px;padding:4px 6px\">"
                             + "<span>EFO00307 \u00a0 04-28-2025</span>"
                             + "<span>Page 1 of 2</span></div>",
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 2 — Instructions + Additional Withholding Worksheet
    // ════════════════════════════════════════════════════════════════

    private static Dictionary<string, object?> BuildPage2(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "page2",
            ["title"] = "Instructions",
            ["sections"] = new List<object>
            {
                BuildPage2Header(),
                BuildLine1Instructions(),
                BuildNonresidentAliens(),
                BuildPayPeriodTable(),
                BuildLine2Instructions(),
                BuildWithholdingWorksheet(raw),
                BuildContactInfo(),
                BuildPage2Footer(),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Page 2 header: "IDAHO State Tax Commission" (left) |
    //                 "Form ID W-4 (continued)" (right)
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildPage2Header()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_page2_header",
            ["title"] = "",
            ["layout"] = "form-header",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "header_p2_left",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-weight:bold;line-height:1.2;padding:6px 10px\">"
                             + "<span style=\"font-size:18px;letter-spacing:1px\">IDAHO</span><br>"
                             + "<span style=\"font-size:10px\">State Tax Commission</span></div>",
                    ["gridColumn"] = "left",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "header_p2_right",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"text-align:right;padding:6px 10px\">"
                             + "<span style=\"font-size:14px;font-weight:bold\">Form ID W-4 (continued)</span></div>",
                    ["gridColumn"] = "right",
                },
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Line 1 instructions — allowances explanation
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildLine1Instructions()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_line1_instructions",
            ["title"] = "Line 1. Total number of allowances you\u2019re claiming.",
            ["layout"] = "instructions",
            ["fields"] = new List<object>
            {
                InstructionParagraph(
                    "You can claim one allowance for yourself. If you\u2019re married filing jointly on your Idaho "
                    + "income tax return, you can also claim one allowance for your spouse (but only if your spouse "
                    + "isn\u2019t already claiming an allowance on a separate Form ID W-4)."),
                InstructionParagraph(
                    "You can claim additional allowances if you have dependents. Generally, the more allowances you "
                    + "claim, the less Idaho income tax your employer will withhold from your paycheck. If you claim "
                    + "too many allowances, you might owe additional tax when you file your return."),
                InstructionParagraph(
                    "If you claim <strong>exempt</strong> status, your employer won\u2019t withhold Idaho income tax "
                    + "from your wages. You can claim exempt status if both of the following are true:"),
                InstructionParagraph(
                    "\u2022 You had no Idaho income tax liability last year, and<br>"
                    + "\u2022 You expect to have no Idaho income tax liability this year."),
                InstructionParagraph(
                    "To claim exempt status, write <strong>\u201cExempt\u201d</strong> on line 1. If you claim exempt "
                    + "status, you\u2019ll need to submit a new Form ID W-4 each year."),
                InstructionParagraph(
                    "If you have more than one job or your spouse also works, you should consider claiming fewer "
                    + "allowances. Otherwise, you might not have enough Idaho income tax withheld."),
                InstructionParagraph(
                    "If you work only part of the year, or recently started a job, your employer might withhold too "
                    + "much Idaho income tax. To compensate, you can claim additional allowances using the worksheet "
                    + "below for line 2."),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Nonresident Aliens instructions
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildNonresidentAliens()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_nonresident_aliens",
            ["title"] = "Nonresident Aliens",
            ["layout"] = "instructions",
            ["fields"] = new List<object>
            {
                InstructionParagraph(
                    "If you\u2019re a nonresident alien, you should claim only one withholding allowance on line 1 "
                    + "unless you\u2019re a resident of Canada, Mexico, South Korea, or a student or business "
                    + "apprentice from India. For more information, see federal Publication 519, U.S. Tax Guide "
                    + "for Aliens."),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Pay Period Table — bordered HTML table
    //  Header: "If your pay period is:" / Weekly / Biweekly / Semimonthly / Monthly
    //  Data: "Enter this amount on line 2:" / $15 / $31 / $33 / $66
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildPayPeriodTable()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_pay_period_table",
            ["title"] = "",
            ["layout"] = "instructions",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "pay_period_table_html",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = BuildPayPeriodTableHtml(),
                },
            },
        };
    }

    private static string BuildPayPeriodTableHtml()
    {
        const string cellStyle = "border:1px solid #000;padding:4px 10px";
        const string headerBg = ";background:#eee;font-weight:bold";

        return "<div style=\"margin:8px 0\">"
             + "<table style=\"border-collapse:collapse;font-size:9px;width:auto\">"
             + "<thead><tr>"
             + $"<th style=\"{cellStyle}{headerBg};text-align:left\">If your pay period is:</th>"
             + $"<th style=\"{cellStyle}{headerBg};text-align:center\">Weekly</th>"
             + $"<th style=\"{cellStyle}{headerBg};text-align:center\">Biweekly</th>"
             + $"<th style=\"{cellStyle}{headerBg};text-align:center\">Semimonthly</th>"
             + $"<th style=\"{cellStyle}{headerBg};text-align:center\">Monthly</th>"
             + "</tr></thead>"
             + "<tbody><tr>"
             + $"<td style=\"{cellStyle};font-weight:bold\">Enter this amount on line 2:</td>"
             + $"<td style=\"{cellStyle};text-align:center\">$15</td>"
             + $"<td style=\"{cellStyle};text-align:center\">$31</td>"
             + $"<td style=\"{cellStyle};text-align:center\">$33</td>"
             + $"<td style=\"{cellStyle};text-align:center\">$66</td>"
             + "</tr></tbody></table></div>";
    }

    // ────────────────────────────────────────────────────────────
    //  Line 2 instructions — additional withholding explanation
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildLine2Instructions()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_line2_instructions",
            ["title"] = "Line 2. Additional amount, if any, you need withheld from each paycheck.",
            ["layout"] = "instructions",
            ["fields"] = new List<object>
            {
                InstructionParagraph(
                    "If you (or your spouse) have more than one job, you might not have enough Idaho income tax "
                    + "withheld from your paycheck. To avoid owing additional tax when you file your return, use "
                    + "the worksheet below to figure how much additional Idaho income tax you need withheld from "
                    + "each paycheck."),
                InstructionParagraph(
                    "Complete this worksheet only if you (or your spouse) have more than one job. Use this form "
                    + "for only one of your jobs. The additional Idaho income tax will be withheld from only "
                    + "this job\u2019s paycheck."),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  9-line Withholding Worksheet
    //  Each line: number + description text + amount input at right
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildWithholdingWorksheet(PdfExtractionResult raw)
    {
        var wsLine1 = Ann(raw, 2, "f2_01") ?? "id_w4_ws_line1";
        var wsLine2 = Ann(raw, 2, "f2_02") ?? "id_w4_ws_line2";
        var wsLine3 = Ann(raw, 2, "f2_03") ?? "id_w4_ws_line3";
        var wsLine4 = Ann(raw, 2, "f2_04") ?? "id_w4_ws_line4";
        var wsLine5 = Ann(raw, 2, "f2_05") ?? "id_w4_ws_line5";
        var wsLine6 = Ann(raw, 2, "f2_06") ?? "id_w4_ws_line6";
        var wsLine7 = Ann(raw, 2, "f2_07") ?? "id_w4_ws_line7";
        var wsLine8 = Ann(raw, 2, "f2_08") ?? "id_w4_ws_line8";
        var wsLine9 = Ann(raw, 2, "f2_09") ?? "id_w4_ws_line9";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_worksheet",
            ["title"] = "Worksheet",
            ["layout"] = "worksheet",
            ["instructions"] = "Use this worksheet only if you (or your spouse) have more than one job.",
            ["fields"] = new List<object>
            {
                WorksheetLine(wsLine1, "1",
                    "How many other jobs do you (or your spouse) expect to work during 2025? "
                    + "(Don\u2019t count your primary job.)",
                    null),
                WorksheetLine(wsLine2, "2",
                    "Multiply line 1 by $15,000",
                    "$"),
                WorksheetLine(wsLine3, "3",
                    "Estimate of your 2025 income from other jobs not listed above. "
                    + "Include your spouse\u2019s income if filing jointly",
                    "$"),
                WorksheetLine(wsLine4, "4",
                    "Enter the smaller of lines 2 or 3",
                    "$"),
                WorksheetLine(wsLine5, "5",
                    "If you plan to itemize deductions on your Idaho income tax return, complete the Itemized "
                    + "Deduction Worksheet in the Idaho income tax booklet and enter the result. "
                    + "Otherwise, enter \u201c0\u201d",
                    "$"),
                WorksheetLine(wsLine6, "6",
                    "Multiply line 5 by $3,868",
                    "$"),
                WorksheetLine(wsLine7, "7",
                    "Subtract line 6 from line 4. If line 6 is more than line 4, enter \u201c0\u201d",
                    "$"),
                WorksheetLine(wsLine8, "8",
                    "Multiply line 7 by 5.3% (.053). This is the additional amount you need to withhold annually",
                    "$"),
                WorksheetLine(wsLine9, "9",
                    "Divide line 8 by the number of remaining pay periods in 2025. Enter the result here "
                    + "and on line 2 of the Form ID W-4 above. (Round to the nearest whole dollar.)",
                    "$"),
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Contact info — centered block with phone numbers + website
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildContactInfo()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_contact",
            ["title"] = "",
            ["layout"] = "instructions",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "contact_info",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-size:9px;line-height:1.6;padding:10px 0;border-top:1px solid #ccc;margin-top:8px;text-align:center\">"
                             + "<strong>Contact us:</strong><br>"
                             + "Boise area: (208) 334-7660 \u00a0\u00a0|\u00a0\u00a0 "
                             + "Toll free: (800) 972-7660 \u00a0\u00a0|\u00a0\u00a0 "
                             + "Hearing impaired (TDD): (800) 377-3529<br>"
                             + "tax.idaho.gov/contact</div>",
                },
            },
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Page 2 footer
    //  "EFO00307  04-28-2025" (left) | "Page 2 of 2" (right)
    // ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildPage2Footer()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_page2_footer",
            ["title"] = "",
            ["layout"] = "form-footer",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "footer_p2",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"display:flex;justify-content:space-between;font-size:8px;padding:4px 6px\">"
                             + "<span>EFO00307 \u00a0 04-28-2025</span>"
                             + "<span>Page 2 of 2</span></div>",
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  HELPER METHODS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find an annotation ID by partial name match on a specific page.
    /// </summary>
    private static string? Ann(PdfExtractionResult raw, int page, string nameFragment)
    {
        return AnnotationMapper.FindByName(raw, page, nameFragment);
    }

    private static Dictionary<string, object?> GridCell(string id, string type, string label,
        string gridColumn, string gridRow,
        string? autocomplete = null, string? placeholder = null, int? maxlength = null,
        bool shaded = false, bool required = false)
    {
        var field = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = type,
            ["label"] = label,
            ["fieldLayout"] = "grid-cell",
            ["gridColumn"] = gridColumn,
            ["gridRow"] = gridRow,
        };
        if (autocomplete is not null) field["autocomplete"] = autocomplete;
        if (placeholder is not null) field["placeholder"] = placeholder;
        if (maxlength is not null) field["maxlength"] = maxlength;
        if (shaded) field["shaded"] = true;
        if (required) field["required"] = true;
        return field;
    }

    private static Dictionary<string, object?> AmountLine(string id, string displayText,
        string amountLabel, string? prefix, bool required = false)
    {
        var field = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = prefix is not null ? "currency" : "number",
            ["label"] = amountLabel,
            ["fieldLayout"] = "amount-line",
            ["displayText"] = displayText,
            ["amountLabel"] = amountLabel,
            ["prefix"] = prefix,
        };
        if (required) field["required"] = true;
        return field;
    }

    private static Dictionary<string, object?> WorksheetLine(string id, string lineNumber,
        string displayText, string? prefix)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = prefix is not null ? "currency" : "number",
            ["label"] = $"Line {lineNumber}",
            ["fieldLayout"] = "worksheet-line",
            ["worksheetLineNumber"] = lineNumber,
            ["displayText"] = displayText,
            ["prefix"] = prefix,
        };
    }

    private static Dictionary<string, object?> InstructionParagraph(string text)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = $"para_{text.GetHashCode():X}",
            ["type"] = "paragraph",
            ["label"] = text,
        };
    }
}
