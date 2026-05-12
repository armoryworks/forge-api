using MediatR;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetReportEntitiesQuery : IRequest<List<ReportEntityDefinitionModel>>;

public class GetReportEntitiesHandler : IRequestHandler<GetReportEntitiesQuery, List<ReportEntityDefinitionModel>>
{
    public Task<List<ReportEntityDefinitionModel>> Handle(GetReportEntitiesQuery request, CancellationToken cancellationToken)
    {
        var entities = new List<ReportEntityDefinitionModel>
        {
            // ── Jobs ──────────────────────────────────────────────────
            new("Jobs", "Jobs", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("JobNumber", "Job Number", "string", true, true, false),
                new("Title", "Title", "string", true, true, false),
                new("Description", "Description", "string", true, false, false),
                new("Priority", "Priority", "enum", true, true, true),
                new("IsArchived", "Archived", "boolean", true, true, true),
                new("DueDate", "Due Date", "date", true, true, true),
                new("StartDate", "Start Date", "date", true, true, false),
                new("CompletedDate", "Completed Date", "date", true, true, false),
                new("BoardPosition", "Board Position", "number", true, true, false),
                new("IterationCount", "Iteration Count", "number", true, true, false),
                new("IterationNotes", "Iteration Notes", "string", true, false, false),
                new("IsInternal", "Internal", "boolean", true, true, true),
                new("Disposition", "Disposition", "enum", true, true, true),
                new("DispositionNotes", "Disposition Notes", "string", true, false, false),
                new("DispositionAt", "Disposed At", "date", true, true, false),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("TrackTypeId", "Track Type ID", "number", true, true, false),
                new("CurrentStageId", "Current Stage ID", "number", true, true, false),
                new("CustomerId", "Customer ID", "number", true, true, false),
                new("AssigneeId", "Assignee ID", "number", true, true, false),
                new("PartId", "Part ID", "number", true, true, false),
                new("ParentJobId", "Parent Job ID", "number", true, true, false),
                new("SalesOrderLineId", "Sales Order Line ID", "number", true, true, false),
                new("InternalProjectTypeId", "Internal Project Type ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Customer.Name", "Customer Name", "string", true, true, true),
                new("Customer.CompanyName", "Customer Company", "string", true, true, true),
                new("TrackType.Name", "Track Type", "string", true, true, true),
                new("CurrentStage.Name", "Current Stage", "string", true, true, true),
                new("Part.PartNumber", "Part Number", "string", true, true, false),
                new("Part.Description", "Part Description", "string", true, false, false),
                new("ParentJob.JobNumber", "Parent Job Number", "string", true, true, false),
            }),

            // ── Parts ─────────────────────────────────────────────────
            new("Parts", "Parts", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("PartNumber", "Part Number", "string", true, true, false),
                new("Description", "Description", "string", true, true, false),
                new("Revision", "Revision", "string", true, true, true),
                new("Status", "Status", "enum", true, true, true),
                new("ProcurementSource", "Procurement Source", "enum", true, true, true),
                new("InventoryClass", "Inventory Class", "enum", true, true, true),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("MinStockThreshold", "Min Stock Threshold", "number", true, true, false),
                new("ReorderPoint", "Reorder Point", "number", true, true, false),
                new("PreferredVendorId", "Preferred Vendor ID", "number", true, true, false),
                new("ToolingAssetId", "Tooling Asset ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("PreferredVendor.CompanyName", "Preferred Vendor", "string", true, true, true),
                new("ToolingAsset.Name", "Tooling Asset", "string", true, true, false),
            }),

            // ── Customers ─────────────────────────────────────────────
            new("Customers", "Customers", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("Name", "Name", "string", true, true, false),
                new("CompanyName", "Company Name", "string", true, true, true),
                new("Email", "Email", "string", true, true, false),
                new("Phone", "Phone", "string", true, false, false),
                new("IsActive", "Active", "boolean", true, true, true),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
            }),

