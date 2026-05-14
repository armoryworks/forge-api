using System.Text.Json;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations.Builders;

/// <summary>
/// Hardcoded builder for USCIS Form I-9 (Employment Eligibility Verification).
/// Produces a ComplianceFormDefinition with correct labels, layout metadata,
/// and field IDs mapped from the actual PDF annotations.
/// </summary>
public class I9FormDefinitionBuilder : IFormDefinitionBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public ComplianceFormType FormType => ComplianceFormType.I9;

    public string BuilderVersion => "v6";

    public string Build(PdfExtractionResult rawResult)
    {
        var definition = new Dictionary<string, object?>
        {
            ["formType"] = "I9",
            ["builderVersion"] = BuilderVersion,
            ["title"] = "I-9",
            ["formNumber"] = "Form I-9",
            ["revision"] = "01/20/25",
            ["agency"] = "Department of Homeland Security — U.S. Citizenship and Immigration Services",
            ["formLayout"] = "government",
            ["maxWidth"] = "850px",
            ["pages"] = new List<object>
            {
                BuildPage1(rawResult),
                BuildPage2(),
                BuildPage3(rawResult),
                BuildPage4(rawResult),
            },
        };

        return JsonSerializer.Serialize(definition, JsonOptions);
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 1 — Section 1 (Employee) + Section 2 (Employer)
    // ════════════════════════════════════════════════════════════════
    private static Dictionary<string, object?> BuildPage1(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "page1",
            ["title"] = "Page 1",
            ["sections"] = new List<object>
            {
                // Form header
                BuildFormHeader(),
                // Section 1 — Employee Information
                BuildSection1EmployeeInfo(raw),
                // Section 1 — Attestation
                BuildSection1Attestation(raw),
                // Section 1 — Signature
                BuildSection1Signature(raw),
                // Preparer/Translator notice
                BuildPreparerNotice(),
                // Section 2 — Employer Review
                BuildSection2Header(),
                BuildSection2Documents(raw),
                BuildSection2Certification(raw),
                // Footer
                BuildPage1Footer(),
            },
        };
    }

    private static Dictionary<string, object?> BuildFormHeader()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section_header",
            ["title"] = "Form I-9",
            ["layout"] = "form-header",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "header_left",
                    ["type"] = "html",
                    ["label"] = "",
                    ["gridColumn"] = "left",
                    ["html"] = "<div style=\"border:2px solid #000;padding:6px 10px;text-align:center;font-size:11px;font-weight:bold;\">Form<br><span style=\"font-size:20px\">I-9</span></div>",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "header_center",
                    ["type"] = "html",
                    ["label"] = "",
                    ["gridColumn"] = "center",
                    ["html"] = "<div style=\"text-align:center;\"><strong style=\"font-size:16px\">Employment Eligibility Verification</strong><br><span style=\"font-size:12px\">Department of Homeland Security</span><br><span style=\"font-size:12px\">U.S. Citizenship and Immigration Services</span></div>",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "header_right",
                    ["type"] = "html",
                    ["label"] = "",
                    ["gridColumn"] = "right",
                    ["html"] = "<div style=\"text-align:right;font-size:10px;\"><strong>USCIS</strong><br><strong>Form I-9</strong><br>OMB No. 1615-0047<br>Expires 05/31/2027</div>",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "header_start_here",
                    ["type"] = "html",
                    ["label"] = "",
                    ["gridColumn"] = "full",
                    ["html"] = "<div style=\"background:#000;color:#fff;font-weight:bold;padding:6px 10px;font-size:11px;\"><strong>START HERE:</strong> Employers must ensure the form instructions are available to employees when completing this form. Employers are liable for failing to comply with the requirements for completing this form. See below and the Instructions.</div><div style=\"padding:6px 10px;font-size:11px;border:1px solid #ccc;margin-top:2px;\"><strong>ANTI-DISCRIMINATION NOTICE:</strong> All employees can choose which acceptable documentation to present for Form I-9. Employers cannot ask employees for documentation to verify information in <strong>Section 1</strong>, or specify which acceptable documentation employees must present for <strong>Section 2</strong> or Supplement B, Reverification and Rehire. Treating employees differently based on their citizenship, immigration status, or national origin may be illegal.</div>",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildSection1EmployeeInfo(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section1_info",
            ["title"] = "Section 1. Employee Information and Attestation",
            ["layout"] = "step",
            ["stepNumber"] = "Section 1.",
            ["stepName"] = "Employee Information\nand Attestation",
            ["instructions"] = "Employees must complete and sign Section 1 of Form I-9 no later than the first day of employment, but not before accepting a job offer.",
            ["gridColumns"] = "1fr 1fr 1fr 1fr",
            ["fields"] = new List<object>
            {
                GridCell(Ann(raw, 1, "Last Name.*Family") ?? "i9_lastName",
                    "text", "Last Name (Family Name)", "1", "1", "family-name", required: true),
                GridCell(Ann(raw, 1, "First Name.*Given") ?? "i9_firstName",
                    "text", "First Name (Given Name)", "2", "1", "given-name", required: true),
                GridCell(Ann(raw, 1, "Middle Initial") ?? "i9_middleInitial",
                    "text", "Middle Initial (if any)", "3", "1", maxlength: 1),
                GridCell(Ann(raw, 1, "Other Last Names") ?? "i9_otherLastNames",
                    "text", "Other Last Names Used (if any)", "4", "1"),
                GridCell(Ann(raw, 1, "Address.*Street") ?? "i9_address",
                    "text", "Address (Street Number and Name)", "1", "2", "street-address", required: true),
                GridCell(Ann(raw, 1, "Apt.*Number") ?? "i9_aptNumber",
                    "text", "Apt. Number (if any)", "2", "2"),
                GridCell(Ann(raw, 1, "City or Town") ?? "i9_city",
                    "text", "City or Town", "3", "2", "address-level2", required: true),
                GridCell(Ann(raw, 1, "^State$") ?? "i9_state",
                    "text", "State", "4 / span 1", "2", "address-level1", maxlength: 2, required: true),
                GridCell(Ann(raw, 1, "ZIP Code") ?? "i9_zipCode",
                    "text", "ZIP Code", "5", "2", "postal-code", maxlength: 10, required: true, mask: "zip"),
                GridCell(Ann(raw, 1, "Date of Birth") ?? "i9_dateOfBirth",
                    "text", "Date of Birth (mm/dd/yyyy)", "1", "3", placeholder: "MM/DD/YYYY", maxlength: 10, required: true, mask: "date"),
                GridCell(Ann(raw, 1, "Social Security") ?? "i9_ssn",
                    "ssn", "U.S. Social Security Number", "2", "3", placeholder: "XXX-XX-XXXX", maxlength: 11, mask: "ssn"),
                GridCell(Ann(raw, 1, "E-?mail") ?? "i9_email",
                    "text", "Employee's Email Address", "3", "3", "email"),
                GridCell(Ann(raw, 1, "Telephone") ?? "i9_telephone",
                    "text", "Employee's Telephone Number", "4", "3", "tel", mask: "phone"),
            },
        };
    }

    private static Dictionary<string, object?> BuildSection1Attestation(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section1_attestation",
            ["title"] = "Citizenship / Immigration Status",
            ["layout"] = "step",
            ["shaded"] = true,
            ["instructions"] = "I am aware that federal law provides for imprisonment and/or fines for false statements, or the use of false documents, in connection with the completion of this form. I attest, under penalty of perjury, that this information, including my selection of the box attesting to my citizenship or immigration status, is true and correct.",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "CB_1") ?? "i9_citizen",
                    ["type"] = "radio",
                    ["label"] = "Citizenship or Immigration Status",
                    ["required"] = true,
                    ["fieldLayout"] = "filing-status",
                    ["options"] = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["value"] = "citizen",
                            ["label"] = "1. A citizen of the United States",
                        },
                        new Dictionary<string, object?>
                        {
                            ["value"] = "noncitizen_national",
                            ["label"] = "2. A noncitizen national of the United States (See Instructions.)",
                        },
                        new Dictionary<string, object?>
                        {
                            ["value"] = "lawful_permanent_resident",
                            ["label"] = "3. A lawful permanent resident",
                            ["hint"] = "Enter USCIS or A-Number",
                        },
                        new Dictionary<string, object?>
                        {
                            ["value"] = "authorized_alien",
                            ["label"] = "4. An alien authorized to work",
                            ["hint"] = "Enter expiration date, if any",
                        },
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "lawful permanent.*USCIS") ?? "i9_uscisNumber",
                    ["type"] = "text",
                    ["label"] = "USCIS or A-Number (if #3)",
                    ["width"] = "half",
                    ["dependsOn"] = new Dictionary<string, object?>
                    {
                        ["field"] = Ann(raw, 1, "CB_1") ?? "i9_citizen",
                        ["value"] = "lawful_permanent_resident",
                        ["operator"] = "eq",
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Exp Date") ?? "i9_expDate",
                    ["type"] = "text",
                    ["label"] = "Expiration Date (mm/dd/yyyy) (if #4)",
                    ["width"] = "half",
                    ["mask"] = "date",
                    ["maxlength"] = 10,
                    ["placeholder"] = "MM/DD/YYYY",
                    ["dependsOn"] = new Dictionary<string, object?>
                    {
                        ["field"] = Ann(raw, 1, "CB_1") ?? "i9_citizen",
                        ["value"] = "authorized_alien",
                        ["operator"] = "eq",
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "USCIS ANumber") ?? "i9_alienUscis",
                    ["type"] = "text",
                    ["label"] = "USCIS A-Number (if #4)",
                    ["width"] = "third",
                    ["dependsOn"] = new Dictionary<string, object?>
                    {
                        ["field"] = Ann(raw, 1, "CB_1") ?? "i9_citizen",
                        ["value"] = "authorized_alien",
                        ["operator"] = "eq",
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Form I94") ?? "i9_i94Number",
                    ["type"] = "text",
                    ["label"] = "Form I-94 Admission Number (if #4)",
                    ["width"] = "third",
                    ["dependsOn"] = new Dictionary<string, object?>
                    {
                        ["field"] = Ann(raw, 1, "CB_1") ?? "i9_citizen",
                        ["value"] = "authorized_alien",
                        ["operator"] = "eq",
                    },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Foreign Passport") ?? "i9_foreignPassport",
                    ["type"] = "text",
                    ["label"] = "Foreign Passport Number and Country of Issuance (if #4)",
                    ["width"] = "third",
                    ["dependsOn"] = new Dictionary<string, object?>
                    {
                        ["field"] = Ann(raw, 1, "CB_1") ?? "i9_citizen",
                        ["value"] = "authorized_alien",
                        ["operator"] = "eq",
                    },
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildSection1Signature(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section1_signature",
            ["title"] = "Employee Signature",
            ["layout"] = "sign",
            ["heavyBorder"] = true,
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Signature of Employee") ?? "i9_employeeSignature",
                    ["type"] = "signature",
                    ["label"] = "Signature of Employee",
                    ["required"] = true,
                    ["fieldLayout"] = "signature-field",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Today.*Date.*mmdd") ?? "i9_employeeSignDate",
                    ["type"] = "text",
                    ["label"] = "Today's Date (mm/dd/yyyy)",
                    ["required"] = true,
                    ["fieldLayout"] = "signature-date",
                    ["mask"] = "date",
                    ["maxlength"] = 10,
                    ["placeholder"] = "MM/DD/YYYY",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildPreparerNotice()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section1_preparer_notice",
            ["title"] = "",
            ["layout"] = "notice",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "preparer_notice_html",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<div style=\"border:1px solid #ccc;padding:8px;font-size:11px;\"><strong>Preparer and/or Translator Certification (check one):</strong><br>If a preparer or translator assisted the employee in completing Section 1, the preparer/translator must complete <em>Supplement A, Preparer and/or Translator Certification for Section 1,</em> on the next page of this form.</div>",
                    ["fieldLayout"] = "full-width",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildSection2Header()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section2_header",
            ["title"] = "Section 2. Employer Review and Verification",
            ["layout"] = "step",
            ["stepNumber"] = "Section 2.",
            ["stepName"] = "Employer Review\nand Verification",
            ["instructions"] = "Employers or their authorized representative must complete and sign Section 2 within three business days after the employee's first day of employment, and must physically examine, or examine consistent with an alternative procedure authorized by the Secretary of DHS, documentation from List A OR a combination of documentation from List B and List C.",
            ["fields"] = new List<object>(),
        };
    }

    private static Dictionary<string, object?> BuildSection2Documents(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section2_documents",
            ["title"] = "Identity and Employment Authorization Documents",
            ["layout"] = "step",
            ["shaded"] = true,
            ["gridColumns"] = "auto 1fr auto 1fr auto 1fr",
            ["instructions"] = "Enter document information from List A OR a combination of List B AND List C.",
            ["fields"] = new List<object>
            {
                // Top-left empty cell (label column header)
                new Dictionary<string, object?>
                {
                    ["id"] = "row_label_header",
                    ["type"] = "html",
                    ["html"] = "",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "1",
                    ["gridRow"] = "1",
                },

                // Column headers (cols 2-6)
                new Dictionary<string, object?>
                {
                    ["id"] = "listA_header",
                    ["type"] = "heading",
                    ["label"] = "List A",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "2",
                    ["gridRow"] = "1",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "or_separator_1",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<strong>OR</strong>",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "3",
                    ["gridRow"] = "1",
                    ["style"] = new Dictionary<string, string> { ["text-align"] = "center", ["padding"] = "0 8px" },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "listB_header",
                    ["type"] = "heading",
                    ["label"] = "List B",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "4",
                    ["gridRow"] = "1",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "and_separator",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "<strong>AND</strong>",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "5",
                    ["gridRow"] = "1",
                    ["style"] = new Dictionary<string, string> { ["text-align"] = "center", ["padding"] = "0 8px" },
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "listC_header",
                    ["type"] = "heading",
                    ["label"] = "List C",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "6",
                    ["gridRow"] = "1",
                },

                // Row labels (col 1, rows 2-13)
                RowLabel("Document Title", "2"),
                RowLabel("Issuing Authority", "3"),
                RowLabel("Document Number (if any)", "4"),
                RowLabel("Expiration Date (if any)", "5"),
                RowLabel("Document Title 2 (if any)", "6"),
                RowLabel("Issuing Authority", "7"),
                RowLabel("Document Number (if any)", "8"),
                RowLabel("Expiration Date (if any)", "9"),
                RowLabel("Document Title 3 (if any)", "10"),
                RowLabel("Issuing Authority", "11"),
                RowLabel("Document Number (if any)", "12"),
                RowLabel("Expiration Date (if any)", "13"),

                // List A - Document 1 (col 2, no inline labels)
                GridCell(Ann(raw, 1, "^Document Title 1$") ?? "i9_listA_docTitle1",
                    "text", "Document Title 1", "2", "2", noLabel: true),
                GridCell(Ann(raw, 1, "^Issuing Authority 1$") ?? "i9_listA_issuingAuth1",
                    "text", "Issuing Authority", "2", "3", noLabel: true),
                GridCell(Ann(raw, 1, "Document Number 0") ?? "i9_listA_docNumber1",
                    "text", "Document Number (if any)", "2", "4", noLabel: true),
                GridCell(Ann(raw, 1, "^Expiration Date if any$") ?? "i9_listA_expDate1",
                    "text", "Expiration Date (if any)", "2", "5", noLabel: true),

                // List A - Document 2 (col 2)
                GridCell(Ann(raw, 1, "Document Title 2 If any") ?? "i9_listA_docTitle2",
                    "text", "Document Title 2 (if any)", "2", "6", noLabel: true),
                GridCell(Ann(raw, 1, "Issuing Authority_2") ?? "i9_listA_issuingAuth2",
                    "text", "Issuing Authority", "2", "7", noLabel: true),
                GridCell(Ann(raw, 1, "Document Number If any_2") ?? "i9_listA_docNumber2",
                    "text", "Document Number (if any)", "2", "8", noLabel: true),
                GridCell(Ann(raw, 1, "Document 2.*Expiration") ?? "i9_listA_expDate2",
                    "text", "Expiration Date (if any)", "2", "9", noLabel: true),

                // List A - Document 3 (col 2)
                GridCell(Ann(raw, 1, "Document Title 3") ?? "i9_listA_docTitle3",
                    "text", "Document Title 3 (if any)", "2", "10", noLabel: true),
                GridCell(Ann(raw, 1, "Document 3.*Issuing") ?? "i9_listA_issuingAuth3",
                    "text", "Issuing Authority", "2", "11", noLabel: true),
                GridCell(Ann(raw, 1, "Document 3 Number") ?? "i9_listA_docNumber3",
                    "text", "Document Number (if any)", "2", "12", noLabel: true),
                GridCell(Ann(raw, 1, "Document Number if any_3") ?? "i9_listA_expDate3",
                    "text", "Expiration Date (if any)", "2", "13", noLabel: true),

                // List B - Document 1 (col 4, no inline labels)
                GridCell(Ann(raw, 1, "List B Document 1 Title") ?? "i9_listB_docTitle",
                    "text", "Document Title", "4", "2", noLabel: true),
                GridCell(Ann(raw, 1, "List B Issuing Authority") ?? "i9_listB_issuingAuth",
                    "text", "Issuing Authority", "4", "3", noLabel: true),
                GridCell(Ann(raw, 1, "List B Document Number") ?? "i9_listB_docNumber",
                    "text", "Document Number", "4", "4", noLabel: true),
                GridCell(Ann(raw, 1, "List B Expiration") ?? "i9_listB_expDate",
                    "text", "Expiration Date (if any)", "4", "5", noLabel: true),

                // List C - Document 1 (col 6, no inline labels)
                GridCell(Ann(raw, 1, "List C Document Title") ?? "i9_listC_docTitle",
                    "text", "Document Title", "6", "2", noLabel: true),
                GridCell(Ann(raw, 1, "List C Issuing Authority") ?? "i9_listC_issuingAuth",
                    "text", "Issuing Authority", "6", "3", noLabel: true),
                GridCell(Ann(raw, 1, "List C Document Number") ?? "i9_listC_docNumber",
                    "text", "Document Number", "6", "4", noLabel: true),
                GridCell(Ann(raw, 1, "List C Expiration") ?? "i9_listC_expDate",
                    "text", "Expiration Date (if any)", "6", "5", noLabel: true),

                // Additional Information (cols 4-6, rows 6-9)
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Additional Information") ?? "i9_additionalInfo",
                    ["type"] = "textarea",
                    ["label"] = "Additional Information",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "4 / span 3",
                    ["gridRow"] = "6 / span 4",
                    ["rows"] = 5,
                },

                // Alternative procedure checkbox (cols 4-6, row 10)
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "CB_Alt$") ?? "i9_altProcedure",
                    ["type"] = "checkbox",
                    ["label"] = "Check here if you used an alternative procedure authorized by DHS to examine documents.",
                    ["fieldLayout"] = "grid-cell",
                    ["gridColumn"] = "4 / span 3",
                    ["gridRow"] = "10",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildSection2Certification(PdfExtractionResult raw)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "section2_certification",
            ["title"] = "Employer Certification",
            ["layout"] = "sign",
            ["heavyBorder"] = true,
            ["instructions"] = "Certification: I attest, under penalty of perjury, that (1) I have examined the documentation presented by the above-named employee, (2) the above-listed documentation appears to be genuine and to relate to the employee named, and (3) to the best of my knowledge, the employee is authorized to work in the United States.",
            ["gridColumns"] = "1fr 1fr 1fr",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "FirstDayEmployed") ?? "i9_firstDayEmployed",
                    ["type"] = "text",
                    ["label"] = "First Day of Employment (mm/dd/yyyy)",
                    ["width"] = "third",
                    ["mask"] = "date",
                    ["maxlength"] = 10,
                    ["placeholder"] = "MM/DD/YYYY",
                },
                GridCell(Ann(raw, 1, "Last Name.*Title.*Employer") ?? "i9_employerName",
                    "text", "Last Name, First Name and Title of Employer or Authorized Representative", "1", "1"),
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Signature of Employer") ?? "i9_employerSignature",
                    ["type"] = "signature",
                    ["label"] = "Signature of Employer or Authorized Representative",
                    ["fieldLayout"] = "signature-field",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "S2 Todays Date") ?? "i9_employerSignDate",
                    ["type"] = "text",
                    ["label"] = "Today's Date (mm/dd/yyyy)",
                    ["fieldLayout"] = "signature-date",
                    ["mask"] = "date",
                    ["maxlength"] = 10,
                    ["placeholder"] = "MM/DD/YYYY",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Employers Business.*Org Name") ?? "i9_employerOrgName",
                    ["type"] = "text",
                    ["label"] = "Employer's Business or Organization Name",
                    ["width"] = "half",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 1, "Employers Business.*Org Address") ?? "i9_employerOrgAddress",
                    ["type"] = "text",
                    ["label"] = "Employer's Business or Organization Address, City or Town, State, ZIP Code",
                    ["width"] = "half",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildPage1Footer()
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
                    ["id"] = "footer_html",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = "Form I-9 Edition 01/20/25 &nbsp;&nbsp; Page 1 of 4",
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 2 — Lists of Acceptable Documents (read-only reference)
    // ════════════════════════════════════════════════════════════════
    private static Dictionary<string, object?> BuildPage2()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "page2",
            ["title"] = "Acceptable Documents",
            ["readonly"] = true,
            ["sections"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "lists_instructions",
                    ["title"] = "LISTS OF ACCEPTABLE DOCUMENTS",
                    ["layout"] = "instructions",
                    ["fields"] = new List<object>
                    {
                        InstructionParagraph("All documents containing an expiration date must be unexpired. Documents extended by the issuing authority are considered unexpired. Employees may present one selection from List A or a combination of one selection from List B and one selection from List C."),
                        InstructionHeading("List A — Documents that Establish Both Identity and Employment Authorization"),
                        InstructionParagraph("1. U.S. Passport or U.S. Passport Card\n2. Permanent Resident Card or Alien Registration Receipt Card (Form I-551)\n3. Foreign passport that contains a temporary I-551 stamp or temporary I-551 printed notation on a machine-readable immigrant visa\n4. Employment Authorization Document that contains a photograph (Form I-766)\n5. For an individual temporarily authorized to work for a specific employer because of his or her status or parole: a. Foreign passport, and b. Form I-94 or Form I-94A that has the following: (1) The same name as the passport, and (2) An endorsement of the individual's status or parole as long as the period of endorsement has not yet expired and the proposed employment is not in conflict with any restrictions or limitations identified on the form\n6. Passport from the Federated States of Micronesia (FSM) or the Republic of the Marshall Islands (RMI) with Form I-94 or Form I-94A indicating nonimmigrant admission under the Compact of Free Association Between the United States and the FSM or RMI"),
                        InstructionHeading("List B — Documents that Establish Identity"),
                        InstructionParagraph("1. Driver's license or ID card issued by a State or outlying territory of the United States, provided it contains a photograph or information such as name, date of birth, gender, height, eye color, and address\n2. ID card issued by federal, state, or local government agencies or entities, provided it contains a photograph or information such as name, date of birth, gender, height, eye color, and address\n3. School ID card with a photograph\n4. Voter's registration card\n5. U.S. Military card or draft record\n6. Military dependent's ID card\n7. U.S. Coast Guard Merchant Mariner Card\n8. Native American tribal document\n9. Driver's license issued by a Canadian government authority"),
                        InstructionParagraph("For persons under age 18 who are unable to present a document listed above:\n10. School record or report card\n11. Clinic, doctor, or hospital record\n12. Day-care or nursery school record"),
                        InstructionHeading("List C — Documents that Establish Employment Authorization"),
                        InstructionParagraph("1. A Social Security Account Number card, unless the card includes one of the following restrictions:\n   (1) NOT VALID FOR EMPLOYMENT\n   (2) VALID FOR WORK ONLY WITH INS AUTHORIZATION\n   (3) VALID FOR WORK ONLY WITH DHS AUTHORIZATION\n2. Certification of report of birth issued by the Department of State (Forms DS-1350, FS-545, FS-240)\n3. Original or certified copy of birth certificate issued by a State, county, municipal authority, or territory of the United States bearing an official seal\n4. Native American tribal document\n5. U.S. Citizen ID Card (Form I-197)\n6. Identification Card for Use of Resident Citizen in the United States (Form I-179)\n7. Employment authorization document issued by the Department of Homeland Security"),
                        InstructionHeading("Acceptable Receipts"),
                        InstructionParagraph("May be presented in lieu of a document listed above for a temporary period. For receipt validity dates, see the Handbook for Employers (M-274)."),
                    },
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 3 — Supplement A: Preparer/Translator Certification
    // ════════════════════════════════════════════════════════════════
    private static Dictionary<string, object?> BuildPage3(PdfExtractionResult raw)
    {
        var sections = new List<object>
        {
            // Header
            new Dictionary<string, object?>
            {
                ["id"] = "suppA_header",
                ["title"] = "Supplement A",
                ["layout"] = "form-header",
                ["fields"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "suppA_header_left",
                        ["type"] = "html",
                        ["label"] = "",
                        ["html"] = "USCIS<br>Form I-9<br>Supplement A",
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "suppA_header_center",
                        ["type"] = "html",
                        ["label"] = "",
                        ["html"] = "<strong style=\"font-size:14px\">Supplement A,</strong><br><strong>Preparer and/or Translator Certification for Section 1</strong><br>Department of Homeland Security<br>U.S. Citizenship and Immigration Services",
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "suppA_header_right",
                        ["type"] = "html",
                        ["label"] = "",
                        ["html"] = "",
                    },
                },
            },

            // Employee info from Section 1
            new Dictionary<string, object?>
            {
                ["id"] = "suppA_employee_info",
                ["title"] = "Employee Information from Section 1",
                ["layout"] = "step",
                ["gridColumns"] = "1fr 1fr 1fr",
                ["fields"] = new List<object>
                {
                    GridCell(Ann(raw, 3, "Last Name.*Section 1$") ?? "i9_suppA_lastName",
                        "text", "Last Name (Family Name) from Section 1", "1", "1"),
                    GridCell(Ann(raw, 3, "First Name.*Section 1$") ?? "i9_suppA_firstName",
                        "text", "First Name (Given Name) from Section 1", "2", "1"),
                    GridCell(Ann(raw, 3, "Middle initial.*Section 1$") ?? "i9_suppA_middleInitial",
                        "text", "Middle Initial (if any) from Section 1", "3", "1"),
                },
            },
        };

        // 4 preparer/translator slots
        for (var i = 0; i < 4; i++)
        {
            sections.Add(BuildPreparerSlot(raw, i));
        }

        return new Dictionary<string, object?>
        {
            ["id"] = "page3",
            ["title"] = "Supplement A",
            ["sections"] = sections,
        };
    }

    private static Dictionary<string, object?> BuildPreparerSlot(PdfExtractionResult raw, int index)
    {
        var suffix = index.ToString();
        return new Dictionary<string, object?>
        {
            ["id"] = $"suppA_preparer_{index}",
            ["title"] = $"Preparer and/or Translator Certification #{index + 1}",
            ["layout"] = "step",
            ["shaded"] = index % 2 == 0,
            ["instructions"] = "I attest, under penalty of perjury, that I have assisted in the completion of Section 1 of this form and that to the best of my knowledge the information is true and correct.",
            ["gridColumns"] = "1fr 1fr 1fr",
            ["fields"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 3, $"Signature of Preparer.*{suffix}$") ?? $"i9_suppA_sig_{index}",
                    ["type"] = "signature",
                    ["label"] = "Signature of Preparer or Translator",
                    ["fieldLayout"] = "signature-field",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 3, $"Sig Date.*{suffix}$") ?? $"i9_suppA_sigDate_{index}",
                    ["type"] = "text",
                    ["label"] = "Date (mm/dd/yyyy)",
                    ["fieldLayout"] = "signature-date",
                    ["mask"] = "date",
                    ["maxlength"] = 10,
                    ["placeholder"] = "MM/DD/YYYY",
                },
                GridCell(Ann(raw, 3, $"Preparer or Translator Last Name.*{suffix}$") ?? $"i9_suppA_ptLastName_{index}",
                    "text", "Last Name (Family Name)", "1", "1"),
                GridCell(Ann(raw, 3, $"Preparer or Translator First Name.*{suffix}$") ?? $"i9_suppA_ptFirstName_{index}",
                    "text", "First Name (Given Name)", "2", "1"),
                GridCell(Ann(raw, 3, $"PT Middle Initial {suffix}$") ?? $"i9_suppA_ptMiddle_{index}",
                    "text", "Middle Initial (if any)", "3", "1"),
                GridCell(Ann(raw, 3, $"Address.*Street.*{suffix}$") ?? $"i9_suppA_ptAddress_{index}",
                    "text", "Address (Street Number and Name)", "1", "2"),
                GridCell(Ann(raw, 3, $"City or Town {suffix}$") ?? $"i9_suppA_ptCity_{index}",
                    "text", "City or Town", "2", "2"),
                GridCell(Ann(raw, 3, $"Preparer State {suffix}$") ?? $"i9_suppA_ptState_{index}",
                    "select", "State", "3", "2"),
                GridCell(Ann(raw, 3, $"Zip Code {suffix}$") ?? $"i9_suppA_ptZip_{index}",
                    "text", "ZIP Code", "4", "2"),
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PAGE 4 — Supplement B: Reverification and Rehire
    // ════════════════════════════════════════════════════════════════
    private static Dictionary<string, object?> BuildPage4(PdfExtractionResult raw)
    {
        var sections = new List<object>
        {
            // Header
            new Dictionary<string, object?>
            {
                ["id"] = "suppB_header",
                ["title"] = "Supplement B",
                ["layout"] = "form-header",
                ["fields"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "suppB_header_left",
                        ["type"] = "html",
                        ["label"] = "",
                        ["html"] = "USCIS<br>Form I-9<br>Supplement B",
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "suppB_header_center",
                        ["type"] = "html",
                        ["label"] = "",
                        ["html"] = "<strong style=\"font-size:14px\">Supplement B,</strong><br><strong>Reverification and Rehire (formerly Section 3)</strong><br>Department of Homeland Security<br>U.S. Citizenship and Immigration Services",
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "suppB_header_right",
                        ["type"] = "html",
                        ["label"] = "",
                        ["html"] = "",
                    },
                },
            },

            // Employee info from Section 1
            new Dictionary<string, object?>
            {
                ["id"] = "suppB_employee_info",
                ["title"] = "Employee Information from Section 1",
                ["layout"] = "step",
                ["gridColumns"] = "1fr 1fr 1fr",
                ["instructions"] = "This supplement replaces Section 3 on the previous version of Form I-9. Only use this page if your employee requires reverification, is rehired within three years of the date the original Form I-9 was completed, or provides proof of a legal name change.",
                ["fields"] = new List<object>
                {
                    GridCell(Ann(raw, 4, "Last Name.*Section 1-2$") ?? "i9_suppB_lastName",
                        "text", "Last Name (Family Name) from Section 1", "1", "1"),
                    GridCell(Ann(raw, 4, "First Name.*Section 1-2$") ?? "i9_suppB_firstName",
                        "text", "First Name (Given Name) from Section 1", "2", "1"),
                    GridCell(Ann(raw, 4, "Middle initial.*Section 1-2$") ?? "i9_suppB_middleInitial",
                        "text", "Middle Initial (if any) from Section 1", "3", "1"),
                },
            },
        };

        // 3 reverification/rehire slots
        for (var i = 0; i < 3; i++)
        {
            sections.Add(BuildReverificationSlot(raw, i));
        }

        return new Dictionary<string, object?>
        {
            ["id"] = "page4",
            ["title"] = "Supplement B",
            ["sections"] = sections,
        };
    }

    private static Dictionary<string, object?> BuildReverificationSlot(PdfExtractionResult raw, int index)
    {
        var suffix = index.ToString();
        return new Dictionary<string, object?>
        {
            ["id"] = $"suppB_reverify_{index}",
            ["title"] = $"Reverification / Rehire #{index + 1}",
            ["layout"] = "step",
            ["shaded"] = index % 2 == 0,
            ["gridColumns"] = "1fr 1fr 1fr 1fr",
            ["fields"] = new List<object>
            {
                GridCell(Ann(raw, 4, $"Date of Rehire {suffix}$") ?? $"i9_suppB_rehireDate_{index}",
                    "text", "Date of Rehire (mm/dd/yyyy) (if applicable)", "1", "1"),
                GridCell(Ann(raw, 4, $"^Last Name {suffix}$") ?? $"i9_suppB_newLastName_{index}",
                    "text", "New Last Name (Family Name) (if applicable)", "2", "1"),
                GridCell(Ann(raw, 4, $"^First Name {suffix}$") ?? $"i9_suppB_newFirstName_{index}",
                    "text", "New First Name (Given Name) (if applicable)", "3", "1"),
                GridCell(Ann(raw, 4, $"^Middle Initial {suffix}$") ?? $"i9_suppB_newMiddle_{index}",
                    "text", "Middle Initial (if applicable)", "4", "1"),
                GridCell(Ann(raw, 4, $"^Document Title {suffix}$") ?? $"i9_suppB_docTitle_{index}",
                    "text", "Document Title", "1", "2"),
                GridCell(Ann(raw, 4, $"^Document Number {suffix}$") ?? $"i9_suppB_docNumber_{index}",
                    "text", "Document Number (if any)", "2 / span 2", "2"),
                GridCell(Ann(raw, 4, $"Expiration Date {suffix}$") ?? $"i9_suppB_expDate_{index}",
                    "text", "Expiration Date (if any) (mm/dd/yyyy)", "4", "2"),
                GridCell(Ann(raw, 4, $"Name of Emp.*{suffix}$") ?? $"i9_suppB_empName_{index}",
                    "text", "Name of Employer or Authorized Representative", "1 / span 2", "3"),
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 4, $"Signature of Emp Rep {suffix}$") ?? $"i9_suppB_empSig_{index}",
                    ["type"] = "signature",
                    ["label"] = "Signature of Employer or Authorized Representative",
                    ["fieldLayout"] = "signature-field",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 4, $"Todays Date {suffix}$") ?? $"i9_suppB_empDate_{index}",
                    ["type"] = "text",
                    ["label"] = "Today's Date (mm/dd/yyyy)",
                    ["fieldLayout"] = "signature-date",
                    ["mask"] = "date",
                    ["maxlength"] = 10,
                    ["placeholder"] = "MM/DD/YYYY",
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 4, $"Addtl Info {suffix}$") ?? $"i9_suppB_additionalInfo_{index}",
                    ["type"] = "textarea",
                    ["label"] = "Additional Information (Initial and date each notation.)",
                    ["width"] = "full",
                    ["rows"] = 3,
                },
                new Dictionary<string, object?>
                {
                    ["id"] = Ann(raw, 4, $"CB_Alt_{suffix}$") ?? $"i9_suppB_altProcedure_{index}",
                    ["type"] = "checkbox",
                    ["label"] = "Check here if you used an alternative procedure authorized by DHS to examine documents.",
                },
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════

    private static string? Ann(PdfExtractionResult raw, int page, string pattern) =>
        AnnotationMapper.FindByName(raw, page, pattern);

    private static Dictionary<string, object?> GridCell(
        string id, string type, string label,
        string gridColumn, string gridRow,
        string? autocomplete = null, string? placeholder = null, int? maxlength = null,
        bool? required = null, string? mask = null, bool noLabel = false)
    {
        var cell = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = type,
            ["label"] = label,
            ["fieldLayout"] = "grid-cell",
            ["gridColumn"] = gridColumn,
            ["gridRow"] = gridRow,
            ["autocomplete"] = autocomplete,
            ["placeholder"] = placeholder,
            ["maxlength"] = maxlength,
            ["required"] = required,
            ["mask"] = mask,
        };
        if (noLabel) cell["noLabel"] = true;
        return cell;
    }

    private static Dictionary<string, object?> RowLabel(string label, string gridRow) =>
        new Dictionary<string, object?>
        {
            ["id"] = $"row_label_{gridRow}",
            ["type"] = "html",
            ["html"] = label,
            ["fieldLayout"] = "grid-cell",
            ["gridColumn"] = "1",
            ["gridRow"] = gridRow,
            ["cssClass"] = "gov-form__grid-row-label",
        };

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
