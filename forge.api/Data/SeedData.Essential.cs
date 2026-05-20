using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    public static async Task SeedEssentialDataAsync(AppDbContext db)
    {
        // Track Types & Stages
        if (!await db.TrackTypes.AnyAsync())
        {
            var production = new TrackType
            {
                Name = "Production",
                Code = "production",
                IsDefault = true,
                SortOrder = 1,
            };
            db.TrackTypes.Add(production);
            await db.SaveChangesAsync();

            db.JobStages.AddRange(
                new JobStage { TrackTypeId = production.Id, Name = "Quote Requested", Code = "quote_requested", SortOrder = 1, Color = "#94a3b8" },
                new JobStage { TrackTypeId = production.Id, Name = "Quoted", Code = "quoted", SortOrder = 2, Color = "#0d9488", AccountingDocumentType = AccountingDocumentType.Estimate },
                new JobStage { TrackTypeId = production.Id, Name = "Order Confirmed", Code = "order_confirmed", SortOrder = 3, Color = "#0ea5e9", AccountingDocumentType = AccountingDocumentType.SalesOrder },
                new JobStage { TrackTypeId = production.Id, Name = "Materials Ordered", Code = "materials_ordered", SortOrder = 4, Color = "#8b5cf6", AccountingDocumentType = AccountingDocumentType.PurchaseOrder, IsShopFloor = true },
                new JobStage { TrackTypeId = production.Id, Name = "Materials Received", Code = "materials_received", SortOrder = 5, Color = "#a855f7", IsShopFloor = true },
                new JobStage { TrackTypeId = production.Id, Name = "In Production", Code = "in_production", SortOrder = 6, Color = "#f59e0b", IsShopFloor = true },
                new JobStage { TrackTypeId = production.Id, Name = "QC/Review", Code = "qc_review", SortOrder = 7, Color = "#ec4899", IsShopFloor = true },
                new JobStage { TrackTypeId = production.Id, Name = "Shipped", Code = "shipped", SortOrder = 8, Color = "#c2410c", AccountingDocumentType = AccountingDocumentType.Invoice, IsShopFloor = true },
                new JobStage { TrackTypeId = production.Id, Name = "Invoiced/Sent", Code = "invoiced_sent", SortOrder = 9, Color = "#dc2626", AccountingDocumentType = AccountingDocumentType.Invoice, IsIrreversible = true },
                new JobStage { TrackTypeId = production.Id, Name = "Payment Received", Code = "payment_received", SortOrder = 10, Color = "#15803d", AccountingDocumentType = AccountingDocumentType.Payment, IsIrreversible = true }
            );
            await db.SaveChangesAsync();

            var rnd = new TrackType { Name = "R&D/Tooling", Code = "rnd", SortOrder = 2, IsShopFloor = false };
            db.TrackTypes.Add(rnd);
            await db.SaveChangesAsync();

            db.JobStages.AddRange(
                new JobStage { TrackTypeId = rnd.Id, Name = "Concept", Code = "concept", SortOrder = 1, Color = "#94a3b8" },
                new JobStage { TrackTypeId = rnd.Id, Name = "Design", Code = "design", SortOrder = 2, Color = "#0d9488" },
                new JobStage { TrackTypeId = rnd.Id, Name = "Prototype", Code = "prototype", SortOrder = 3, Color = "#0ea5e9", IsShopFloor = true },
                new JobStage { TrackTypeId = rnd.Id, Name = "Test", Code = "test", SortOrder = 4, Color = "#f59e0b", IsShopFloor = true },
                new JobStage { TrackTypeId = rnd.Id, Name = "Iterate", Code = "iterate", SortOrder = 5, Color = "#ec4899", IsShopFloor = true },
                new JobStage { TrackTypeId = rnd.Id, Name = "Production Ready", Code = "production_ready", SortOrder = 6, Color = "#15803d" }
            );
            await db.SaveChangesAsync();

            var maintenance = new TrackType { Name = "Maintenance", Code = "maintenance", SortOrder = 3 };
            db.TrackTypes.Add(maintenance);
            await db.SaveChangesAsync();

            db.JobStages.AddRange(
                new JobStage { TrackTypeId = maintenance.Id, Name = "Requested", Code = "requested", SortOrder = 1, Color = "#94a3b8" },
                new JobStage { TrackTypeId = maintenance.Id, Name = "Scheduled", Code = "scheduled", SortOrder = 2, Color = "#0ea5e9", IsShopFloor = true },
                new JobStage { TrackTypeId = maintenance.Id, Name = "In Progress", Code = "in_progress", SortOrder = 3, Color = "#f59e0b", IsShopFloor = true },
                new JobStage { TrackTypeId = maintenance.Id, Name = "Complete", Code = "complete", SortOrder = 4, Color = "#15803d", IsShopFloor = true }
            );
            await db.SaveChangesAsync();

            Log.Information("Seeded track types and stages");
        }

        // Reference Data
        if (!await db.ReferenceData.AnyAsync())
        {
            db.ReferenceData.AddRange(
                new ReferenceData { IsSeedData = true, GroupCode = "job_priority", Code = "low", Label = "Low", SortOrder = 1, Metadata = """{"color":"#94a3b8"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "job_priority", Code = "normal", Label = "Normal", SortOrder = 2, Metadata = """{"color":"#0d9488"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "job_priority", Code = "high", Label = "High", SortOrder = 3, Metadata = """{"color":"#f59e0b"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "job_priority", Code = "urgent", Label = "Urgent", SortOrder = 4, Metadata = """{"color":"#dc2626"}""" },

                new ReferenceData { IsSeedData = true, GroupCode = "contact_role", Code = "primary", Label = "Primary", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "contact_role", Code = "billing", Label = "Billing", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "contact_role", Code = "technical", Label = "Technical", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "contact_role", Code = "shipping", Label = "Shipping", SortOrder = 4 },
                new ReferenceData { IsSeedData = true, GroupCode = "contact_role", Code = "owner", Label = "Owner", SortOrder = 5 },
                new ReferenceData { IsSeedData = true, GroupCode = "contact_role", Code = "manager", Label = "Manager", SortOrder = 6 },
                new ReferenceData { IsSeedData = true, GroupCode = "contact_role", Code = "engineer", Label = "Engineer", SortOrder = 7 },
                new ReferenceData { IsSeedData = true, GroupCode = "contact_role", Code = "procurement", Label = "Procurement", SortOrder = 8 },

                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "materials", Label = "Materials", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "tools", Label = "Tools", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "travel", Label = "Travel", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "fuel", Label = "Fuel", SortOrder = 4 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "meals", Label = "Meals", SortOrder = 5 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "shipping", Label = "Shipping", SortOrder = 6 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "office_supplies", Label = "Office Supplies", SortOrder = 7 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "equipment", Label = "Equipment", SortOrder = 8 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "maintenance", Label = "Maintenance", SortOrder = 9 },
                new ReferenceData { IsSeedData = true, GroupCode = "expense_category", Code = "other", Label = "Other", SortOrder = 10 },

                new ReferenceData { IsSeedData = true, GroupCode = "return_reason", Code = "defective", Label = "Defective", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "return_reason", Code = "wrong_part", Label = "Wrong Part", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "return_reason", Code = "damaged_in_shipping", Label = "Damaged in Shipping", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "return_reason", Code = "customer_error", Label = "Customer Error", SortOrder = 4 },

                new ReferenceData { IsSeedData = true, GroupCode = "lead_source", Code = "referral", Label = "Referral", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "lead_source", Code = "website", Label = "Website", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "lead_source", Code = "trade_show", Label = "Trade Show", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "lead_source", Code = "cold_call", Label = "Cold Call", SortOrder = 4 },
                new ReferenceData { IsSeedData = true, GroupCode = "lead_source", Code = "email", Label = "Email", SortOrder = 5 },
                new ReferenceData { IsSeedData = true, GroupCode = "lead_source", Code = "social_media", Label = "Social Media", SortOrder = 6 },
                new ReferenceData { IsSeedData = true, GroupCode = "lead_source", Code = "other", Label = "Other", SortOrder = 7 },

                // Job Workflow Statuses
                new ReferenceData { IsSeedData = true, GroupCode = "job_workflow_status", Code = "job_status_created", Label = "Created", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "job_workflow_status", Code = "job_status_in_progress", Label = "In Progress", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "job_workflow_status", Code = "job_status_on_hold", Label = "On Hold", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "job_workflow_status", Code = "job_status_completed", Label = "Completed", SortOrder = 4 },
                new ReferenceData { IsSeedData = true, GroupCode = "job_workflow_status", Code = "job_status_archived", Label = "Archived", SortOrder = 5 },

                // Job Hold Types
                new ReferenceData { IsSeedData = true, GroupCode = "job_hold_type", Code = "job_hold_material", Label = "Material Hold", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "job_hold_type", Code = "job_hold_quality", Label = "Quality Hold", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "job_hold_type", Code = "job_hold_customer", Label = "Customer Hold", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "job_hold_type", Code = "job_hold_engineering", Label = "Engineering Hold", SortOrder = 4 },

                // Quote Workflow Statuses
                new ReferenceData { IsSeedData = true, GroupCode = "quote_workflow_status", Code = "quote_status_draft", Label = "Draft", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "quote_workflow_status", Code = "quote_status_sent", Label = "Sent", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "quote_workflow_status", Code = "quote_status_accepted", Label = "Accepted", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "quote_workflow_status", Code = "quote_status_declined", Label = "Declined", SortOrder = 4 },
                new ReferenceData { IsSeedData = true, GroupCode = "quote_workflow_status", Code = "quote_status_expired", Label = "Expired", SortOrder = 5 },
                new ReferenceData { IsSeedData = true, GroupCode = "quote_workflow_status", Code = "quote_status_converted_to_quote", Label = "Converted to Quote", SortOrder = 6 },
                new ReferenceData { IsSeedData = true, GroupCode = "quote_workflow_status", Code = "quote_status_converted_to_order", Label = "Converted to Order", SortOrder = 7 },

                // Sales Order Workflow Statuses
                new ReferenceData { IsSeedData = true, GroupCode = "so_workflow_status", Code = "so_status_draft", Label = "Draft", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "so_workflow_status", Code = "so_status_confirmed", Label = "Confirmed", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "so_workflow_status", Code = "so_status_in_progress", Label = "In Progress", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "so_workflow_status", Code = "so_status_fulfilled", Label = "Fulfilled", SortOrder = 4 },
                new ReferenceData { IsSeedData = true, GroupCode = "so_workflow_status", Code = "so_status_closed", Label = "Closed", SortOrder = 5 },

                // Purchase Order Workflow Statuses
                new ReferenceData { IsSeedData = true, GroupCode = "po_workflow_status", Code = "po_status_draft", Label = "Draft", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "po_workflow_status", Code = "po_status_submitted", Label = "Submitted", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "po_workflow_status", Code = "po_status_partial_received", Label = "Partially Received", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "po_workflow_status", Code = "po_status_received", Label = "Received", SortOrder = 4 },
                new ReferenceData { IsSeedData = true, GroupCode = "po_workflow_status", Code = "po_status_closed", Label = "Closed", SortOrder = 5 },

                // Asset Hold Types
                new ReferenceData { IsSeedData = true, GroupCode = "asset_hold_type", Code = "asset_hold_maintenance", Label = "Maintenance Due", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "asset_hold_type", Code = "asset_hold_calibration", Label = "Calibration Expired", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "asset_hold_type", Code = "asset_hold_repair", Label = "Under Repair", SortOrder = 3 },

                // Clock Event Types — defines behavior via metadata
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "ClockIn", Label = "Clock In", SortOrder = 1, Metadata = """{"statusMapping":"In","oppositeCode":"ClockOut","category":"work","countsAsActive":true,"isMismatchable":true,"icon":"login","color":"#22c55e"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "ClockOut", Label = "Clock Out", SortOrder = 2, Metadata = """{"statusMapping":"Out","oppositeCode":"ClockIn","category":"work","countsAsActive":false,"isMismatchable":false,"icon":"logout","color":"#ef4444"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "BreakStart", Label = "Start Break", SortOrder = 3, Metadata = """{"statusMapping":"OnBreak","oppositeCode":"BreakEnd","category":"break","countsAsActive":true,"isMismatchable":true,"icon":"free_breakfast","color":"#f59e0b"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "BreakEnd", Label = "End Break", SortOrder = 4, Metadata = """{"statusMapping":"In","oppositeCode":"BreakStart","category":"break","countsAsActive":true,"isMismatchable":false,"icon":"play_arrow","color":"#22c55e"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "LunchStart", Label = "Start Lunch", SortOrder = 5, Metadata = """{"statusMapping":"OnLunch","oppositeCode":"LunchEnd","category":"lunch","countsAsActive":true,"isMismatchable":true,"icon":"restaurant","color":"#f97316"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "LunchEnd", Label = "End Lunch", SortOrder = 6, Metadata = """{"statusMapping":"In","oppositeCode":"LunchStart","category":"lunch","countsAsActive":true,"isMismatchable":false,"icon":"play_arrow","color":"#22c55e"}""" }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded reference data");
        }

        // Incremental reference data seeding — add new groups that don't exist yet
        var existingGroups = await db.ReferenceData.Select(r => r.GroupCode).Distinct().ToListAsync();
        var incrementalEntries = new List<ReferenceData>();

        if (!existingGroups.Contains("clock_event_type"))
        {
            incrementalEntries.AddRange(new[]
            {
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "ClockIn", Label = "Clock In", SortOrder = 1, Metadata = """{"statusMapping":"In","oppositeCode":"ClockOut","category":"work","countsAsActive":true,"isMismatchable":true,"icon":"login","color":"#22c55e"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "ClockOut", Label = "Clock Out", SortOrder = 2, Metadata = """{"statusMapping":"Out","oppositeCode":"ClockIn","category":"work","countsAsActive":false,"isMismatchable":false,"icon":"logout","color":"#ef4444"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "BreakStart", Label = "Start Break", SortOrder = 3, Metadata = """{"statusMapping":"OnBreak","oppositeCode":"BreakEnd","category":"break","countsAsActive":true,"isMismatchable":true,"icon":"free_breakfast","color":"#f59e0b"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "BreakEnd", Label = "End Break", SortOrder = 4, Metadata = """{"statusMapping":"In","oppositeCode":"BreakStart","category":"break","countsAsActive":true,"isMismatchable":false,"icon":"play_arrow","color":"#22c55e"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "LunchStart", Label = "Start Lunch", SortOrder = 5, Metadata = """{"statusMapping":"OnLunch","oppositeCode":"LunchEnd","category":"lunch","countsAsActive":true,"isMismatchable":true,"icon":"restaurant","color":"#f97316"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "clock_event_type", Code = "LunchEnd", Label = "End Lunch", SortOrder = 6, Metadata = """{"statusMapping":"In","oppositeCode":"LunchStart","category":"lunch","countsAsActive":true,"isMismatchable":false,"icon":"play_arrow","color":"#22c55e"}""" },
            });
        }

        if (incrementalEntries.Count > 0)
        {
            db.ReferenceData.AddRange(incrementalEntries);
            await db.SaveChangesAsync();
            Log.Information("Seeded {Count} incremental reference data entries", incrementalEntries.Count);
        }

        // State Withholding Forms — all US states with form info + DocuSeal template IDs where pre-loaded
        if (!await db.ReferenceData.AnyAsync(r => r.GroupCode == "state_withholding"))
        {
            db.ReferenceData.AddRange(
                // States with NO income tax — marked as "none" category
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "AK", Label = "Alaska", SortOrder = 1, Metadata = """{"category":"no_tax"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "FL", Label = "Florida", SortOrder = 2, Metadata = """{"category":"no_tax"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "NV", Label = "Nevada", SortOrder = 3, Metadata = """{"category":"no_tax"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "NH", Label = "New Hampshire", SortOrder = 4, Metadata = """{"category":"no_tax"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "SD", Label = "South Dakota", SortOrder = 5, Metadata = """{"category":"no_tax"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "TN", Label = "Tennessee", SortOrder = 6, Metadata = """{"category":"no_tax"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "TX", Label = "Texas", SortOrder = 7, Metadata = """{"category":"no_tax"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "WA", Label = "Washington", SortOrder = 8, Metadata = """{"category":"no_tax"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "WY", Label = "Wyoming", SortOrder = 9, Metadata = """{"category":"no_tax"}""" },

                // States that accept federal W-4 only — marked as "federal" category
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "CO", Label = "Colorado", SortOrder = 10, Metadata = """{"category":"federal","formName":"Uses Federal W-4"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "MT", Label = "Montana", SortOrder = 11, Metadata = """{"category":"federal","formName":"Uses Federal W-4"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "NM", Label = "New Mexico", SortOrder = 12, Metadata = """{"category":"federal","formName":"Uses Federal W-4"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "ND", Label = "North Dakota", SortOrder = 13, Metadata = """{"category":"federal","formName":"Uses Federal W-4"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "UT", Label = "Utah", SortOrder = 14, Metadata = """{"category":"federal","formName":"Uses Federal W-4"}""" },

                // States with own forms — pre-loaded in DocuSeal (docuSealTemplateId set)
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "AR", Label = "Arkansas", SortOrder = 15, Metadata = """{"category":"state_form","formName":"AR4EC","sourceUrl":"https://www.dfa.arkansas.gov/images/uploads/incomeTaxOffice/AR4EC.pdf","docuSealTemplateId":3}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "CA", Label = "California", SortOrder = 16, Metadata = """{"category":"state_form","formName":"DE 4","sourceUrl":"https://edd.ca.gov/siteassets/files/pdf_pub_ctr/de4.pdf","docuSealTemplateId":4}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "KS", Label = "Kansas", SortOrder = 17, Metadata = """{"category":"state_form","formName":"K-4","sourceUrl":"https://www.ksrevenue.gov/pdf/k-4.pdf","docuSealTemplateId":5}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "MA", Label = "Massachusetts", SortOrder = 18, Metadata = """{"category":"state_form","formName":"M-4","sourceUrl":"https://www.mass.gov/doc/form-m-4-massachusetts-employees-withholding-exemption-certificate/download","docuSealTemplateId":6}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "NJ", Label = "New Jersey", SortOrder = 19, Metadata = """{"category":"state_form","formName":"NJ-W4","sourceUrl":"https://www.nj.gov/treasury/taxation/pdf/current/njw4.pdf","docuSealTemplateId":7}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "NY", Label = "New York", SortOrder = 20, Metadata = """{"category":"state_form","formName":"IT-2104","sourceUrl":"https://www.tax.ny.gov/pdf/current_forms/it/it2104_fill_in.pdf","docuSealTemplateId":8}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "OR", Label = "Oregon", SortOrder = 21, Metadata = """{"category":"state_form","formName":"OR-W-4","sourceUrl":"https://www.oregon.gov/dor/forms/FormsPubs/form-or-w-4_101-402_2024.pdf","docuSealTemplateId":9}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "PA", Label = "Pennsylvania", SortOrder = 22, Metadata = """{"category":"state_form","formName":"REV-419","sourceUrl":"https://www.revenue.pa.gov/FormsandPublications/FormsforIndividuals/PIT/Documents/rev-419.pdf","docuSealTemplateId":10}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "VA", Label = "Virginia", SortOrder = 23, Metadata = """{"category":"state_form","formName":"VA-4","sourceUrl":"https://www.tax.virginia.gov/sites/default/files/taxforms/withholding/any/va-4-any.pdf","docuSealTemplateId":11}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "VT", Label = "Vermont", SortOrder = 24, Metadata = """{"category":"state_form","formName":"W-4VT","sourceUrl":"https://tax.vermont.gov/sites/tax/files/documents/W-4VT.pdf","docuSealTemplateId":12}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "WI", Label = "Wisconsin", SortOrder = 25, Metadata = """{"category":"state_form","formName":"WT-4","sourceUrl":"https://www.revenue.wi.gov/DOR%20Publications/pb166.pdf","docuSealTemplateId":13}""" },

                // States with own forms — source URLs for PDF extraction pipeline
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "AL", Label = "Alabama", SortOrder = 26, Metadata = """{"category":"state_form","formName":"A-4","sourceUrl":"https://www.revenue.alabama.gov/wp-content/uploads/2017/05/A-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "AZ", Label = "Arizona", SortOrder = 27, Metadata = """{"category":"state_form","formName":"A-4","sourceUrl":"https://azdor.gov/sites/default/files/media/FORM_A-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "CT", Label = "Connecticut", SortOrder = 28, Metadata = """{"category":"state_form","formName":"CT-W4","sourceUrl":"https://portal.ct.gov/-/media/drs/forms/2024/withholdingforms/ct-w4_1224.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "DC", Label = "District of Columbia", SortOrder = 29, Metadata = """{"category":"state_form","formName":"D-4","sourceUrl":"https://otr.cfo.dc.gov/sites/default/files/dc/sites/otr/publication/attachments/2024_D-4_Fill_In.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "DE", Label = "Delaware", SortOrder = 30, Metadata = """{"category":"state_form","formName":"W-4 (DE)","sourceUrl":"https://revenue.delaware.gov/wp-content/uploads/sites/tax/2020/02/Delaware_W4_Employee_Withholding.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "GA", Label = "Georgia", SortOrder = 31, Metadata = """{"category":"state_form","formName":"G-4","sourceUrl":"https://dor.georgia.gov/sites/dor.georgia.gov/files/related_files/document/TSD/Form/2024_G-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "HI", Label = "Hawaii", SortOrder = 32, Metadata = """{"category":"state_form","formName":"HW-4","sourceUrl":"https://files.hawaii.gov/tax/forms/2023/hw4_i.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "IA", Label = "Iowa", SortOrder = 33, Metadata = """{"category":"state_form","formName":"IA W-4","sourceUrl":"https://tax.iowa.gov/sites/default/files/2023-01/IAW4%2844-019%29.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "ID", Label = "Idaho", SortOrder = 34, Metadata = """{"category":"state_form","formName":"ID W-4","sourceUrl":"https://tax.idaho.gov/wp-content/uploads/forms/EFO00307/EFO00307_04-28-2025.pdf","docuSealTemplateId":14}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "IL", Label = "Illinois", SortOrder = 35, Metadata = """{"category":"state_form","formName":"IL-W-4","sourceUrl":"https://tax.illinois.gov/content/dam/soi/en/web/tax/forms/withholding/documents/il-w-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "IN", Label = "Indiana", SortOrder = 36, Metadata = """{"category":"state_form","formName":"WH-4","sourceUrl":"https://www.in.gov/dor/files/WH-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "KY", Label = "Kentucky", SortOrder = 37, Metadata = """{"category":"state_form","formName":"K-4 (KY)","sourceUrl":"https://revenue.ky.gov/Forms/Form%20K-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "LA", Label = "Louisiana", SortOrder = 38, Metadata = """{"category":"state_form","formName":"L-4","sourceUrl":"https://revenue.louisiana.gov/Forms/ForIndividuals/R-1300(L4).pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "MD", Label = "Maryland", SortOrder = 39, Metadata = """{"category":"state_form","formName":"MW507","sourceUrl":"https://www.marylandtaxes.gov/forms/current_forms/MW507.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "ME", Label = "Maine", SortOrder = 40, Metadata = """{"category":"state_form","formName":"W-4ME","sourceUrl":"https://www.maine.gov/revenue/sites/maine.gov.revenue/files/inline-files/w-4me_2024.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "MI", Label = "Michigan", SortOrder = 41, Metadata = """{"category":"state_form","formName":"MI-W4","sourceUrl":"https://www.michigan.gov/taxes/-/media/Project/Websites/taxes/Forms/2024/Withholding/MI-W4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "MN", Label = "Minnesota", SortOrder = 42, Metadata = """{"category":"state_form","formName":"W-4MN","sourceUrl":"https://www.revenue.state.mn.us/sites/default/files/2024-01/w-4mn_24.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "MO", Label = "Missouri", SortOrder = 43, Metadata = """{"category":"state_form","formName":"MO W-4","sourceUrl":"https://dor.mo.gov/forms/MO%20W-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "MS", Label = "Mississippi", SortOrder = 44, Metadata = """{"category":"state_form","formName":"89-350","sourceUrl":"https://www.dor.ms.gov/sites/default/files/Forms/Individual/Withholding/89-350-23-1.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "NC", Label = "North Carolina", SortOrder = 45, Metadata = """{"category":"state_form","formName":"NC-4","sourceUrl":"https://www.ncdor.gov/documents/nc-4-employee-withholding-allowance-certificate"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "NE", Label = "Nebraska", SortOrder = 46, Metadata = """{"category":"state_form","formName":"W-4N","sourceUrl":"https://revenue.nebraska.gov/sites/revenue.nebraska.gov/files/doc/tax-forms/f_w4n.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "OH", Label = "Ohio", SortOrder = 47, Metadata = """{"category":"state_form","formName":"IT-4","sourceUrl":"https://tax.ohio.gov/static/forms/ohio_individual/individual/2024/it-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "OK", Label = "Oklahoma", SortOrder = 48, Metadata = """{"category":"state_form","formName":"OK-W-4","sourceUrl":"https://oklahoma.gov/content/dam/ok/en/tax/documents/forms/withholding/OK-W-4.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "RI", Label = "Rhode Island", SortOrder = 49, Metadata = """{"category":"state_form","formName":"RI W-4","sourceUrl":"https://tax.ri.gov/sites/g/files/xkgbur541/files/forms/W-4_2024.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "SC", Label = "South Carolina", SortOrder = 50, Metadata = """{"category":"state_form","formName":"SC W-4","sourceUrl":"https://dor.sc.gov/forms-site/Forms/SC_W4_2024.pdf"}""" },
                new ReferenceData { IsSeedData = true, GroupCode = "state_withholding", Code = "WV", Label = "West Virginia", SortOrder = 51, Metadata = """{"category":"state_form","formName":"WV/IT-104","sourceUrl":"https://tax.wv.gov/Documents/TaxForms/it104.pdf"}""" }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded state withholding reference data (51 entries)");
        }

        // Bought-parts effort PR2.5 — quote currencies. Seeds the seven most
        // common ISO-4217 codes for use as the QuoteCurrency dropdown source
        // on PurchaseOrder + VendorPart. Admin-extensible via the standard
        // reference-data UI; per-group idempotent so re-running on an
        // existing install is safe.
        if (!await db.ReferenceData.AnyAsync(r => r.GroupCode == "currency"))
        {
            db.ReferenceData.AddRange(
                new ReferenceData { IsSeedData = true, GroupCode = "currency", Code = "USD", Label = "USD — US Dollar", SortOrder = 1 },
                new ReferenceData { IsSeedData = true, GroupCode = "currency", Code = "EUR", Label = "EUR — Euro", SortOrder = 2 },
                new ReferenceData { IsSeedData = true, GroupCode = "currency", Code = "GBP", Label = "GBP — British Pound", SortOrder = 3 },
                new ReferenceData { IsSeedData = true, GroupCode = "currency", Code = "CAD", Label = "CAD — Canadian Dollar", SortOrder = 4 },
                new ReferenceData { IsSeedData = true, GroupCode = "currency", Code = "MXN", Label = "MXN — Mexican Peso", SortOrder = 5 },
                new ReferenceData { IsSeedData = true, GroupCode = "currency", Code = "JPY", Label = "JPY — Japanese Yen", SortOrder = 6 },
                new ReferenceData { IsSeedData = true, GroupCode = "currency", Code = "CNY", Label = "CNY — Chinese Yuan", SortOrder = 7 }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded currency reference data (7 entries)");
        }

        // Units of Measure — the stock/purchase/sales UoM dropdowns and the
        // part-workflow inventory step resolve a UoM by code. Without these
        // rows the inventory-step UoM picker offers values the server can't
        // resolve (ResolveUomIdAsync returns null), so the hasInventory gate
        // never passes. Codes here MUST match the picker option values.
        // 'ea' (each) is the base Count unit and the default for new parts.
        if (!await db.UnitsOfMeasure.AnyAsync())
        {
            db.UnitsOfMeasure.AddRange(
                new UnitOfMeasure { Code = "ea", Name = "Each", Symbol = "ea", Category = UomCategory.Count, DecimalPlaces = 0, IsBaseUnit = true, SortOrder = 1 },
                new UnitOfMeasure { Code = "pr", Name = "Pair", Symbol = "pr", Category = UomCategory.Count, DecimalPlaces = 0, SortOrder = 2 },
                new UnitOfMeasure { Code = "dz", Name = "Dozen", Symbol = "dz", Category = UomCategory.Count, DecimalPlaces = 0, SortOrder = 3 },
                new UnitOfMeasure { Code = "box", Name = "Box", Symbol = "box", Category = UomCategory.Count, DecimalPlaces = 0, SortOrder = 4 },
                new UnitOfMeasure { Code = "cs", Name = "Case", Symbol = "cs", Category = UomCategory.Count, DecimalPlaces = 0, SortOrder = 5 },
                new UnitOfMeasure { Code = "kg", Name = "Kilogram", Symbol = "kg", Category = UomCategory.Weight, DecimalPlaces = 3, IsBaseUnit = true, SortOrder = 10 },
                new UnitOfMeasure { Code = "g", Name = "Gram", Symbol = "g", Category = UomCategory.Weight, DecimalPlaces = 2, SortOrder = 11 },
                new UnitOfMeasure { Code = "lb", Name = "Pound", Symbol = "lb", Category = UomCategory.Weight, DecimalPlaces = 3, SortOrder = 12 },
                new UnitOfMeasure { Code = "oz", Name = "Ounce", Symbol = "oz", Category = UomCategory.Weight, DecimalPlaces = 2, SortOrder = 13 },
                new UnitOfMeasure { Code = "m", Name = "Meter", Symbol = "m", Category = UomCategory.Length, DecimalPlaces = 3, IsBaseUnit = true, SortOrder = 20 },
                new UnitOfMeasure { Code = "cm", Name = "Centimeter", Symbol = "cm", Category = UomCategory.Length, DecimalPlaces = 2, SortOrder = 21 },
                new UnitOfMeasure { Code = "mm", Name = "Millimeter", Symbol = "mm", Category = UomCategory.Length, DecimalPlaces = 2, SortOrder = 22 },
                new UnitOfMeasure { Code = "ft", Name = "Foot", Symbol = "ft", Category = UomCategory.Length, DecimalPlaces = 3, SortOrder = 23 },
                new UnitOfMeasure { Code = "in", Name = "Inch", Symbol = "in", Category = UomCategory.Length, DecimalPlaces = 3, SortOrder = 24 },
                new UnitOfMeasure { Code = "L", Name = "Liter", Symbol = "L", Category = UomCategory.Volume, DecimalPlaces = 3, IsBaseUnit = true, SortOrder = 30 },
                new UnitOfMeasure { Code = "mL", Name = "Milliliter", Symbol = "mL", Category = UomCategory.Volume, DecimalPlaces = 2, SortOrder = 31 },
                new UnitOfMeasure { Code = "gal", Name = "Gallon", Symbol = "gal", Category = UomCategory.Volume, DecimalPlaces = 3, SortOrder = 32 }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded units of measure (17 entries)");
        }

        // Compliance Form Templates
        if (!await db.ComplianceFormTemplates.AnyAsync())
        {
            db.ComplianceFormTemplates.AddRange(
                new ComplianceFormTemplate
                {
                    Name = "W-4 Federal Tax Withholding",
                    FormType = ComplianceFormType.W4,
                    Description = "Employee's Withholding Certificate — determines federal income tax withholding from your paycheck.",
                    Icon = "request_quote",
                    SourceUrl = "https://www.irs.gov/pub/irs-pdf/fw4.pdf",
                    IsAutoSync = true,
                    IsActive = true,
                    SortOrder = 1,
                    RequiresIdentityDocs = false,
                    BlocksJobAssignment = true,
                    ProfileCompletionKey = "w4",
                },
                new ComplianceFormTemplate
                {
                    Name = "I-9 Employment Eligibility",
                    FormType = ComplianceFormType.I9,
                    Description = "Employment Eligibility Verification — verifies identity and authorization to work in the United States.",
                    Icon = "verified_user",
                    SourceUrl = "https://www.uscis.gov/sites/default/files/document/forms/i-9.pdf",
                    IsAutoSync = true,
                    IsActive = true,
                    SortOrder = 2,
                    RequiresIdentityDocs = true,
                    BlocksJobAssignment = true,
                    ProfileCompletionKey = "i9",
                },
                new ComplianceFormTemplate
                {
                    Name = "State Tax Withholding",
                    FormType = ComplianceFormType.StateWithholding,
                    Description = "State-specific income tax withholding form — varies by state. Upload the form for your state.",
                    Icon = "account_balance",
                    IsAutoSync = false,
                    IsActive = true,
                    SortOrder = 3,
                    RequiresIdentityDocs = false,
                    BlocksJobAssignment = true,
                    ProfileCompletionKey = "stateWithholding",
                },
                new ComplianceFormTemplate
                {
                    Name = "Direct Deposit Authorization",
                    FormType = ComplianceFormType.DirectDeposit,
                    Description = "Authorize electronic deposit of your paycheck to your bank account.",
                    Icon = "account_balance_wallet",
                    IsAutoSync = false,
                    IsActive = true,
                    SortOrder = 4,
                    RequiresIdentityDocs = false,
                    BlocksJobAssignment = false,
                    ProfileCompletionKey = "directDeposit",
                },
                new ComplianceFormTemplate
                {
                    Name = "Workers' Comp Acknowledgment",
                    FormType = ComplianceFormType.WorkersComp,
                    Description = "Acknowledge receipt and understanding of workers' compensation coverage and procedures.",
                    Icon = "health_and_safety",
                    IsAutoSync = false,
                    IsActive = true,
                    SortOrder = 5,
                    RequiresIdentityDocs = false,
                    BlocksJobAssignment = false,
                    ProfileCompletionKey = "workersComp",
                },
                new ComplianceFormTemplate
                {
                    Name = "Employee Handbook Acknowledgment",
                    FormType = ComplianceFormType.Handbook,
                    Description = "Acknowledge receipt and understanding of the employee handbook and company policies.",
                    Icon = "menu_book",
                    IsAutoSync = false,
                    IsActive = true,
                    SortOrder = 6,
                    RequiresIdentityDocs = false,
                    BlocksJobAssignment = false,
                    ProfileCompletionKey = "handbook",
                }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded compliance form templates");
        }

        // Form definitions are NOT seeded — they are dynamically extracted from PDFs
        // when an admin uploads a document or triggers extraction via the extract-definition endpoint.
        // Templates with SourceUrl will have their definitions extracted on first sync.

        // Backfill state withholding source URLs for existing installs
        // Load all entries in memory — Metadata is jsonb, so string.Contains() doesn't translate to SQL
        var stateEntries = (await db.ReferenceData
            .Where(r => r.GroupCode == "state_withholding")
            .ToListAsync())
            .Where(r => r.Metadata != null && !r.Metadata.Contains("sourceUrl"))
            .ToList();
        if (stateEntries.Count > 0)
        {
            var stateUrls = StateWithholdingUrls.GetAll();
            foreach (var entry in stateEntries)
            {
                if (stateUrls.TryGetValue(entry.Code, out var url))
                {
                    // Inject sourceUrl into existing metadata JSON
                    entry.Metadata = entry.Metadata!.TrimEnd('}') + $@",""sourceUrl"":""{url}""}}";
                }
            }
            await db.SaveChangesAsync();
            Log.Information("Backfilled {Count} state withholding entries with source URLs", stateEntries.Count);
        }

        // Company Profile Settings
        if (!await db.SystemSettings.AnyAsync(s => s.Key == "company.name"))
        {
            db.SystemSettings.AddRange(
                new SystemSetting { Key = "company.name", Value = "", Description = "Legal business name" },
                new SystemSetting { Key = "company.phone", Value = "", Description = "Main company phone" },
                new SystemSetting { Key = "company.email", Value = "", Description = "Main company email" },
                new SystemSetting { Key = "company.ein", Value = "", Description = "Federal tax identification number (EIN)" },
                new SystemSetting { Key = "company.website", Value = "", Description = "Company website URL" },
                new SystemSetting { Key = "company_state", Value = "ID", Description = "Company home state (determines default state withholding form)" }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded company profile settings");
        }

        // HR Policy Document URLs — surfaced on the onboarding Acknowledgments step
        // so employees can read the policy before checking the toggle. Empty by
        // default; admins paste a link in Admin → Settings later. The handbook
        // acknowledgment is hidden + not required when its URL is blank.
        if (!await db.SystemSettings.AnyAsync(s => s.Key == "hr.workersCompDocUrl"))
        {
            db.SystemSettings.AddRange(
                new SystemSetting { Key = "hr.workersCompDocUrl", Value = "", Description = "URL to the company workers' compensation notice / policy document shown during onboarding." },
                new SystemSetting { Key = "hr.handbookDocUrl",    Value = "", Description = "URL to the employee handbook PDF. When blank the handbook acknowledgment step is skipped." }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded HR policy document URL settings");
        }

        // Bought-parts effort PR4 — purchasing thresholds. Tenant-wide
        // default for the off-tier price-variance prompt at PO line entry;
        // per-vendor override on `Vendor.OffTierVariancePct` falls back to
        // this when null. 5% is the design-memo default — wide enough to
        // ignore rounding noise, tight enough to catch real surprises.
        if (!await db.SystemSettings.AnyAsync(s => s.Key == "purchasing.offTierVariancePct"))
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = "purchasing.offTierVariancePct",
                Value = "5",
                Description = "Default off-tier price variance percent that triggers the PO-line off-tier prompt. Per-vendor override on Vendor.OffTierVariancePct.",
            });
            await db.SaveChangesAsync();
            Log.Information("Seeded purchasing.offTierVariancePct = 5");
        }

        // ── Training Modules & Paths ──────────────────────────────────────
        await SeedTrainingAsync(db);
        await SeedAdditionalTrainingPathsAsync(db);

        // ── Approval Workflows ────────────────────────────────────────────
        await SeedApprovalWorkflowsAsync(db);

        // ── Pillar 1 — Part ItemKind taxonomy (idempotent upsert) ─────────
        await SeedPartItemKindsAsync(db);

        // ── Pillar 2 follow-up — Part MaterialSpec / ValuationClass ───────
        await SeedPartMaterialSpecsAsync(db);
        await SeedPartValuationClassesAsync(db);

        // ── Job Number Sequence (essential) ───────────────────────────────
        // JobRepository.GenerateNextJobNumberAsync reads nextval('job_number_seq').
        // Created here (always-run) so job creation works on clean installs —
        // not only when demo data is seeded. Idempotent; the demo path advances
        // it past seeded jobs via setval. Without this, job creation 500s on
        // every SEED_DEMO_DATA=false install with: relation "job_number_seq"
        // does not exist.
        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE IF NOT EXISTS job_number_seq START WITH 1");
        Log.Information("Ensured job_number_seq exists");
    }

    /// <summary>
    /// Pillar 1 — descriptive item-kind taxonomy on parts. Independent of the
    /// procurement_source × inventory_class axes; this is the "what kind of
    /// thing is it" tag (Fastener, Electronic, Packaging, etc.). Admin can
    /// extend the taxonomy via the standard reference_data CRUD; the seed
    /// upsert never overwrites admin-edited rows because we key on
    /// (GroupCode, Code) and skip existing entries.
    /// </summary>
    private static async Task SeedPartItemKindsAsync(AppDbContext db)
    {
        const string group = "part.item_kind";
        var existing = await db.ReferenceData
            .Where(r => r.GroupCode == group)
            .Select(r => r.Code)
            .ToListAsync();
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seeds = new (string Code, string Label, int Sort)[]
        {
            ("fastener", "Fastener", 1),
            ("electronic", "Electronic", 2),
            ("packaging", "Packaging", 3),
            ("hardware", "Hardware", 4),
            ("material", "Material", 5),
            ("subassembly", "Subassembly", 6),
            ("phantom", "Phantom", 7),
            ("tool", "Tool", 8),
            ("consumable", "Consumable", 9),
            ("custom", "Custom", 10),
        };

        var added = 0;
        foreach (var (code, label, sort) in seeds)
        {
            if (existingSet.Contains(code)) continue;
            db.ReferenceData.Add(new ReferenceData
            {
                IsSeedData = true,
                GroupCode = group,
                Code = code,
                Label = label,
                SortOrder = sort,
                IsActive = true,
            });
            added++;
        }
        if (added > 0) await db.SaveChangesAsync();
    }

    /// <summary>
    /// Pillar 2 follow-up — Material specification taxonomy on parts.
    /// Hierarchical: parent material families (Aluminum, Steel, Stainless,
    /// Plastic, …) with common engineering grades as children. Idempotent
    /// upsert keyed on (GroupCode, Code); admin extensions are preserved.
    /// </summary>
    internal static async Task SeedPartMaterialSpecsAsync(AppDbContext db)
    {
        const string group = "part.material_spec";

        var existing = await db.ReferenceData
            .Where(r => r.GroupCode == group)
            .ToListAsync();
        var existingByCode = existing
            .GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Parents: top-level material families. ParentId stays null.
        var parents = new (string Code, string Label, int Sort)[]
        {
            ("aluminum", "Aluminum", 1),
            ("steel", "Steel", 2),
            ("stainless", "Stainless Steel", 3),
            ("plastic", "Plastic", 4),
            ("brass", "Brass / Bronze", 5),
            ("copper", "Copper", 6),
            ("titanium", "Titanium", 7),
            ("other-metal", "Other Metal", 8),
            ("composite", "Composite", 9),
            ("rubber", "Rubber / Elastomer", 10),
        };

        var parentAdded = 0;
        foreach (var (code, label, sort) in parents)
        {
            if (existingByCode.ContainsKey(code)) continue;
            db.ReferenceData.Add(new ReferenceData
            {
                IsSeedData = true,
                GroupCode = group,
                Code = code,
                Label = label,
                SortOrder = sort,
                IsActive = true,
                ParentId = null,
            });
            parentAdded++;
        }
        if (parentAdded > 0) await db.SaveChangesAsync();

        // Re-load after parent insert so we can resolve ParentId for children.
        var parentByCode = (await db.ReferenceData
                .Where(r => r.GroupCode == group && r.ParentId == null)
                .ToListAsync())
            .ToDictionary(r => r.Code, r => r.Id, StringComparer.OrdinalIgnoreCase);

        // Children: engineering grades grouped under their parent family.
        // (parentCode, code, label, sort)
        var children = new (string ParentCode, string Code, string Label, int Sort)[]
        {
            ("aluminum",  "aluminum-6061-t6",  "6061-T6",                    1),
            ("aluminum",  "aluminum-7075-t6",  "7075-T6",                    2),
            ("aluminum",  "aluminum-5052-h32", "5052-H32",                   3),
            ("aluminum",  "aluminum-2024-t3",  "2024-T3",                    4),

            ("steel",     "steel-1018",        "1018 Cold-Rolled",           1),
            ("steel",     "steel-1045",        "1045",                       2),
            ("steel",     "steel-4140",        "4140 Alloy",                 3),
            ("steel",     "steel-a36",         "A36 Structural",             4),
            ("steel",     "steel-12l14",       "12L14 (Free-Machining)",     5),

            ("stainless", "stainless-303",     "303",                        1),
            ("stainless", "stainless-304",     "304",                        2),
            ("stainless", "stainless-316",     "316 / 316L",                 3),
            ("stainless", "stainless-410",     "410",                        4),
            ("stainless", "stainless-440c",    "440C",                       5),
            ("stainless", "stainless-17-4-ph", "17-4 PH",                    6),

            ("plastic",   "plastic-abs",       "ABS",                        1),
            ("plastic",   "plastic-pc",        "Polycarbonate",              2),
            ("plastic",   "plastic-delrin",    "Delrin (Acetal)",            3),
            ("plastic",   "plastic-uhmw",      "UHMW Polyethylene",          4),
            ("plastic",   "plastic-nylon-6-6", "Nylon 6/6",                  5),
            ("plastic",   "plastic-pp",        "Polypropylene",              6),
            ("plastic",   "plastic-pvc",       "PVC",                        7),
            ("plastic",   "plastic-peek",      "PEEK",                       8),

            ("brass",     "brass-360",         "Brass 360 (Free-Machining)", 1),
            ("brass",     "bronze-932",        "Bronze 932 (SAE 660)",       2),

            ("copper",    "copper-c110",       "Copper C110 (ETP)",          1),

            ("titanium",  "titanium-grade-2",  "Grade 2 (Pure)",             1),
            ("titanium",  "titanium-grade-5",  "Grade 5 (Ti-6Al-4V)",        2),
        };

        var childAdded = 0;
        foreach (var (parentCode, code, label, sort) in children)
        {
            if (existingByCode.ContainsKey(code)) continue;
            if (!parentByCode.TryGetValue(parentCode, out var parentId)) continue;
            db.ReferenceData.Add(new ReferenceData
            {
                IsSeedData = true,
                GroupCode = group,
                Code = code,
                Label = label,
                SortOrder = sort,
                IsActive = true,
                ParentId = parentId,
            });
            childAdded++;
        }
        if (childAdded > 0) await db.SaveChangesAsync();
    }

    /// <summary>
    /// Pillar 2 follow-up — Inventory valuation method taxonomy on parts.
    /// Flat list (no hierarchy). Idempotent upsert keyed on (GroupCode, Code).
    /// </summary>
    internal static async Task SeedPartValuationClassesAsync(AppDbContext db)
    {
        const string group = "part.valuation_class";
        var existing = await db.ReferenceData
            .Where(r => r.GroupCode == group)
            .Select(r => r.Code)
            .ToListAsync();
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seeds = new (string Code, string Label, int Sort)[]
        {
            ("fifo",         "FIFO (First-In, First-Out)",   1),
            ("lifo",         "LIFO (Last-In, First-Out)",    2),
            ("weighted-avg", "Weighted Average",             3),
            ("standard",     "Standard Cost",                4),
            ("specific",     "Specific Identification",      5),
        };

        var added = 0;
        foreach (var (code, label, sort) in seeds)
        {
            if (existingSet.Contains(code)) continue;
            db.ReferenceData.Add(new ReferenceData
            {
                IsSeedData = true,
                GroupCode = group,
                Code = code,
                Label = label,
                SortOrder = sort,
                IsActive = true,
            });
            added++;
        }
        if (added > 0) await db.SaveChangesAsync();
    }

    private static async Task SeedApprovalWorkflowsAsync(AppDbContext db)
    {
        var changed = false;

        if (!await db.ApprovalWorkflows.AnyAsync(w => w.EntityType == "PurchaseOrder"))
        {
            db.ApprovalWorkflows.Add(new ApprovalWorkflow
            {
                Name = "Purchase Order — Manager Review",
                EntityType = "PurchaseOrder",
                Description = "Any submitted PO. Auto-approved below $500; above that, routed to any Manager.",
                IsActive = true,
                Steps =
                {
                    new ApprovalStep
                    {
                        StepNumber = 1,
                        Name = "Manager Approval",
                        ApproverType = ApproverType.Role,
                        ApproverRole = "Manager",
                        AutoApproveBelow = 500m,
                        EscalationHours = 48,
                        RequireComments = false,
                        AllowDelegation = true,
                    },
                },
            });
            changed = true;
        }

        if (!await db.ApprovalWorkflows.AnyAsync(w => w.EntityType == "Expense"))
        {
            db.ApprovalWorkflows.Add(new ApprovalWorkflow
            {
                Name = "Expense — Manager Review",
                EntityType = "Expense",
                Description = "Submitted expenses. Auto-approved below $100; above that, routed to any Manager.",
                IsActive = true,
                Steps =
                {
                    new ApprovalStep
                    {
                        StepNumber = 1,
                        Name = "Manager Approval",
                        ApproverType = ApproverType.Role,
                        ApproverRole = "Manager",
                        AutoApproveBelow = 100m,
                        EscalationHours = 72,
                        RequireComments = false,
                        AllowDelegation = true,
                    },
                },
            });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
            Log.Information("Seeded default approval workflows");
        }
    }

}
