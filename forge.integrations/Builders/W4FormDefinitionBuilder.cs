using System.Text.Json;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations.Builders;

/// <summary>
/// Hardcoded builder for IRS Form W-4 (Employee's Withholding Certificate).
/// Produces a pixel-perfect ComplianceFormDefinition with correct labels,
/// instructions, layout metadata, and field IDs mapped from the actual PDF.
/// </summary>
public class W4FormDefinitionBuilder : IFormDefinitionBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public ComplianceFormType FormType => ComplianceFormType.W4;

    public string BuilderVersion => "v2";

    public string Build(PdfExtractionResult rawResult)
    {
        // Start with calculator output, then override with exact values
        // reverse-engineered from the source PDF's rendered HTML/CSS.
        var formStyles = PdfMetricsCalculator.Compute(rawResult);

        // ─── Font sizes (measured from source PDF at 100% zoom) ───
        formStyles["gov-font"] = "9px";         // Body/instructions text
        formStyles["gov-font-sm"] = "7px";       // Field labels below inputs
        formStyles["gov-font-md"] = "10px";      // Step number/name labels
        formStyles["gov-font-lg"] = "11px";      // Section headings, larger text
        formStyles["gov-font-title"] = "16px";   // Form header title
        formStyles["gov-font-footer"] = "7px";   // Footer text (Cat. No., etc.)

        // ─── Line heights ───
        formStyles["gov-line-height"] = "1.15";
        formStyles["gov-line-height-tight"] = "1.1";  // For dense label text
        formStyles["gov-line-height-relaxed"] = "1.4"; // For instructions/tips

        // ─── Borders (from source PDF) ───
        formStyles["gov-border-heavy"] = "2px";
        formStyles["gov-border-normal"] = "1px";
        formStyles["gov-border-color"] = "#000";

        // ─── Layout dimensions ───
        formStyles["gov-step-label-pct"] = "14%";        // Step label column width
        formStyles["gov-header-left-pct"] = "15%";       // Form header left column
        formStyles["gov-header-right-pct"] = "12%";      // Form header right column
        formStyles["gov-step-label-padding"] = "6px 8px"; // Step label padding
        formStyles["gov-step-content-padding"] = "0";     // Content padding (grid handles own)
        formStyles["gov-grid-cell-padding"] = "0 6px";    // Grid cell horizontal padding
        formStyles["gov-input-height"] = "18px";          // Grid input height
        formStyles["gov-input-padding"] = "1px 2px";      // Grid input padding
        formStyles["gov-amount-input-height"] = "20px";   // Amount field input height
        formStyles["gov-amount-col-width"] = "155px";     // Right-side amount column
        formStyles["gov-field-label-padding-top"] = "1px"; // Space above field labels
        formStyles["gov-tip-padding"] = "6px 10px";       // TIP block padding
        formStyles["gov-checkbox-size"] = "10px";         // Checkbox box size
        formStyles["gov-radio-size"] = "10px";            // Filing status square box size
        formStyles["gov-radio-border-width"] = "1px";     // Box border width (source PDF uses thin)

        // ─── Header element sizes ───
        formStyles["gov-header-number-size"] = "28px";  // Form number (W-4) in header left
        formStyles["gov-header-year-size"] = "24px";    // Tax year in header right
        formStyles["gov-header-padding"] = "4px 8px";   // Header column padding
        formStyles["gov-header-label-size"] = "7px";     // "Form" label above number

        // ─── Step labels ───
        formStyles["gov-step-number-line-height"] = "1.3";
        formStyles["gov-step-name-line-height"] = "1.3";

        // ─── Filing status ───
        formStyles["gov-filing-status-gap"] = "5px";
        formStyles["gov-filing-status-padding"] = "3px 0";
        formStyles["gov-radio-gap"] = "1px";    // Gap between radio options
        formStyles["gov-radio-padding"] = "1px 0"; // Padding per radio option
        formStyles["gov-radio-line-height"] = "1.35";

        // ─── Amount lines ───
        formStyles["gov-amount-line-gap"] = "3px";
        formStyles["gov-amount-line-padding"] = "1px 0";
        formStyles["gov-amount-dots-min-width"] = "20px";
        formStyles["gov-amount-dots-max-width"] = "80px";
        formStyles["gov-amount-field-padding"] = "1px 4px";
        formStyles["gov-amount-label-min-width"] = "22px";

        // ─── Checkbox ───
        formStyles["gov-checkbox-line-padding"] = "2px 0";
        formStyles["gov-checkbox-inline-gap"] = "6px";
        formStyles["gov-checkbox-inline-padding"] = "3px 0";

        // ─── Signature ───
        formStyles["gov-signature-gap"] = "10px";
        formStyles["gov-signature-font-size"] = "18px";
        formStyles["gov-signature-height"] = "28px";
        formStyles["gov-signature-date-width"] = "15%";

        // ─── Exempt section ───
        formStyles["gov-exempt-label-padding"] = "8px";
        formStyles["gov-exempt-content-padding"] = "8px 10px";

        // ─── Employers section ───
        formStyles["gov-employers-label-padding"] = "8px";
        formStyles["gov-employers-content-padding"] = "4px 10px";
        formStyles["gov-employers-row-min-height"] = "40px";
        formStyles["gov-emp-field-padding"] = "3px 8px";

        // ─── Instructions & worksheets ───
        formStyles["gov-inst-text-line-height"] = "1.4";
        formStyles["gov-ws-line-padding"] = "4px 0";
        formStyles["gov-ws-num-width"] = "16px";

        // ─── Colors ───
        formStyles["gov-shading-color"] = "rgba(200, 215, 235, 0.2)";
        formStyles["gov-shading-color-light"] = "rgba(200, 215, 235, 0.15)";
        formStyles["gov-shading-color-dark"] = "rgba(100, 150, 200, 0.05)";

        var definition = new Dictionary<string, object?>
        {
            ["formType"] = "W4",
            ["builderVersion"] = BuilderVersion,
            ["title"] = "W-4",
            ["formNumber"] = "Form W-4",
            ["revision"] = DateTimeOffset.UtcNow.ToString("yyyy"),
            ["agency"] = "Department of the Treasury — Internal Revenue Service",
            ["formLayout"] = "government",
            ["maxWidth"] = "850px",
            ["formStyles"] = formStyles,
            ["pages"] = new List<object>
            {
                BuildPage1(rawResult),
                BuildPage2(),
                BuildPage3(rawResult),
                BuildPage4(rawResult),
                BuildPage5(),
            },
        };

        return JsonSerializer.Serialize(definition, JsonOptions);
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 1 — Employee's Withholding Certificate
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
                BuildStep1(raw),
                BuildTipAndGating(),
                BuildStep2(raw),
                BuildStep3(raw),
                BuildStep4(raw),
                BuildExemptRow(raw),
                BuildStep5(raw),
                BuildEmployersOnly(raw),
                BuildFormFooter(),
            },
        };
    }

    private static Dictionary<string, object?> BuildFormHeader()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_header",
            ["title"] = "Form W-4",
            ["layout"] = "form-header",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "header_left",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<span class=\"gov-form__header-form-line\"><span class=\"gov-form__header-label\">Form</span> <span class=\"gov-form__header-number\">W-4</span></span>"
                             + "<span class=\"gov-form__header-agency\">Department of the Treasury<br>Internal Revenue Service</span>",
                    ["gridColumn"] = "left",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "header_center",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<span class=\"gov-form__header-title\">Employee\u2019s Withholding Certificate</span>"
                             + "<span class=\"gov-form__header-subtitle\">Complete Form W-4 so that your employer can withhold the correct federal income tax from your pay.</span>"
                             + "<span class=\"gov-form__header-subtitle-bold\">Give Form W-4 to your employer.</span>"
                             + "<span class=\"gov-form__header-subtitle\">Your withholding is subject to review by the IRS.</span>",
                    ["gridColumn"] = "center",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "header_right",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<span class=\"gov-form__header-omb\">OMB No. 1545-0074</span>"
                             + "<span class=\"gov-form__header-year\"><span class=\"gov-form__header-year-outline\">20</span>26</span>",
                    ["gridColumn"] = "right",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildStep1(PdfExtractionResult raw)
    {
        // Map annotation IDs from the actual PDF
        var firstName = Ann(raw, 1, "f1_01") ?? "w4_firstName";
        var lastName = Ann(raw, 1, "f1_02") ?? "w4_lastName";
        var address = Ann(raw, 1, "f1_03") ?? "w4_address";
        var cityStateZip = Ann(raw, 1, "f1_04") ?? "w4_cityStateZip";
        var ssn = Ann(raw, 1, "f1_05") ?? "w4_ssn";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_step1",
            ["title"] = "Step 1: Enter Personal Information",
            ["layout"] = "step",
            ["stepNumber"] = "Step 1:",
            ["stepName"] = "Enter\nPersonal\nInformation",
            ["gridColumns"] = "2fr 1fr 1fr",
            ["fields"] = new List<object>
            {
                GridCell(firstName, "text", "(a) First name and middle initial", "1", "1", autocomplete: "given-name", required: true),
                GridCell(lastName, "text", "Last name", "2", "1", autocomplete: "family-name", required: true),
                GridCell(ssn, "ssn", "(b) Social security number", "3", "1", placeholder: "XXX-XX-XXXX", maxlength: 11, required: true),
                GridCell(address, "text", "Address", "1 / span 2", "2", autocomplete: "street-address", required: true),
                new Dictionary<string, object?>
                {
                    ["id"] = "ssn_callout",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"border:2px solid var(--error);padding:6px 8px;font-size:10px;line-height:1.3\">Does your name match the name on your social security card? If not, to ensure you get credit for your earnings, contact SSA at 800-772-1213 or go to <em>www.ssa.gov</em>.</div>",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "3",
                    ["gridRow"] = "2 / span 2",
                },
                GridCell(cityStateZip, "text", "City or town, state, and ZIP code", "1 / span 2", "3", autocomplete: "address-level2", required: true),
                new Dictionary<string, object?>
                {
                    ["id"] = "filingStatus",
                    ["type"] = "radio",
                    ["label"] = "(c) Filing status",
                    ["required"] = true,
                    ["fieldLayout"] = "filing-status",
                    ["checkboxStyle"] = "square",
                    ["options"] = new List<object>
                    {
                        new Dictionary<string, object?> { ["value"] = "Single", ["label"] = "Single or Married filing separately" },
                        new Dictionary<string, object?> { ["value"] = "Married", ["label"] = "Married filing jointly or Qualifying surviving spouse" },
                        new Dictionary<string, object?>
                        {
                            ["value"] = "HeadOfHousehold",
                            ["label"] = "Head of household",
                            ["hint"] = "Check only if you're unmarried and pay more than half the costs of keeping up a home for yourself and a qualifying individual.",
                        },
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "step1_caution",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-size:10px;line-height:1.4;padding-top:4px\"><strong>Caution:</strong> To claim certain credits or deductions on your tax return, you (and/or your spouse if married filing jointly) are required to have a social security number valid for employment. See page 2 for more information.</div>",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildTipAndGating()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_tip",
            ["title"] = "",
            ["layout"] = "tip",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "tip_estimator",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-size:10px;line-height:1.5\"><strong>TIP:</strong> Consider using the estimator at <em>www.irs.gov/W4App</em> to determine the most accurate withholding for the rest of the year if you: are completing this form after the beginning of the year; expect to work only part of the year; or have changes during the year in your marital status, number of jobs for you (and/or your spouse if married filing jointly), dependents, other income (not from jobs), deductions, or credits. Have your most recent pay stub(s) from this year available when using the estimator. At the beginning of next year, use the estimator again to recheck your withholding.</div>",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "gating_instruction",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"font-size:10px;line-height:1.5;padding-top:6px\">Complete Steps 2\u20134 <strong>ONLY</strong> if they apply to you; otherwise, skip to Step 5. See page 2 for more information on each step, who can claim exemption from withholding, and when to use the estimator at <em>www.irs.gov/W4App</em>.</div>",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildStep2(PdfExtractionResult raw)
    {
        var twoJobsCheckbox = Ann(raw, 1, "c1_2") ?? "w4_twoJobs";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_step2",
            ["title"] = "Step 2: Multiple Jobs or Spouse Works",
            ["layout"] = "step",
            ["shaded"] = true,
            ["stepNumber"] = "Step 2:",
            ["stepName"] = "Multiple Jobs\nor Spouse\nWorks",
            ["instructions"] = "Complete this step if you (1) hold more than one job at a time, or (2) are married filing jointly and your spouse also works. The correct amount of withholding depends on income earned from all of these jobs.",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "step2_do_only_one",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "Do <strong>only one</strong> of the following.",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "step2_option_a",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<strong>(a)</strong> Use the estimator at www.irs.gov/W4App for the most accurate withholding for this step (and Steps 3\u20134). If you or your spouse have self-employment income, use this option; <strong>or</strong>",
                    ["cssClass"] = "gov-form__text--indent",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "step2_option_b",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<strong>(b)</strong> Use the Multiple Jobs Worksheet on page 3 and enter the result in Step 4(c) below; <strong>or</strong>",
                    ["cssClass"] = "gov-form__text--indent",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = twoJobsCheckbox,
                    ["type"] = "checkbox",
                    ["label"] = "",
                    ["displayText"] = "<strong>(c)</strong> If there are only two jobs total, you may check this box. Do the same on Form W-4 for the other job. This option is generally more accurate than Step 2(b) if pay at the lower paying job is more than half of the pay at the higher paying job. Otherwise, Step 2(b) is more accurate",
                    ["fieldLayout"] = "checkbox-dots",
                    ["cssClass"] = "gov-form__text--indent",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "step2_complete_note",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<strong>Complete Steps 3\u20134(b) on Form W-4 for only ONE of these jobs.</strong> Leave those steps blank for the other jobs. (Your withholding will be most accurate if you complete Steps 3\u20134(b) on the Form W-4 for the highest paying job.)",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildStep3(PdfExtractionResult raw)
    {
        var qualifyingChildren = Ann(raw, 1, "f1_06") ?? "w4_qualifyingChildren";
        var otherDependents = Ann(raw, 1, "f1_07") ?? "w4_otherDependents";
        var totalCredits = Ann(raw, 1, "f1_08") ?? "w4_totalCredits";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_step3",
            ["title"] = "Step 3: Claim Dependent and Other Credits",
            ["layout"] = "step-amounts",
            ["stepNumber"] = "Step 3:",
            ["stepName"] = "Claim\nDependents",
            ["innerColumnWidth"] = "300px",
            ["instructions"] = "If your total income will be $200,000 or less ($400,000 or less if married filing jointly):",
            ["fields"] = new List<object>
            {
                AmountLineInner(qualifyingChildren, "Multiply the number of qualifying children under age 17 by $2,200", "3(a)", "$"),
                AmountLineInner(otherDependents, "Multiply the number of other dependents by $500", "3(b)", "$"),
                AmountLineTotal(totalCredits, "Add the amounts from Steps 3(a) and 3(b), plus the amount for other credits. Enter the total here", "3", "$"),
            },
        };
    }

    private static Dictionary<string, object?> BuildStep4(PdfExtractionResult raw)
    {
        var otherIncome = Ann(raw, 1, "f1_09") ?? "w4_otherIncome";
        var deductions = Ann(raw, 1, "f1_10") ?? "w4_deductions";
        var extraWithholding = Ann(raw, 1, "f1_11") ?? "w4_extraWithholding";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_step4",
            ["title"] = "Step 4: Other Adjustments",
            ["layout"] = "step-amounts",
            ["shaded"] = true,
            ["stepNumber"] = "Step 4:",
            ["stepName"] = "Other\nAdjustments\n(optional)",
            ["amountColumnWidth"] = "155px",
            ["fields"] = new List<object>
            {
                AmountLine(otherIncome, "<strong>Other income (not from jobs).</strong> If you want tax withheld for other income you expect this year that won't have withholding, enter the amount of other income here. This may include interest, dividends, and retirement income", "4(a)", "$"),
                AmountLine(deductions, "<strong>Deductions.</strong> If you expect to claim deductions other than the standard deduction and want to reduce your withholding, use the Deductions Worksheet on page 4 and enter the result here", "4(b)", "$"),
                AmountLine(extraWithholding, "<strong>Extra withholding.</strong> Enter any additional tax you want withheld each pay period", "4(c)", "$"),
            },
        };
    }

    private static Dictionary<string, object?> BuildExemptRow(PdfExtractionResult raw)
    {
        var exemptCheckbox = Ann(raw, 1, "c1_3") ?? "w4_exempt";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_exempt",
            ["title"] = "Exempt from\nwithholding",
            ["layout"] = "exempt",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = exemptCheckbox,
                    ["type"] = "checkbox",
                    ["label"] = "",
                    ["displayText"] = "I claim exemption from withholding for 2026, and I certify that I meet both of the conditions for exemption for 2026. See <em>Exemption from withholding</em> on page 2. I understand I will need to submit a new Form W-4 for 2027.",
                    ["fieldLayout"] = "checkbox-dots",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildStep5(PdfExtractionResult raw)
    {
        var signature = Ann(raw, 1, "f1_12") ?? "w4_signature";
        var signDate = Ann(raw, 1, "f1_13") ?? "w4_signDate";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_step5",
            ["title"] = "Step 5: Sign Here",
            ["layout"] = "step",
            ["stepNumber"] = "Step 5:",
            ["stepName"] = "Sign\nHere",
            ["heavyBorder"] = true,
            ["instructions"] = "Under penalties of perjury, I declare that this certificate, to the best of my knowledge and belief, is true, correct, and complete.",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = signature,
                    ["type"] = "signature",
                    ["label"] = "Employee's signature",
                    ["hint"] = "This form is not valid unless you sign it.",
                    ["required"] = true,
                    ["fieldLayout"] = "signature-field",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = signDate,
                    ["type"] = "date",
                    ["label"] = "Date",
                    ["required"] = true,
                    ["fieldLayout"] = "signature-date",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildEmployersOnly(PdfExtractionResult raw)
    {
        var ein = Ann(raw, 1, "f1_14") ?? "w4_ein";

        return new Dictionary<string, object?>
        {
            ["id"] = "section_employers",
            ["title"] = "Employers Only",
            ["layout"] = "employers-only",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "emp_name_address",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "Employer's name and address",
                    ["cssClass"] = "gov-form__emp-field--wide",
                },
                Field("emp_first_date", "text", "First date of employment"),
                Field(ein, "text", "Employer identification number (EIN)"),
            },
        };
    }

    private static Dictionary<string, object?> BuildFormFooter()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_footer",
            ["title"] = "",
            ["layout"] = "form-footer",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "footer_text",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "For Privacy Act and Paperwork Reduction Act Notice, see page 4.  Cat. No. 10220Q  Form <strong>W-4</strong> (2026)",
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 2 — General Instructions (read-only)
    // ════════════════════════════════════════════════════════════════

    private static Dictionary<string, object?> BuildPage2()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "page2",
            ["title"] = "Instructions",
            ["readonly"] = true,
            ["sections"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "section_general_instructions",
                    ["title"] = "General Instructions",
                    ["layout"] = "instructions",
                    ["fields"] = new List<object>
                    {
                        InstructionParagraph("Section references are to the Internal Revenue Code unless otherwise noted."),
                        InstructionHeading("Future Developments"),
                        InstructionParagraph("For the latest information about developments related to Form W-4, such as legislation enacted after it was published, go to www.irs.gov/FormW4."),
                        InstructionHeading("Purpose of Form"),
                        InstructionParagraph("Complete Form W-4 so that your employer can withhold the correct federal income tax from your pay. If too little is withheld, you will generally owe tax when you file your tax return and may owe a penalty. If too much is withheld, you will generally be due a refund. Complete a new Form W-4 when changes to your personal or financial situation would change the entries on the form. For more information on withholding and when you must furnish a new Form W-4, see Pub. 505, Tax Withholding and Estimated Tax."),
                        InstructionHeading("Exemption from withholding"),
                        InstructionParagraph("You may claim exemption from withholding for 2026 if you meet both of the following conditions: you had no federal income tax liability in 2025 and you expect to have no federal income tax liability in 2026. You had no federal income tax liability in 2025 if (1) your total tax on line 24 on your 2025 Form 1040 or 1040-SR is zero (or less than the sum of lines 27a, 28, 29, and 30), or (2) you were not required to file a return because your income was below the filing threshold for your correct filing status. If you claim exemption, you will have no income tax withheld from your paycheck and may owe taxes and penalties when you file your 2026 tax return. To claim exemption from withholding, certify that you meet both of the conditions by checking the box in the Exempt from withholding section. Then, complete Steps 1(a), 1(b), and 5. Do not complete any other steps. You will need to submit a new Form W-4 by February 16, 2027."),
                        InstructionHeading("Your privacy"),
                        InstructionParagraph("Steps 2(c) and 4(a) ask for information regarding income you received from sources other than the job associated with this Form W-4. If you have concerns with providing the information asked for in Step 2(c), you may choose Step 2(b) as an alternative; if you have concerns with providing the information asked for in Step 4(a), you may enter an additional amount you want withheld per pay period in Step 4(c) as an alternative."),
                        InstructionHeading("When to use the estimator"),
                        InstructionParagraph("Consider using the estimator at www.irs.gov/W4App if you:\n\n1. Are submitting this form after the beginning of the year;\n2. Expect to work only part of the year;\n3. Have changes during the year in your marital status, number of jobs for you (and/or your spouse if married filing jointly), or number of dependents, or changes in your deductions or credits;\n4. Receive dividends, capital gains, social security, bonuses, or business income, or are subject to the Additional Medicare Tax or Net Investment Income Tax; or\n5. Prefer the most accurate withholding for multiple job situations.\n\nTIP: Have your most recent pay stub(s) from this year available when using the estimator to account for federal income tax that has already been withheld this year. At the beginning of next year, use the estimator again to recheck your withholding."),
                        InstructionHeading("Self-employment"),
                        InstructionParagraph("Generally, you will owe both income and self-employment taxes on any self-employment income you receive separate from the wages you receive as an employee. If you want to pay these taxes through withholding from your wages, use the estimator at www.irs.gov/W4App to figure the amount to have withheld."),
                        InstructionHeading("Nonresident alien"),
                        InstructionParagraph("If you're a nonresident alien, see Notice 1392, Supplemental Form W-4 Instructions for Nonresident Aliens, before completing this form."),
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "section_specific_instructions",
                    ["title"] = "Specific Instructions",
                    ["layout"] = "instructions",
                    ["fields"] = new List<object>
                    {
                        InstructionHeading("Step 1(c)"),
                        InstructionParagraph("Check your anticipated filing status. This will determine the standard deduction and tax rates used to compute your withholding."),
                        InstructionHeading("Step 2"),
                        InstructionParagraph("Use this step if you (1) have more than one job at the same time, or (2) are married filing jointly and you and your spouse both work. Submit a separate Form W-4 for each job.\n\nOption (a) most accurately calculates the additional tax you need to have withheld, while option (b) does so with a little less accuracy.\n\nInstead, if you (and your spouse) have a total of only two jobs, you may check the box in option (c). The box must also be checked on the Form W-4 for the other job. If the box is checked, the standard deduction and tax brackets will be cut in half for each job to calculate withholding. This option is accurate for jobs with similar pay; otherwise, more tax than necessary may be withheld, and this extra amount of tax withheld will be larger the greater the difference in pay is between the two jobs."),
                        InstructionParagraph("Multiple jobs. Complete Steps 3 through 4(b) on only one Form W-4. Withholding will be most accurate if you do this on the Form W-4 for the highest paying job."),
                        InstructionHeading("Step 3"),
                        InstructionParagraph("This step provides instructions for determining the amount of the child tax credit and the credit for other dependents that you may be able to claim when you file your tax return. To qualify for the child tax credit, the child must be under age 17 as of December 31, must be your dependent who generally lives with you for more than half the year, and must have the required social security number. You (and/or your spouse if married filing jointly) must have the required social security number to claim certain credits. You may be able to claim a credit for other dependents for whom a child tax credit can't be claimed, such as an older child or a qualifying relative. For additional eligibility requirements for these credits, see Pub. 501, Dependents, Standard Deduction, and Filing Information. You can also include other tax credits for which you are eligible in this step, such as the foreign tax credit and the education tax credits. To do so, add an estimate of the amount for the year to your credits for dependents and enter the total amount in Step 3. Including these credits will increase your paycheck and reduce the amount of any refund you may receive when you file your tax return."),
                        InstructionHeading("Step 4"),
                        InstructionParagraph("Step 4(a). Enter in this step the total of your other estimated income for the year, if any. You shouldn't include income from any jobs or self-employment. If you complete Step 4(a), you likely won't have to make estimated tax payments for that income. If you prefer to pay estimated tax rather than having tax on other income withheld from your paycheck, see Form 1040-ES, Estimated Tax for Individuals."),
                        InstructionParagraph("Step 4(b). Enter in this step the amount from the Deductions Worksheet, line 15, if you expect to claim deductions other than the basic standard deduction on your 2026 tax return and want to reduce your withholding to account for these deductions. This includes both itemized deductions and other deductions such as for qualified tips, overtime compensation, and passenger vehicle loan interest; student loan interest; IRAs; and seniors. You (and/or your spouse if married filing jointly) must have the required social security number to claim certain deductions. For additional eligibility requirements, see Pub. 501."),
                        InstructionParagraph("Step 4(c). Enter in this step any additional tax you want withheld from your pay each pay period, including any amounts from the Multiple Jobs Worksheet, line 4. Entering an amount here will reduce your paycheck and will either increase your refund or reduce any amount of tax that you owe when you file your tax return."),
                    },
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 3 — Multiple Jobs Worksheet
    // ════════════════════════════════════════════════════════════════

    private static Dictionary<string, object?> BuildPage3(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "page3",
            ["title"] = "Worksheet",
            ["sections"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "section_worksheet2b",
                    ["title"] = "Step 2(b)\u2014Multiple Jobs Worksheet",
                    ["subtitle"] = "(Keep for your records.)",
                    ["layout"] = "worksheet",
                    ["instructions"] = "If you choose the option in Step 2(b) on Form W-4, complete this worksheet (which calculates the total extra tax for all jobs) on only ONE Form W-4. Withholding will be most accurate if you complete the worksheet and enter the result on the Form W-4 for the highest paying job. To be accurate, submit a new Form W-4 for all other jobs if you have not updated your withholding since 2019.",
                    ["note"] = "Note: If more than one job has annual wages of more than $120,000 or there are more than three jobs, see Pub. 505 for additional tables; or, you can use the online withholding estimator at www.irs.gov/W4App.",
                    ["fields"] = new List<object>
                    {
                        WorksheetLine(Ann(raw, 3, "f3_01") ?? "w4_ws_line1", "1",
                            "<strong>Two jobs.</strong> If you have two jobs or you\u2019re married filing jointly and you and your spouse each have one job, find the amount from the appropriate table on page 5. Using the \u201cHigher Paying Job\u201d row and the \u201cLower Paying Job\u201d column, find the value at the intersection of the two household salaries and enter that value on line 1. Then, skip to line 3", "$"),
                        WorksheetLine(Ann(raw, 3, "f3_02") ?? "w4_ws_line2a", "2a",
                            "<strong>Three jobs.</strong> If you and/or your spouse have three jobs at the same time, complete lines 2a, 2b, and 2c below. Otherwise, skip to line 3.\n\nFind the amount from the appropriate table on page 5 using the annual wages from the highest paying job in the \u201cHigher Paying Job\u201d row and the annual wages for your next highest paying job in the \u201cLower Paying Job\u201d column. Find the value at the intersection of the two household salaries and enter that value on line 2a", "$"),
                        WorksheetLine(Ann(raw, 3, "f3_03") ?? "w4_ws_line2b", "2b",
                            "Add the annual wages of the two highest paying jobs from line 2a together and use the total as the wages in the \u201cHigher Paying Job\u201d row and use the annual wages for your third job in the \u201cLower Paying Job\u201d column to find the amount from the appropriate table on page 5 and enter this amount on line 2b", "$"),
                        WorksheetLine(Ann(raw, 3, "f3_04") ?? "w4_ws_line2c", "2c",
                            "Add the amounts from lines 2a and 2b and enter the result on line 2c", "$"),
                        WorksheetLine(Ann(raw, 3, "f3_05") ?? "w4_ws_line3", "3",
                            "Enter the number of pay periods per year for the highest paying job. For example, if that job pays weekly, enter 52; if it pays every other week, enter 26; if it pays monthly, enter 12, etc.", null),
                        WorksheetLine(Ann(raw, 3, "f3_06") ?? "w4_ws_line4", "4",
                            "Divide the annual amount on line 1 or line 2c by the number of pay periods on line 3. Enter this amount here and in Step 4(c) of Form W-4 for the highest paying job (plus any other additional amount you want withheld)", "$"),
                    },
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 4 — Deductions Worksheet + Privacy Act
    // ════════════════════════════════════════════════════════════════

    private static Dictionary<string, object?> BuildPage4(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "page4",
            ["title"] = "Deductions",
            ["sections"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "section_deductions",
                    ["title"] = "Step 4(b)\u2014Deductions Worksheet",
                    ["subtitle"] = "(Keep for your records.)",
                    ["layout"] = "worksheet",
                    ["instructions"] = "See the Instructions for Schedule 1-A (Form 1040) for more information about whether you qualify for the deductions on lines 1a, 1b, 1c, 3a, and 3b.",
                    ["fields"] = new List<object>
                    {
                        // Line 1 group: Qualified tips, overtime, vehicle loan interest
                        WorksheetLine(Ann(raw, 4, "f4_01") ?? "w4_ded_1a", "1a",
                            "<strong>Deductions for qualified tips, overtime compensation, and passenger vehicle loan interest.</strong>\n\n<strong>Qualified tips.</strong> If your total income is less than $150,000 ($300,000 if married filing jointly), enter an estimate of your qualified tips up to $25,000", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_02") ?? "w4_ded_1b", "1b",
                            "<strong>Qualified overtime compensation.</strong> If your total income is less than $150,000 ($300,000 if married filing jointly), enter an estimate of your qualified overtime compensation up to $12,500 ($25,000 if married filing jointly) of the \u201cand-a-half\u201d portion of time-and-a-half compensation", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_03") ?? "w4_ded_1c", "1c",
                            "<strong>Qualified passenger vehicle loan interest.</strong> If your total income is less than $100,000 ($200,000 if married filing jointly), enter an estimate of your qualified passenger vehicle loan interest up to $10,000", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_04") ?? "w4_ded_2", "2",
                            "Add lines 1a, 1b, and 1c. Enter the result here", "$"),

                        // Line 3 group: Seniors
                        WorksheetLine(Ann(raw, 4, "f4_05") ?? "w4_ded_3a", "3a",
                            "<strong>Seniors age 65 or older.</strong> If your total income is less than $75,000 ($150,000 if married filing jointly):\n\nEnter $6,000 if you are age 65 or older before the end of the year", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_06") ?? "w4_ded_3b", "3b",
                            "Enter $6,000 if your spouse is age 65 or older before the end of the year and has a social security number valid for employment", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_07") ?? "w4_ded_4", "4",
                            "Add lines 3a and 3b. Enter the result here", "$"),

                        // Line 5: Other adjustments
                        WorksheetLine(Ann(raw, 4, "f4_08") ?? "w4_ded_5", "5",
                            "Enter an estimate of your student loan interest, deductible IRA contributions, educator expenses, alimony paid, and certain other adjustments from Schedule 1 (Form 1040), Part II. See Pub. 505 for more information", "$"),

                        // Line 6 group: Itemized deductions
                        WorksheetLine(Ann(raw, 4, "f4_09") ?? "w4_ded_6a", "6a",
                            "<strong>Itemized deductions.</strong> Enter an estimate of your 2026 itemized deductions from Schedule A (Form 1040). Such deductions may include qualifying:\n\n<strong>Medical and dental expenses.</strong> Enter expenses in excess of 7.5% (0.075) of your total income", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_10") ?? "w4_ded_6b", "6b",
                            "<strong>State and local taxes.</strong> If your total income is less than $505,000 ($252,500 if married filing separately), enter state and local taxes paid up to $40,400 ($20,200 if married filing separately)", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_11") ?? "w4_ded_6c", "6c",
                            "<strong>Home mortgage interest.</strong> If your home acquisition debt is less than $750,000 ($375,000 if married filing separately), enter your home mortgage interest expense (including mortgage insurance premiums)", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_12") ?? "w4_ded_6d", "6d",
                            "<strong>Gifts to charities.</strong> Enter contributions in excess of 0.5% (0.005) of your total income", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_13") ?? "w4_ded_6e", "6e",
                            "<strong>Other itemized deductions.</strong> Enter the amount for other itemized deductions", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_14") ?? "w4_ded_7", "7",
                            "Add lines 6a, 6b, 6c, 6d, and 6e. Enter the result here", "$"),

                        // Line 8 group: Limitation on itemized deductions
                        WorksheetLine(Ann(raw, 4, "f4_15") ?? "w4_ded_8a", "8a",
                            "<strong>Limitation on itemized deductions.</strong>\n\nEnter your total income", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_16") ?? "w4_ded_8b", "8b",
                            "Subtract line 4 from line 8a. If line 4 is greater than line 8a, enter -0- here and on line 10. Skip line 9", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_17") ?? "w4_ded_9", "9",
                            "Enter: \u2022 $768,700 if you\u2019re married filing jointly or a qualifying surviving spouse \u2022 $640,600 if you\u2019re single or head of household \u2022 $384,350 if you\u2019re married filing separately", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_18") ?? "w4_ded_10", "10",
                            "If line 9 is greater than line 8b, enter the amount from line 7. Otherwise, multiply line 7 by 94% (0.94) and enter the result here", "$"),

                        // Line 11-15: Standard deduction and final calculation
                        WorksheetLine(Ann(raw, 4, "f4_19") ?? "w4_ded_11", "11",
                            "<strong>Standard deduction.</strong>\n\nEnter: \u2022 $32,200 if you\u2019re married filing jointly or a qualifying surviving spouse \u2022 $24,150 if you\u2019re head of household \u2022 $16,100 if you\u2019re single or married filing separately", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_20") ?? "w4_ded_12", "12",
                            "<strong>Cash gifts to charities.</strong> If you take the standard deduction, enter cash contributions up to $1,000 ($2,000 if married filing jointly)", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_21") ?? "w4_ded_13", "13",
                            "Add lines 11 and 12. Enter the result here", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_22") ?? "w4_ded_14", "14",
                            "If line 10 is greater than line 13, subtract line 11 from line 10 and enter the result here. If line 13 is greater than line 10, enter the amount from line 12", "$"),
                        WorksheetLine(Ann(raw, 4, "f4_23") ?? "w4_ded_15", "15",
                            "Add lines 2, 4, 5, and 14. Enter the result here and in Step 4(b) of Form W-4", "$"),
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "section_privacy",
                    ["title"] = "Privacy Act and Paperwork Reduction Act Notice",
                    ["layout"] = "instructions",
                    ["fields"] = new List<object>
                    {
                        InstructionParagraph("We ask for the information on this form to carry out the Internal Revenue laws of the United States. Internal Revenue Code sections 3402(f)(2) and 6109 and their regulations require you to provide this information; your employer uses it to determine your federal income tax withholding. Failure to provide a properly completed form will result in your being treated as a single person with no other entries on the form; providing fraudulent information may subject you to penalties. Routine uses of this information include giving it to the Department of Justice for civil and criminal litigation; to cities, states, the District of Columbia, and U.S. commonwealths and territories for use in administering their tax laws; and to the Department of Health and Human Services for use in the National Directory of New Hires. We may also disclose this information to other countries under a tax treaty, to federal and state agencies to enforce federal nontax criminal laws, or to federal law enforcement and intelligence agencies to combat terrorism."),
                        InstructionParagraph("You are not required to provide the information requested on a form that is subject to the Paperwork Reduction Act unless the form displays a valid OMB control number. Books or records relating to a form or its instructions must be retained as long as their contents may become material in the administration of any Internal Revenue law. Generally, tax returns and return information are confidential, as required by Code section 6103."),
                        InstructionParagraph("The average time and expenses required to complete and file this form will vary depending on individual circumstances. For estimated averages, see the instructions for your income tax return."),
                        InstructionParagraph("If you have suggestions for making this form simpler, we would be happy to hear from you. See the instructions for your income tax return."),
                    },
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 5 — Tax Tables (read-only)
    // ════════════════════════════════════════════════════════════════

    private static Dictionary<string, object?> BuildPage5()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "page5",
            ["title"] = "Tables",
            ["readonly"] = true,
            ["sections"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "section_table_mfj",
                    ["title"] = "Married Filing Jointly or Qualifying Surviving Spouse",
                    ["layout"] = "instructions",
                    ["fields"] = new List<object>
                    {
                        TaxTableHtml(BuildMfjTable()),
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "section_table_single",
                    ["title"] = "Single or Married Filing Separately",
                    ["layout"] = "instructions",
                    ["fields"] = new List<object>
                    {
                        TaxTableHtml(BuildSingleTable()),
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "section_table_hoh",
                    ["title"] = "Head of Household",
                    ["layout"] = "instructions",
                    ["fields"] = new List<object>
                    {
                        TaxTableHtml(BuildHohTable()),
                    },
                },
            },
        };
    }

    private static Dictionary<string, object?> TaxTableHtml(string tableHtml)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = $"table_{tableHtml.GetHashCode():X}",
            ["type"] = "html",
            ["label"] = "",
            ["html"] = tableHtml,
        };
    }

    private static readonly string[] LowerPayColumns =
    [
        "$0 -\n9,999", "$10,000 -\n19,999", "$20,000 -\n29,999", "$30,000 -\n39,999",
        "$40,000 -\n49,999", "$50,000 -\n59,999", "$60,000 -\n69,999", "$70,000 -\n79,999",
        "$80,000 -\n89,999", "$90,000 -\n99,999", "$100,000 -\n109,999", "$110,000 -\n120,000",
    ];

    private static string BuildTaxTableHtml(string[][] rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<div style=\"overflow-x:auto\"><table style=\"border-collapse:collapse;font-size:9px;width:100%;text-align:right\">");
        // Header row
        sb.Append("<thead><tr><th style=\"border:1px solid #999;padding:3px 4px;text-align:left;background:#eee;min-width:90px\">Higher Paying Job<br>Annual Taxable<br>Wage &amp; Salary</th>");
        foreach (var col in LowerPayColumns)
            sb.Append($"<th style=\"border:1px solid #999;padding:3px 4px;background:#eee;min-width:55px;white-space:pre-line\">{col}</th>");
        sb.Append("</tr></thead><tbody>");
        // Data rows
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            for (var i = 0; i < row.Length; i++)
            {
                var style = i == 0
                    ? "border:1px solid #999;padding:3px 4px;text-align:left;font-weight:bold;white-space:nowrap"
                    : "border:1px solid #999;padding:3px 4px";
                sb.Append($"<td style=\"{style}\">{row[i]}</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }

    private static string BuildMfjTable()
    {
        var rows = new[]
        {
            new[] { "$0 - 9,999", "$0", "$0", "$480", "$850", "$850", "$1,020", "$1,020", "$1,020", "$1,020", "$1,020", "$1,020", "$1,020" },
            new[] { "$10,000 - 19,999", "0", "480", "1,480", "1,850", "2,050", "2,220", "2,220", "2,220", "2,220", "2,220", "2,220", "2,620" },
            new[] { "$20,000 - 29,999", "480", "1,480", "2,480", "3,050", "3,250", "3,420", "3,420", "3,420", "3,420", "3,420", "3,820", "4,820" },
            new[] { "$30,000 - 39,999", "850", "1,850", "3,050", "3,620", "3,820", "3,990", "3,990", "3,990", "3,990", "4,390", "5,390", "6,390" },
            new[] { "$40,000 - 49,999", "850", "2,050", "3,250", "3,820", "4,020", "4,190", "4,190", "4,190", "4,590", "5,590", "6,590", "7,590" },
            new[] { "$50,000 - 59,999", "1,020", "2,220", "3,420", "3,990", "4,190", "4,360", "4,360", "4,760", "5,760", "6,760", "7,760", "8,760" },
            new[] { "$60,000 - 69,999", "1,020", "2,220", "3,420", "3,990", "4,190", "4,360", "4,760", "5,760", "6,760", "7,760", "8,760", "9,760" },
            new[] { "$70,000 - 79,999", "1,020", "2,220", "3,420", "3,990", "4,190", "4,760", "5,760", "6,760", "7,760", "8,760", "9,760", "10,760" },
            new[] { "$80,000 - 99,999", "1,020", "2,220", "3,420", "4,240", "5,440", "6,610", "7,610", "8,610", "9,610", "10,610", "11,610", "12,610" },
            new[] { "$100,000 - 149,999", "1,870", "4,070", "6,270", "7,840", "9,040", "10,210", "11,210", "12,210", "13,210", "14,210", "15,360", "16,560" },
            new[] { "$150,000 - 239,999", "1,870", "4,100", "6,500", "8,270", "9,670", "11,040", "12,240", "13,440", "14,640", "15,840", "17,040", "18,240" },
            new[] { "$240,000 - 319,999", "2,040", "4,440", "6,840", "8,610", "10,010", "11,380", "12,580", "13,780", "14,980", "16,180", "17,380", "18,580" },
            new[] { "$320,000 - 364,999", "2,040", "4,440", "6,840", "8,610", "10,010", "11,380", "12,580", "13,860", "15,860", "17,860", "19,860", "21,860" },
            new[] { "$365,000 - 524,999", "2,720", "5,920", "9,390", "12,260", "14,760", "17,230", "19,530", "21,830", "24,130", "26,430", "28,730", "31,030" },
            new[] { "$525,000 and over", "3,140", "6,840", "10,540", "13,610", "16,310", "18,980", "21,480", "23,980", "26,480", "28,980", "31,480", "33,990" },
        };
        return BuildTaxTableHtml(rows);
    }

    private static string BuildSingleTable()
    {
        var rows = new[]
        {
            new[] { "$0 - 9,999", "$90", "$850", "$1,020", "$1,020", "$1,020", "$1,070", "$1,870", "$1,870", "$1,870", "$1,870", "$1,870", "$1,970" },
            new[] { "$10,000 - 19,999", "850", "1,780", "1,980", "1,980", "2,030", "3,030", "3,830", "3,830", "3,830", "3,830", "3,930", "4,130" },
            new[] { "$20,000 - 29,999", "1,020", "1,980", "2,180", "2,230", "3,230", "4,230", "5,030", "5,030", "5,030", "5,130", "5,330", "5,530" },
            new[] { "$30,000 - 39,999", "1,020", "1,980", "2,230", "3,230", "4,230", "5,230", "6,030", "6,030", "6,130", "6,330", "6,530", "6,730" },
            new[] { "$40,000 - 59,999", "1,020", "2,880", "4,080", "5,080", "6,080", "7,080", "7,950", "8,150", "8,350", "8,550", "8,750", "8,950" },
            new[] { "$60,000 - 79,999", "1,870", "3,830", "5,030", "6,030", "7,100", "8,300", "9,300", "9,500", "9,700", "9,900", "10,100", "10,300" },
            new[] { "$80,000 - 99,999", "1,870", "3,830", "5,100", "6,300", "7,500", "8,700", "9,700", "9,900", "10,100", "10,300", "10,500", "10,700" },
            new[] { "$100,000 - 124,999", "2,030", "4,190", "5,590", "6,790", "7,990", "9,190", "10,190", "10,390", "10,590", "10,940", "11,940", "12,940" },
            new[] { "$125,000 - 149,999", "2,040", "4,200", "5,600", "6,800", "8,000", "9,200", "10,200", "10,950", "11,950", "12,950", "13,950", "14,950" },
            new[] { "$150,000 - 174,999", "2,040", "4,200", "5,600", "6,800", "8,150", "10,150", "11,950", "12,950", "13,950", "14,950", "16,170", "17,470" },
            new[] { "$175,000 - 199,999", "2,040", "4,200", "6,150", "8,150", "10,150", "12,150", "13,950", "15,020", "16,320", "17,620", "18,920", "20,220" },
            new[] { "$200,000 - 249,999", "2,720", "5,680", "7,880", "10,140", "12,440", "14,740", "16,840", "18,140", "19,440", "20,740", "22,040", "23,340" },
            new[] { "$250,000 - 449,999", "2,970", "6,230", "8,730", "11,030", "13,330", "15,630", "17,730", "19,030", "20,330", "21,630", "22,930", "24,240" },
            new[] { "$450,000 and over", "3,140", "6,600", "9,300", "11,800", "14,300", "16,800", "19,100", "20,600", "22,100", "23,600", "25,100", "26,610" },
        };
        return BuildTaxTableHtml(rows);
    }

    private static string BuildHohTable()
    {
        var rows = new[]
        {
            new[] { "$0 - 9,999", "$0", "$280", "$850", "$950", "$1,020", "$1,020", "$1,020", "$1,020", "$1,560", "$1,870", "$1,870", "$1,870" },
            new[] { "$10,000 - 19,999", "280", "1,280", "1,950", "2,150", "2,220", "2,220", "2,220", "2,760", "3,760", "4,070", "4,070", "4,210" },
            new[] { "$20,000 - 29,999", "850", "1,950", "2,720", "2,920", "2,980", "2,980", "3,520", "4,520", "5,520", "5,830", "5,980", "6,180" },
            new[] { "$30,000 - 39,999", "950", "2,150", "2,920", "3,120", "3,180", "3,720", "4,720", "5,720", "6,720", "7,180", "7,380", "7,580" },
            new[] { "$40,000 - 59,999", "1,020", "2,220", "2,980", "3,570", "4,640", "5,640", "6,640", "7,750", "8,950", "9,460", "9,660", "9,860" },
            new[] { "$60,000 - 79,999", "1,020", "2,610", "4,370", "5,570", "6,640", "7,750", "8,950", "10,150", "11,350", "11,860", "12,060", "12,260" },
            new[] { "$80,000 - 99,999", "1,870", "4,070", "5,830", "7,150", "8,410", "9,610", "10,810", "12,010", "13,210", "13,720", "13,920", "14,120" },
            new[] { "$100,000 - 124,999", "1,870", "4,270", "6,230", "7,630", "8,900", "10,100", "11,300", "12,500", "13,700", "14,210", "14,720", "15,720" },
            new[] { "$125,000 - 149,999", "2,040", "4,440", "6,400", "7,800", "9,070", "10,270", "11,470", "12,670", "14,580", "15,890", "16,890", "17,890" },
            new[] { "$150,000 - 174,999", "2,040", "4,440", "6,400", "7,800", "9,070", "10,580", "12,580", "14,580", "16,580", "17,890", "18,890", "20,170" },
            new[] { "$175,000 - 199,999", "2,040", "4,440", "6,400", "8,510", "10,580", "12,580", "14,580", "16,580", "18,710", "20,320", "21,620", "22,920" },
            new[] { "$200,000 - 249,999", "2,720", "5,920", "8,680", "10,900", "13,270", "15,570", "17,870", "20,170", "22,470", "24,080", "25,380", "26,680" },
            new[] { "$250,000 - 449,999", "2,970", "6,470", "9,540", "12,040", "14,410", "16,710", "19,010", "21,310", "23,610", "25,220", "26,520", "27,820" },
            new[] { "$450,000 and over", "3,140", "6,840", "10,110", "12,810", "15,380", "17,880", "20,380", "22,880", "25,380", "27,190", "28,690", "30,190" },
        };
        return BuildTaxTableHtml(rows);
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

    private static Dictionary<string, object?> Field(string id, string type, string label,
        string? width = null, string? autocomplete = null)
    {
        var field = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = type,
            ["label"] = label,
        };
        if (width is not null) field["width"] = width;
        if (autocomplete is not null) field["autocomplete"] = autocomplete;
        return field;
    }

    private static Dictionary<string, object?> GridCell(string id, string type, string label,
        string gridColumn, string gridRow,
        string? autocomplete = null, string? placeholder = null, int? maxlength = null,
        bool required = false)
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
        if (required) field["required"] = true;
        return field;
    }

    private static Dictionary<string, object?> AmountLine(string id, string displayText, string amountLabel, string? prefix)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "currency",
            ["label"] = amountLabel,
            ["fieldLayout"] = "amount-line",
            ["displayText"] = displayText,
            ["amountLabel"] = amountLabel,
            ["prefix"] = prefix,
        };
    }

    private static Dictionary<string, object?> AmountLineInner(string id, string displayText, string amountLabel, string? prefix)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "currency",
            ["label"] = amountLabel,
            ["fieldLayout"] = "amount-line-inner",
            ["displayText"] = displayText,
            ["amountLabel"] = amountLabel,
            ["prefix"] = prefix,
        };
    }

    private static Dictionary<string, object?> AmountLineTotal(string id, string displayText, string amountLabel, string? prefix)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "currency",
            ["label"] = amountLabel,
            ["fieldLayout"] = "amount-line-total",
            ["displayText"] = displayText,
            ["amountLabel"] = amountLabel,
            ["prefix"] = prefix,
        };
    }

    private static Dictionary<string, object?> WorksheetLine(string id, string lineNumber, string displayText, string? prefix)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "currency",
            ["label"] = $"Line {lineNumber}",
            ["fieldLayout"] = "worksheet-line",
            ["worksheetLineNumber"] = lineNumber,
            ["displayText"] = displayText,
            ["prefix"] = prefix,
        };
    }

    private static Dictionary<string, object?> InstructionHeading(string text)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = $"heading_{text.GetHashCode():X}",
            ["type"] = "heading",
            ["label"] = text,
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