            // ── Expenses ──────────────────────────────────────────────
            new("Expenses", "Expenses", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("Amount", "Amount", "number", true, true, false),
                new("Category", "Category", "string", true, true, true),
                new("Description", "Description", "string", true, false, false),
                new("Status", "Status", "enum", true, true, true),
                new("ExpenseDate", "Expense Date", "date", true, true, true),
                new("UserId", "User ID", "number", true, true, false),
                new("JobId", "Job ID", "number", true, true, false),
                new("ApprovedBy", "Approved By ID", "number", true, true, false),
                new("ApprovalNotes", "Approval Notes", "string", true, false, false),
                new("ReceiptFileId", "Receipt File ID", "string", true, false, false),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Job.JobNumber", "Job Number", "string", true, true, false),
                new("Job.Title", "Job Title", "string", true, false, false),
            }),

            // ── Time Entries ──────────────────────────────────────────
            new("TimeEntries", "Time Entries", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("Date", "Date", "date", true, true, true),
                new("DurationMinutes", "Duration (min)", "number", true, true, false),
                new("Category", "Category", "string", true, true, true),
                new("Notes", "Notes", "string", true, false, false),
                new("IsManual", "Manual Entry", "boolean", true, true, true),
                new("IsLocked", "Locked", "boolean", true, true, true),
                new("UserId", "User ID", "number", true, true, false),
                new("JobId", "Job ID", "number", true, true, false),
                new("TimerStart", "Timer Start", "date", true, true, false),
                new("TimerStop", "Timer Stop", "date", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Job.JobNumber", "Job Number", "string", true, true, false),
                new("Job.Title", "Job Title", "string", true, false, false),
            }),

            // ── Invoices ──────────────────────────────────────────────
            new("Invoices", "Invoices", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("InvoiceNumber", "Invoice Number", "string", true, true, false),
                new("Status", "Status", "enum", true, true, true),
                new("InvoiceDate", "Invoice Date", "date", true, true, true),
                new("DueDate", "Due Date", "date", true, true, true),
                new("CreditTerms", "Credit Terms", "enum", true, true, true),
                new("TaxRate", "Tax Rate", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("CustomerId", "Customer ID", "number", true, true, false),
                new("SalesOrderId", "Sales Order ID", "number", true, true, false),
                new("ShipmentId", "Shipment ID", "number", true, true, false),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("LastSyncedAt", "Last Synced At", "date", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Customer.Name", "Customer Name", "string", true, true, true),
                new("Customer.CompanyName", "Customer Company", "string", true, true, true),
                new("SalesOrder.OrderNumber", "Sales Order #", "string", true, true, false),
                new("Shipment.ShipmentNumber", "Shipment #", "string", true, true, false),
            }),

            // ── Leads ─────────────────────────────────────────────────
            new("Leads", "Leads", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("CompanyName", "Company Name", "string", true, true, false),
                new("ContactName", "Contact Name", "string", true, true, false),
                new("Email", "Email", "string", true, false, false),
                new("Phone", "Phone", "string", true, false, false),
                new("Source", "Source", "string", true, true, true),
                new("Status", "Status", "enum", true, true, true),
                new("Notes", "Notes", "string", true, false, false),
                new("FollowUpDate", "Follow-Up Date", "date", true, true, false),
                new("LostReason", "Lost Reason", "string", true, true, true),
                new("ConvertedCustomerId", "Converted Customer ID", "number", true, true, false),
                new("CreatedBy", "Created By ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("ConvertedCustomer.Name", "Converted Customer", "string", true, true, false),
            }),

            // ── Assets ────────────────────────────────────────────────
            new("Assets", "Assets", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("Name", "Name", "string", true, true, false),
                new("AssetType", "Type", "enum", true, true, true),
                new("Location", "Location", "string", true, true, true),
                new("Manufacturer", "Manufacturer", "string", true, true, true),
                new("Model", "Model", "string", true, true, false),
                new("SerialNumber", "Serial Number", "string", true, false, false),
                new("Status", "Status", "enum", true, true, true),
                new("CurrentHours", "Current Hours", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("PhotoFileId", "Photo File ID", "string", true, false, false),
                new("IsCustomerOwned", "Customer Owned", "boolean", true, true, true),
                new("CavityCount", "Cavity Count", "number", true, true, false),
                new("ToolLifeExpectancy", "Tool Life Expectancy", "number", true, true, false),
                new("CurrentShotCount", "Shot Count", "number", true, true, false),
                new("SourceJobId", "Source Job ID", "number", true, true, false),
                new("SourcePartId", "Source Part ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("SourceJob.JobNumber", "Source Job #", "string", true, true, false),
                new("SourceJob.Title", "Source Job Title", "string", true, false, false),
                new("SourcePart.PartNumber", "Source Part #", "string", true, true, false),
            }),

            // ── Purchase Orders ───────────────────────────────────────
            new("PurchaseOrders", "Purchase Orders", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("PONumber", "PO Number", "string", true, true, false),
                new("Status", "Status", "enum", true, true, true),
                new("SubmittedDate", "Submitted Date", "date", true, true, true),
                new("AcknowledgedDate", "Acknowledged Date", "date", true, true, false),
                new("ExpectedDeliveryDate", "Expected Delivery", "date", true, true, true),
                new("ReceivedDate", "Received Date", "date", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("VendorId", "Vendor ID", "number", true, true, false),
                new("JobId", "Job ID", "number", true, true, false),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Vendor.CompanyName", "Vendor Name", "string", true, true, true),
                new("Vendor.ContactName", "Vendor Contact", "string", true, true, false),
                new("Job.JobNumber", "Job Number", "string", true, true, false),
                new("Job.Title", "Job Title", "string", true, false, false),
            }),

            // ── Sales Orders ──────────────────────────────────────────
            new("SalesOrders", "Sales Orders", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("OrderNumber", "Order Number", "string", true, true, false),
                new("Status", "Status", "enum", true, true, true),
                new("ConfirmedDate", "Confirmed Date", "date", true, true, true),
                new("RequestedDeliveryDate", "Requested Delivery", "date", true, true, true),
                new("CustomerPO", "Customer PO", "string", true, true, false),
                new("CreditTerms", "Credit Terms", "enum", true, true, true),
                new("TaxRate", "Tax Rate", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("CustomerId", "Customer ID", "number", true, true, false),
                new("QuoteId", "Quote ID", "number", true, true, false),
                new("ShippingAddressId", "Shipping Address ID", "number", true, true, false),
                new("BillingAddressId", "Billing Address ID", "number", true, true, false),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Customer.Name", "Customer Name", "string", true, true, true),
                new("Customer.CompanyName", "Customer Company", "string", true, true, true),
                new("Quote.QuoteNumber", "Quote #", "string", true, true, false),
            }),

            // ── Quotes ────────────────────────────────────────────────
            new("Quotes", "Quotes", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("QuoteNumber", "Quote Number", "string", true, true, false),
                new("Status", "Status", "enum", true, true, true),
                new("SentDate", "Sent Date", "date", true, true, true),
                new("ExpirationDate", "Expiration Date", "date", true, true, true),
                new("AcceptedDate", "Accepted Date", "date", true, true, false),
                new("TaxRate", "Tax Rate", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("CustomerId", "Customer ID", "number", true, true, false),
                new("ShippingAddressId", "Shipping Address ID", "number", true, true, false),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Customer.Name", "Customer Name", "string", true, true, true),
                new("Customer.CompanyName", "Customer Company", "string", true, true, true),
            }),

            // ── Shipments ─────────────────────────────────────────────
            new("Shipments", "Shipments", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("ShipmentNumber", "Shipment Number", "string", true, true, false),
                new("Status", "Status", "enum", true, true, true),
                new("Carrier", "Carrier", "string", true, true, true),
                new("TrackingNumber", "Tracking Number", "string", true, false, false),
                new("ShippedDate", "Shipped Date", "date", true, true, true),
                new("DeliveredDate", "Delivered Date", "date", true, true, false),
                new("ShippingCost", "Shipping Cost", "number", true, true, false),
                new("Weight", "Weight", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("SalesOrderId", "Sales Order ID", "number", true, true, false),
                new("ShippingAddressId", "Shipping Address ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("SalesOrder.OrderNumber", "Sales Order #", "string", true, true, false),
                new("SalesOrder.CustomerPO", "Customer PO", "string", true, true, false),
                new("SalesOrder.Customer.Name", "Customer Name", "string", true, true, true),
            }),

            // ── Inventory (Bin Contents) ──────────────────────────────
            new("Inventory", "Inventory (Bin Contents)", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("EntityType", "Entity Type", "string", true, true, true),
                new("EntityId", "Entity ID", "number", true, true, false),
                new("Quantity", "Quantity", "number", true, true, false),
                new("ReservedQuantity", "Reserved Qty", "number", true, true, false),
                new("LotNumber", "Lot Number", "string", true, true, true),
                new("Status", "Status", "enum", true, true, true),
                new("PlacedAt", "Placed At", "date", true, true, true),
                new("RemovedAt", "Removed At", "date", true, true, false),
                new("PlacedBy", "Placed By ID", "number", true, true, false),
                new("RemovedBy", "Removed By ID", "number", true, true, false),
                new("JobId", "Job ID", "number", true, true, false),
                new("LocationId", "Location ID", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("Location.Name", "Location Name", "string", true, true, true),
                new("Job.JobNumber", "Job Number", "string", true, true, false),
            }),

            // ── Payments ──────────────────────────────────────────────
            new("Payments", "Payments", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("PaymentNumber", "Payment Number", "string", true, true, false),
                new("Method", "Payment Method", "enum", true, true, true),
                new("Amount", "Amount", "number", true, true, false),
                new("PaymentDate", "Payment Date", "date", true, true, true),
                new("ReferenceNumber", "Reference Number", "string", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("CustomerId", "Customer ID", "number", true, true, false),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("LastSyncedAt", "Last Synced At", "date", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Customer.Name", "Customer Name", "string", true, true, true),
                new("Customer.CompanyName", "Customer Company", "string", true, true, true),
            }),

            // ── Vendors ───────────────────────────────────────────────
            new("Vendors", "Vendors", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("CompanyName", "Company Name", "string", true, true, false),
                new("ContactName", "Contact Name", "string", true, true, false),
                new("Email", "Email", "string", true, true, false),
                new("Phone", "Phone", "string", true, false, false),
                new("Address", "Address", "string", true, false, false),
                new("City", "City", "string", true, true, true),
                new("State", "State", "string", true, true, true),
                new("ZipCode", "Zip Code", "string", true, true, false),
                new("Country", "Country", "string", true, true, true),
                new("PaymentTerms", "Payment Terms", "string", true, true, true),
                new("Notes", "Notes", "string", true, false, false),
                new("IsActive", "Active", "boolean", true, true, true),
                new("ExternalRef", "External Ref", "string", true, true, false),
                new("Provider", "Accounting Provider", "string", true, true, true),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
            }),

            // ── Production Runs ───────────────────────────────────────
            new("ProductionRuns", "Production Runs", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("RunNumber", "Run Number", "string", true, true, false),
                new("Status", "Status", "enum", true, true, true),
                new("TargetQuantity", "Target Qty", "number", true, true, false),
                new("CompletedQuantity", "Completed Qty", "number", true, true, false),
                new("ScrapQuantity", "Scrap Qty", "number", true, true, false),
                new("StartedAt", "Started At", "date", true, true, true),
                new("CompletedAt", "Completed At", "date", true, true, false),
                new("SetupTimeMinutes", "Setup Time (min)", "number", true, true, false),
                new("RunTimeMinutes", "Run Time (min)", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("JobId", "Job ID", "number", true, true, false),
                new("PartId", "Part ID", "number", true, true, false),
                new("OperatorId", "Operator ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Job.JobNumber", "Job Number", "string", true, true, false),
                new("Job.Title", "Job Title", "string", true, false, false),
                new("Part.PartNumber", "Part Number", "string", true, true, false),
                new("Part.Description", "Part Description", "string", true, false, false),
            }),

            // ── Lot Records ───────────────────────────────────────────
            new("LotRecords", "Lot Records", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("LotNumber", "Lot Number", "string", true, true, false),
                new("Quantity", "Quantity", "number", true, true, false),
                new("ExpirationDate", "Expiration Date", "date", true, true, true),
                new("SupplierLotNumber", "Supplier Lot #", "string", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("PartId", "Part ID", "number", true, true, false),
                new("JobId", "Job ID", "number", true, true, false),
                new("ProductionRunId", "Production Run ID", "number", true, true, false),
                new("PurchaseOrderLineId", "PO Line ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Part.PartNumber", "Part Number", "string", true, true, false),
                new("Part.Description", "Part Description", "string", true, false, false),
                new("Job.JobNumber", "Job Number", "string", true, true, false),
                new("Job.Title", "Job Title", "string", true, false, false),
                new("ProductionRun.RunNumber", "Production Run #", "string", true, true, false),
            }),

            // ── QC Inspections ────────────────────────────────────────
            new("QcInspections", "QC Inspections", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("Status", "Status", "string", true, true, true),
                new("LotNumber", "Lot Number", "string", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("CompletedAt", "Completed At", "date", true, true, false),
                new("JobId", "Job ID", "number", true, true, false),
                new("ProductionRunId", "Production Run ID", "number", true, true, false),
                new("TemplateId", "Template ID", "number", true, true, false),
                new("InspectorId", "Inspector ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Job.JobNumber", "Job Number", "string", true, true, false),
                new("Job.Title", "Job Title", "string", true, false, false),
                new("ProductionRun.RunNumber", "Production Run #", "string", true, true, false),
                new("ProductionRun.Part.PartNumber", "Part Number", "string", true, true, false),
                new("Template.Name", "Template Name", "string", true, true, true),
            }),

            // ── Maintenance Schedules ─────────────────────────────────
            new("MaintenanceSchedules", "Maintenance Schedules", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("Title", "Title", "string", true, true, false),
                new("Description", "Description", "string", true, false, false),
                new("IntervalDays", "Interval (Days)", "number", true, true, false),
                new("IntervalHours", "Interval (Hours)", "number", true, true, false),
                new("LastPerformedAt", "Last Performed", "date", true, true, false),
                new("NextDueAt", "Next Due", "date", true, true, true),
                new("IsActive", "Active", "boolean", true, true, true),
                new("AssetId", "Asset ID", "number", true, true, false),
                new("MaintenanceJobId", "Maintenance Job ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Asset.Name", "Asset Name", "string", true, true, true),
                new("Asset.AssetType", "Asset Type", "enum", true, true, true),
                new("Asset.Location", "Asset Location", "string", true, true, true),
                new("MaintenanceJob.JobNumber", "Maintenance Job #", "string", true, true, false),
            }),

            // ── Maintenance Logs ──────────────────────────────────────
            new("MaintenanceLogs", "Maintenance Logs", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("PerformedAt", "Performed At", "date", true, true, true),
                new("PerformedById", "Performed By ID", "number", true, true, false),
                new("HoursAtService", "Hours at Service", "number", true, true, false),
                new("Cost", "Cost", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("MaintenanceScheduleId", "Schedule ID", "number", true, true, false),
                new("Schedule.Title", "Schedule Title", "string", true, true, false),
                new("Schedule.Asset.Name", "Asset Name", "string", true, true, true),
                new("Schedule.Asset.Location", "Asset Location", "string", true, true, true),
            }),

            // ── Downtime Logs ─────────────────────────────────────────
            new("DowntimeLogs", "Downtime Logs", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("StartedAt", "Started At", "date", true, true, true),
                new("EndedAt", "Ended At", "date", true, true, false),
                new("Reason", "Reason", "string", true, true, true),
                new("Resolution", "Resolution", "string", true, false, false),
                new("IsPlanned", "Planned", "boolean", true, true, true),
                new("Notes", "Notes", "string", true, false, false),
                new("AssetId", "Asset ID", "number", true, true, false),
                new("ReportedById", "Reported By ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Asset.Name", "Asset Name", "string", true, true, true),
                new("Asset.AssetType", "Asset Type", "enum", true, true, true),
                new("Asset.Location", "Asset Location", "string", true, true, true),
            }),

            // ── Customer Returns ──────────────────────────────────────
            new("CustomerReturns", "Customer Returns", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("ReturnNumber", "Return Number", "string", true, true, false),
                new("Status", "Status", "enum", true, true, true),
                new("Reason", "Reason", "string", true, true, true),
                new("ReturnDate", "Return Date", "date", true, true, true),
                new("Notes", "Notes", "string", true, false, false),
                new("InspectedAt", "Inspected At", "date", true, true, false),
                new("InspectionNotes", "Inspection Notes", "string", true, false, false),
                new("CustomerId", "Customer ID", "number", true, true, false),
                new("OriginalJobId", "Original Job ID", "number", true, true, false),
                new("ReworkJobId", "Rework Job ID", "number", true, true, false),
                new("InspectedById", "Inspected By ID", "number", true, true, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
                new("Customer.Name", "Customer Name", "string", true, true, true),
                new("Customer.CompanyName", "Customer Company", "string", true, true, true),
                new("OriginalJob.JobNumber", "Original Job #", "string", true, true, false),
                new("OriginalJob.Title", "Original Job Title", "string", true, false, false),
                new("ReworkJob.JobNumber", "Rework Job #", "string", true, true, false),
            }),

            // ── Bin Movements ─────────────────────────────────────────
            new("BinMovements", "Bin Movements", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("EntityType", "Entity Type", "string", true, true, true),
                new("EntityId", "Entity ID", "number", true, true, false),
                new("Quantity", "Quantity", "number", true, true, false),
                new("LotNumber", "Lot Number", "string", true, true, true),
                new("Reason", "Reason", "enum", true, true, true),
                new("MovedAt", "Moved At", "date", true, true, true),
                new("MovedBy", "Moved By ID", "number", true, true, false),
                new("FromLocationId", "From Location ID", "number", true, true, false),
                new("ToLocationId", "To Location ID", "number", true, true, false),
                new("FromLocation.Name", "From Location", "string", true, true, true),
                new("ToLocation.Name", "To Location", "string", true, true, true),
            }),

            // ── Planning Cycles ───────────────────────────────────────
            new("PlanningCycles", "Planning Cycles", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("Name", "Name", "string", true, true, false),
                new("Status", "Status", "enum", true, true, true),
                new("StartDate", "Start Date", "date", true, true, true),
                new("EndDate", "End Date", "date", true, true, true),
                new("DurationDays", "Duration (Days)", "number", true, true, false),
                new("Goals", "Goals", "string", true, false, false),
                new("CreatedAt", "Created At", "date", true, true, true),
                new("UpdatedAt", "Updated At", "date", true, true, false),
            }),

            // ── Invoice Lines ─────────────────────────────────────────
            new("InvoiceLines", "Invoice Lines", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("LineNumber", "Line #", "number", true, true, false),
                new("Description", "Description", "string", true, false, false),
                new("Quantity", "Quantity", "number", true, true, false),
                new("UnitPrice", "Unit Price", "number", true, true, false),
                new("InvoiceId", "Invoice ID", "number", true, true, false),
                new("PartId", "Part ID", "number", true, true, false),
                new("Invoice.InvoiceNumber", "Invoice #", "string", true, true, false),
                new("Invoice.Status", "Invoice Status", "enum", true, true, true),
                new("Invoice.InvoiceDate", "Invoice Date", "date", true, true, true),
                new("Invoice.Customer.Name", "Customer Name", "string", true, true, true),
                new("Part.PartNumber", "Part Number", "string", true, true, false),
                new("Part.Description", "Part Description", "string", true, false, false),
            }),

            // ── Sales Order Lines ─────────────────────────────────────
            new("SalesOrderLines", "Sales Order Lines", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("LineNumber", "Line #", "number", true, true, false),
                new("Description", "Description", "string", true, false, false),
                new("Quantity", "Quantity", "number", true, true, false),
                new("UnitPrice", "Unit Price", "number", true, true, false),
                new("ShippedQuantity", "Shipped Qty", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("SalesOrderId", "Sales Order ID", "number", true, true, false),
                new("PartId", "Part ID", "number", true, true, false),
                new("SalesOrder.OrderNumber", "Sales Order #", "string", true, true, false),
                new("SalesOrder.Status", "Order Status", "enum", true, true, true),
                new("SalesOrder.Customer.Name", "Customer Name", "string", true, true, true),
                new("SalesOrder.CustomerPO", "Customer PO", "string", true, true, false),
                new("Part.PartNumber", "Part Number", "string", true, true, false),
                new("Part.Description", "Part Description", "string", true, false, false),
            }),

            // ── Purchase Order Lines ──────────────────────────────────
            new("PurchaseOrderLines", "Purchase Order Lines", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("Description", "Description", "string", true, false, false),
                new("OrderedQuantity", "Ordered Qty", "number", true, true, false),
                new("ReceivedQuantity", "Received Qty", "number", true, true, false),
                new("UnitPrice", "Unit Price", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("PurchaseOrderId", "PO ID", "number", true, true, false),
                new("PartId", "Part ID", "number", true, true, false),
                new("PurchaseOrder.PONumber", "PO Number", "string", true, true, false),
                new("PurchaseOrder.Status", "PO Status", "enum", true, true, true),
                new("PurchaseOrder.Vendor.CompanyName", "Vendor Name", "string", true, true, true),
                new("PurchaseOrder.ExpectedDeliveryDate", "Expected Delivery", "date", true, true, false),
                new("Part.PartNumber", "Part Number", "string", true, true, false),
                new("Part.Description", "Part Description", "string", true, false, false),
            }),

            // ── Quote Lines ───────────────────────────────────────────
            new("QuoteLines", "Quote Lines", new List<ReportFieldDefinitionModel>
            {
                new("Id", "ID", "number", true, true, false),
                new("LineNumber", "Line #", "number", true, true, false),
                new("Description", "Description", "string", true, false, false),
                new("Quantity", "Quantity", "number", true, true, false),
                new("UnitPrice", "Unit Price", "number", true, true, false),
                new("Notes", "Notes", "string", true, false, false),
                new("QuoteId", "Quote ID", "number", true, true, false),
                new("PartId", "Part ID", "number", true, true, false),
                new("Quote.QuoteNumber", "Quote #", "string", true, true, false),
                new("Quote.Status", "Quote Status", "enum", true, true, true),
                new("Quote.SentDate", "Quote Sent Date", "date", true, true, true),
                new("Quote.Customer.Name", "Customer Name", "string", true, true, true),
                new("Part.PartNumber", "Part Number", "string", true, true, false),
                new("Part.Description", "Part Description", "string", true, false, false),
            }),
        };

        return Task.FromResult(entities);
    }
}
