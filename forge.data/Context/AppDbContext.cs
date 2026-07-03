using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Entities.Calendar;
using Forge.Core.Entities.Compliance;
using Forge.Core.Entities.Regulatory;
using Forge.Core.Interfaces;

namespace Forge.Data.Context;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>, IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    private readonly IClock _clock;
    private bool _isCapturingAudit;

    /// <summary>
    /// Set by middleware to identify the current user for automatic audit logging.
    /// </summary>
    public int? CurrentUserId { get; set; }

    /// <summary>
    /// Set by middleware. Captured into system-wide audit_log_entries.ip_address.
    /// </summary>
    public string? CurrentIpAddress { get; set; }

    /// <summary>
    /// Set by middleware. Captured into system-wide audit_log_entries.user_agent.
    /// </summary>
    public string? CurrentUserAgent { get; set; }

    /// <summary>
    /// When true, automatic audit logging is suppressed (e.g., during seed operations).
    /// </summary>
    public bool SuppressAudit { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, IClock clock) : base(options)
    {
        _clock = clock;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Accounting GL (Phase 0): enforce posted-ledger immutability in software
        // (§2, §4, §5.2). Idempotent — only add the interceptor when DI hasn't
        // already registered it via AddDbContext, so production (Program.cs) and
        // InMemory unit tests both get it without double-registration.
        var extension = optionsBuilder.Options
            .FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>();
        var alreadyRegistered = extension?.Interceptors?
            .OfType<Interceptors.LedgerImmutabilityInterceptor>().Any() ?? false;
        if (!alreadyRegistered)
            optionsBuilder.AddInterceptors(new Interceptors.LedgerImmutabilityInterceptor());
    }

    public DbSet<TrackType> TrackTypes => Set<TrackType>();
    public DbSet<JobStage> JobStages => Set<JobStage>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobSubtask> JobSubtasks => Set<JobSubtask>();
    public DbSet<JobLink> JobLinks => Set<JobLink>();
    public DbSet<JobActivityLog> JobActivityLogs => Set<JobActivityLog>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerSegment> CustomerSegments => Set<CustomerSegment>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactOutreachPreferences> ContactOutreachPreferences => Set<ContactOutreachPreferences>();
    public DbSet<CustomerPortalAccess> CustomerPortalAccesses => Set<CustomerPortalAccess>();
    public DbSet<ContactInteraction> ContactInteractions => Set<ContactInteraction>();
    public DbSet<CommunicationSyncConfig> CommunicationSyncConfigs => Set<CommunicationSyncConfig>();
    public DbSet<OAuthStateToken> OAuthStateTokens => Set<OAuthStateToken>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<PartPrice> PartPrices => Set<PartPrice>();
    public DbSet<BOMLine> BOMLines => Set<BOMLine>();

    // Phase 3 H4 / WU-20 — BOM revision history.
    public DbSet<BomRevision> BomRevisions => Set<BomRevision>();
    public DbSet<BomRevisionLine> BomRevisionLines => Set<BomRevisionLine>();
    public DbSet<ReferenceData> ReferenceData => Set<ReferenceData>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<SyncQueueEntry> SyncQueueEntries => Set<SyncQueueEntry>();
    public DbSet<IntegrationOutboxEntry> IntegrationOutboxEntries => Set<IntegrationOutboxEntry>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<BinContent> BinContents => Set<BinContent>();
    public DbSet<BinMovement> BinMovements => Set<BinMovement>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadOutreachPreferences> LeadOutreachPreferences => Set<LeadOutreachPreferences>();
    public DbSet<OutreachCampaign> OutreachCampaigns => Set<OutreachCampaign>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<IcpRubric> IcpRubrics => Set<IcpRubric>();
    public DbSet<IcpDimension> IcpDimensions => Set<IcpDimension>();
    public DbSet<AssignmentRule> AssignmentRules => Set<AssignmentRule>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountContact> AccountContacts => Set<AccountContact>();
    public DbSet<SampleShipment> SampleShipments => Set<SampleShipment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<ClockEvent> ClockEvents => Set<ClockEvent>();
    public DbSet<TimeCorrectionLog> TimeCorrectionLogs => Set<TimeCorrectionLog>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<TerminologyEntry> TerminologyEntries => Set<TerminologyEntry>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<PlanningCycle> PlanningCycles => Set<PlanningCycle>();
    public DbSet<PlanningCycleEntry> PlanningCycleEntries => Set<PlanningCycleEntry>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    // Pillar 3 — vendor-part intersection + tiered pricing.
    public DbSet<VendorPart> VendorParts => Set<VendorPart>();
    public DbSet<VendorPartPriceTier> VendorPartPriceTiers => Set<VendorPartPriceTier>();
    public DbSet<PartPurchaseUnit> PartPurchaseUnits => Set<PartPurchaseUnit>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderRelease> PurchaseOrderReleases => Set<PurchaseOrderRelease>();
    public DbSet<ReceivingRecord> ReceivingRecords => Set<ReceivingRecord>();
    // Bought-parts effort PR3 — landed cost foundation. Tariff rates are
    // SCD-2 keyed on (HtsCode, CountryOfOrigin) and consumed by the
    // landed-cost resolver. Today the table ships empty.
    public DbSet<TariffRate> TariffRates => Set<TariffRate>();

    // RFQ (Request for Quote)
    public DbSet<RequestForQuote> RequestForQuotes => Set<RequestForQuote>();
    public DbSet<RfqVendorResponse> RfqVendorResponses => Set<RfqVendorResponse>();
    public DbSet<JobPart> JobParts => Set<JobPart>();
    public DbSet<JobNote> JobNotes => Set<JobNote>();
    public DbSet<EntityNote> EntityNotes => Set<EntityNote>();

    // Order Management
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();
    public DbSet<Carrier> Carriers => Set<Carrier>();

    // Standalone Financial (⚡ Accounting Boundary)
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentApplication> PaymentApplications => Set<PaymentApplication>();

    // AP sub-ledger (⚡ Accounting Boundary) — Phase-2 VendorBill / VendorPayment
    public DbSet<VendorBill> VendorBills => Set<VendorBill>();
    public DbSet<VendorBillLine> VendorBillLines => Set<VendorBillLine>();
    public DbSet<VendorPayment> VendorPayments => Set<VendorPayment>();
    public DbSet<VendorPaymentApplication> VendorPaymentApplications => Set<VendorPaymentApplication>();
    // Electronic bank/ACH submission tracking (generic SourceType/SourceId, wired to VendorPayment today)
    public DbSet<PaymentTransmission> PaymentTransmissions => Set<PaymentTransmission>();
    // BANK-002 Phase A — NACHA origination (⚡ Banking Boundary): encrypted vendor ACH
    // destinations (dual-control + prenote) and the payment batches / entry lines that
    // become the generated NACHA files (release-by-portal-upload = the SoD step).
    public DbSet<VendorBankAccount> VendorBankAccounts => Set<VendorBankAccount>();
    public DbSet<PaymentBatch> PaymentBatches => Set<PaymentBatch>();
    public DbSet<PaymentBatchItem> PaymentBatchItems => Set<PaymentBatchItem>();

    // Pricing
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListEntry> PriceListEntries => Set<PriceListEntry>();
    public DbSet<RecurringOrder> RecurringOrders => Set<RecurringOrder>();
    public DbSet<RecurringOrderLine> RecurringOrderLines => Set<RecurringOrderLine>();
    public DbSet<CustomerReturn> CustomerReturns => Set<CustomerReturn>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();
    public DbSet<ChatRoomMember> ChatRoomMembers => Set<ChatRoomMember>();
    public DbSet<ChatMessageMention> ChatMessageMentions => Set<ChatMessageMention>();

    // Asset Maintenance
    public DbSet<MaintenanceSchedule> MaintenanceSchedules => Set<MaintenanceSchedule>();
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();

    // Production
    public DbSet<ProductionRun> ProductionRuns => Set<ProductionRun>();

    // Part Revisions
    public DbSet<PartRevision> PartRevisions => Set<PartRevision>();

    // Downtime
    public DbSet<DowntimeLog> DowntimeLogs => Set<DowntimeLog>();

    // Sales Tax
    public DbSet<SalesTaxRate> SalesTaxRates => Set<SalesTaxRate>();

    // Quality & Traceability
    public DbSet<QcChecklistTemplate> QcChecklistTemplates => Set<QcChecklistTemplate>();
    public DbSet<QcChecklistItem> QcChecklistItems => Set<QcChecklistItem>();
    public DbSet<QcInspection> QcInspections => Set<QcInspection>();
    public DbSet<QcInspectionResult> QcInspectionResults => Set<QcInspectionResult>();
    public DbSet<LotRecord> LotRecords => Set<LotRecord>();

    // Operations
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<OperationMaterial> OperationMaterials => Set<OperationMaterial>();

    // Cycle Counts
    public DbSet<CycleCount> CycleCounts => Set<CycleCount>();
    public DbSet<CycleCountLine> CycleCountLines => Set<CycleCountLine>();

    // Reservations
    public DbSet<Reservation> Reservations => Set<Reservation>();

    // AI / RAG
    public DbSet<DocumentEmbedding> DocumentEmbeddings => Set<DocumentEmbedding>();
    public DbSet<AiAssistant> AiAssistants => Set<AiAssistant>();

    // Status Tracking
    public DbSet<StatusEntry> StatusEntries => Set<StatusEntry>();

    // Audit
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    // Shipment Packages
    public DbSet<ShipmentPackage> ShipmentPackages => Set<ShipmentPackage>();

    // Scheduled Tasks
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();

    // Scan Identifiers (NFC/RFID/Barcode)
    public DbSet<UserScanIdentifier> UserScanIdentifiers => Set<UserScanIdentifier>();

    // Report Builder
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();

    // Document Approval Workflow
    public DbSet<ControlledDocument> ControlledDocuments => Set<ControlledDocument>();
    public DbSet<DocumentRevision> DocumentRevisions => Set<DocumentRevision>();

    // General versioned-document store (lightweight, multi-entity) — e.g. wrapped shipping labels.
    public DbSet<DocumentSet> DocumentSets => Set<DocumentSet>();
    public DbSet<DocumentSetVersion> DocumentSetVersions => Set<DocumentSetVersion>();
    public DbSet<DocumentSetLink> DocumentSetLinks => Set<DocumentSetLink>();

    // Outbound Webhooks
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    // Teams & Kiosk Terminals
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<KioskTerminal> KioskTerminals => Set<KioskTerminal>();

    // Central Barcode Registry
    public DbSet<Barcode> Barcodes => Set<Barcode>();

    // Employee Profiles
    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();

    // Company Locations
    public DbSet<CompanyLocation> CompanyLocations => Set<CompanyLocation>();

    // Working calendars (PR 1 — bought-parts effort) — drives every business-day
    // helper in the system. Tenant has a default; CompanyLocation may override.
    public DbSet<WorkingCalendar> WorkingCalendars => Set<WorkingCalendar>();
    public DbSet<Holiday> Holidays => Set<Holiday>();

    // Compliance & Document Signing
    public DbSet<ComplianceFormTemplate> ComplianceFormTemplates => Set<ComplianceFormTemplate>();
    public DbSet<ComplianceFormSubmission> ComplianceFormSubmissions => Set<ComplianceFormSubmission>();
    public DbSet<IdentityDocument> IdentityDocuments => Set<IdentityDocument>();
    public DbSet<FormDefinitionVersion> FormDefinitionVersions => Set<FormDefinitionVersion>();

    // Payroll
    public DbSet<PayStub> PayStubs => Set<PayStub>();
    public DbSet<PayStubDeduction> PayStubDeductions => Set<PayStubDeduction>();
    public DbSet<TaxDocument> TaxDocuments => Set<TaxDocument>();

    // Replenishment
    public DbSet<ReorderSuggestion> ReorderSuggestions => Set<ReorderSuggestion>();

    // MRP
    public DbSet<MrpRun> MrpRuns => Set<MrpRun>();
    public DbSet<MrpDemand> MrpDemands => Set<MrpDemand>();
    public DbSet<MrpSupply> MrpSupplies => Set<MrpSupply>();
    public DbSet<MrpPlannedOrder> MrpPlannedOrders => Set<MrpPlannedOrder>();
    public DbSet<MrpException> MrpExceptions => Set<MrpException>();
    public DbSet<MasterSchedule> MasterSchedules => Set<MasterSchedule>();
    public DbSet<MasterScheduleLine> MasterScheduleLines => Set<MasterScheduleLine>();
    public DbSet<DemandForecast> DemandForecasts => Set<DemandForecast>();
    public DbSet<ForecastOverride> ForecastOverrides => Set<ForecastOverride>();

    // Job Costing
    public DbSet<LaborRate> LaborRates => Set<LaborRate>();
    public DbSet<MaterialIssue> MaterialIssues => Set<MaterialIssue>();

    // SPC
    public DbSet<SpcCharacteristic> SpcCharacteristics => Set<SpcCharacteristic>();
    public DbSet<SpcMeasurement> SpcMeasurements => Set<SpcMeasurement>();
    public DbSet<SpcControlLimit> SpcControlLimits => Set<SpcControlLimit>();
    public DbSet<SpcOocEvent> SpcOocEvents => Set<SpcOocEvent>();

    // Subcontracting
    public DbSet<SubcontractOrder> SubcontractOrders => Set<SubcontractOrder>();

    // CAPA / NCR
    public DbSet<NonConformance> NonConformances => Set<NonConformance>();
    public DbSet<CorrectiveAction> CorrectiveActions => Set<CorrectiveAction>();
    public DbSet<CapaTask> CapaTasks => Set<CapaTask>();

    // Scheduling
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<WorkCenterCalendar> WorkCenterCalendars => Set<WorkCenterCalendar>();
    public DbSet<WorkCenterQualification> WorkCenterQualifications => Set<WorkCenterQualification>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<WorkCenterShift> WorkCenterShifts => Set<WorkCenterShift>();
    public DbSet<ScheduledOperation> ScheduledOperations => Set<ScheduledOperation>();
    public DbSet<ScheduleRun> ScheduleRuns => Set<ScheduleRun>();
    public DbSet<ScheduleMilestone> ScheduleMilestones => Set<ScheduleMilestone>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<OvertimeRule> OvertimeRules => Set<OvertimeRule>();
    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<ReviewCycle> ReviewCycles => Set<ReviewCycle>();
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();

    // Training
    public DbSet<TrainingModule> TrainingModules => Set<TrainingModule>();
    public DbSet<TrainingPath> TrainingPaths => Set<TrainingPath>();
    public DbSet<TrainingPathModule> TrainingPathModules => Set<TrainingPathModule>();
    public DbSet<TrainingPathEnrollment> TrainingPathEnrollments => Set<TrainingPathEnrollment>();
    public DbSet<TrainingProgress> TrainingProgress => Set<TrainingProgress>();
    public DbSet<TrainingModuleTranslation> TrainingModuleTranslations => Set<TrainingModuleTranslation>();
    public DbSet<TrainingPathTranslation> TrainingPathTranslations => Set<TrainingPathTranslation>();

    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventAttendee> EventAttendees => Set<EventAttendee>();

    // Calendar taxonomy (compliance-calendar A-1): Event → CalendarEventType → CalendarSuperGroup
    public DbSet<CalendarSuperGroup> CalendarSuperGroups => Set<CalendarSuperGroup>();
    public DbSet<CalendarEventType> CalendarEventTypes => Set<CalendarEventType>();
    public DbSet<CalendarSuperGroupRoleVisibility> CalendarSuperGroupRoleVisibilities => Set<CalendarSuperGroupRoleVisibility>();
    public DbSet<CalendarSavedView> CalendarSavedViews => Set<CalendarSavedView>();

    // Compliance profiles (regulated-parts-safety C-1)
    public DbSet<ComplianceProfile> ComplianceProfiles => Set<ComplianceProfile>();
    public DbSet<ComplianceFieldRule> ComplianceFieldRules => Set<ComplianceFieldRule>();

    // Lot genealogy edges (regulated-parts-safety C-2)
    public DbSet<LotConsumption> LotConsumptions => Set<LotConsumption>();

    // Part SDS documents (regulated-parts-safety C-3)
    public DbSet<PartSafetyDataSheet> PartSafetyDataSheets => Set<PartSafetyDataSheet>();

    // Regulatory Watchtower (cluster B)
    public DbSet<RegulatorySource> RegulatorySources => Set<RegulatorySource>();
    public DbSet<RegulatoryChangeProposal> RegulatoryChangeProposals => Set<RegulatoryChangeProposal>();

    // AI provenance markers (ai-fleet-orchestration D)
    public DbSet<AiProvenance> AiProvenances => Set<AiProvenance>();

    // EDI
    public DbSet<EdiTradingPartner> EdiTradingPartners => Set<EdiTradingPartner>();
    public DbSet<EdiTransaction> EdiTransactions => Set<EdiTransaction>();
    public DbSet<EdiMapping> EdiMappings => Set<EdiMapping>();

    // MFA
    public DbSet<UserMfaDevice> UserMfaDevices => Set<UserMfaDevice>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();

    // User Integrations
    public DbSet<UserIntegration> UserIntegrations => Set<UserIntegration>();

    // Units of Measure
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<UomConversion> UomConversions => Set<UomConversion>();

    // Approval Workflows
    public DbSet<ApprovalWorkflow> ApprovalWorkflows => Set<ApprovalWorkflow>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();

    // Vendor Scorecards
    public DbSet<VendorScorecard> VendorScorecards => Set<VendorScorecard>();

    // Part Alternates
    public DbSet<PartAlternate> PartAlternates => Set<PartAlternate>();

    // Engineering Change Orders
    public DbSet<EngineeringChangeOrder> EngineeringChangeOrders => Set<EngineeringChangeOrder>();
    public DbSet<EcoAffectedItem> EcoAffectedItems => Set<EcoAffectedItem>();

    // Serial Number Tracking
    public DbSet<SerialNumber> SerialNumbers => Set<SerialNumber>();
    public DbSet<SerialHistory> SerialHistories => Set<SerialHistory>();

    // Gage / Calibration
    public DbSet<Gage> Gages => Set<Gage>();
    public DbSet<CalibrationRecord> CalibrationRecords => Set<CalibrationRecord>();

    // CPQ (Configure, Price, Quote)
    public DbSet<ProductConfigurator> ProductConfigurators => Set<ProductConfigurator>();
    public DbSet<ConfiguratorOption> ConfiguratorOptions => Set<ConfiguratorOption>();
    public DbSet<ProductConfiguration> ProductConfigurations => Set<ProductConfiguration>();

    // Multi-Plant
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<InterPlantTransfer> InterPlantTransfers => Set<InterPlantTransfer>();
    public DbSet<InterPlantTransferLine> InterPlantTransferLines => Set<InterPlantTransferLine>();

    // Multi-Currency
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    // Multi-Language
    public DbSet<TranslatedLabel> TranslatedLabels => Set<TranslatedLabel>();
    public DbSet<SupportedLanguage> SupportedLanguages => Set<SupportedLanguage>();

    // IoT / Machine Integration
    public DbSet<MachineConnection> MachineConnections => Set<MachineConnection>();
    public DbSet<MachineTag> MachineTags => Set<MachineTag>();
    public DbSet<MachineDataPoint> MachineDataPoints => Set<MachineDataPoint>();

    // E-Commerce
    public DbSet<ECommerceIntegration> ECommerceIntegrations => Set<ECommerceIntegration>();
    public DbSet<ECommerceOrderSync> ECommerceOrderSyncs => Set<ECommerceOrderSync>();

    // Andon Board
    public DbSet<AndonAlert> AndonAlerts => Set<AndonAlert>();

    // BI API Keys
    public DbSet<BiApiKey> BiApiKeys => Set<BiApiKey>();

    // System (user-bound) API Keys — used by headless integrations that
    // authenticate AS a real ApplicationUser (vs the unbound BiApiKey
    // synthetic-role pattern). See SystemApiKey docstring.
    public DbSet<SystemApiKey> SystemApiKeys => Set<SystemApiKey>();

    // Consignment Inventory
    public DbSet<ConsignmentAgreement> ConsignmentAgreements => Set<ConsignmentAgreement>();
    public DbSet<ConsignmentTransaction> ConsignmentTransactions => Set<ConsignmentTransaction>();

    // ABC Classification
    public DbSet<AbcClassificationRun> AbcClassificationRuns => Set<AbcClassificationRun>();
    public DbSet<AbcClassification> AbcClassifications => Set<AbcClassification>();

    // Wave Planning / Pick Lists
    public DbSet<PickWave> PickWaves => Set<PickWave>();
    public DbSet<PickLine> PickLines => Set<PickLine>();

    // Kanban Replenishment
    public DbSet<KanbanCard> KanbanCards => Set<KanbanCard>();
    public DbSet<KanbanTriggerLog> KanbanTriggerLogs => Set<KanbanTriggerLog>();

    // Project Accounting / WBS
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WbsElement> WbsElements => Set<WbsElement>();
    public DbSet<WbsCostEntry> WbsCostEntries => Set<WbsCostEntry>();

    // PPAP
    public DbSet<PpapSubmission> PpapSubmissions => Set<PpapSubmission>();
    public DbSet<PpapElement> PpapElements => Set<PpapElement>();

    // FMEA
    public DbSet<FmeaAnalysis> FmeaAnalyses => Set<FmeaAnalysis>();
    public DbSet<FmeaItem> FmeaItems => Set<FmeaItem>();

    // Predictive Maintenance
    public DbSet<MaintenancePrediction> MaintenancePredictions => Set<MaintenancePrediction>();
    public DbSet<MlModel> MlModels => Set<MlModel>();
    public DbSet<PredictionFeedback> PredictionFeedbacks => Set<PredictionFeedback>();

    // Receiving Inspection
    public DbSet<ReceivingInspection> ReceivingInspections => Set<ReceivingInspection>();

    // Credit Hold Audit Trail
    public DbSet<CreditHold> CreditHolds => Set<CreditHold>();

    // Announcements
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementAcknowledgment> AnnouncementAcknowledgments => Set<AnnouncementAcknowledgment>();
    public DbSet<AnnouncementTeam> AnnouncementTeams => Set<AnnouncementTeam>();
    public DbSet<AnnouncementTemplate> AnnouncementTemplates => Set<AnnouncementTemplate>();

    // Follow-Up Tasks
    public DbSet<FollowUpTask> FollowUpTasks => Set<FollowUpTask>();

    // Auto-PO Suggestions
    public DbSet<AutoPoSuggestion> AutoPoSuggestions => Set<AutoPoSuggestion>();

    // Domain Event Failures
    public DbSet<DomainEventFailure> DomainEventFailures => Set<DomainEventFailure>();

    // User Scan Devices
    public DbSet<UserScanDevice> UserScanDevices => Set<UserScanDevice>();

    // Scanner Action Logs
    public DbSet<ScanActionLog> ScanActionLogs => Set<ScanActionLog>();

    // Training Scan Logs
    public DbSet<TrainingScanLog> TrainingScanLogs => Set<TrainingScanLog>();

    // Role Templates (Phase 3 / WU-06 / C1) — tenant-configurable rollup roles.
    public DbSet<RoleTemplate> RoleTemplates => Set<RoleTemplate>();

    // Capabilities (Phase 4 Phase-A) — capability gating storage.
    public DbSet<Capability> Capabilities => Set<Capability>();
    public DbSet<CapabilityConfig> CapabilityConfigs => Set<CapabilityConfig>();

    // Discovery (Phase 4 Phase-F) — wizard run audit trail.
    public DbSet<DiscoveryRun> DiscoveryRuns => Set<DiscoveryRun>();

    // Workflow Pattern Phase 2 — workflow runtime + readiness validators +
    // costing substrate (D5 schema landed empty; populated in a later phase).
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<WorkflowRunEntity> WorkflowRunEntities => Set<WorkflowRunEntity>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<EntityReadinessValidator> EntityReadinessValidators => Set<EntityReadinessValidator>();
    public DbSet<EntityCapabilityRequirement> EntityCapabilityRequirements => Set<EntityCapabilityRequirement>();
    public DbSet<CostingProfile> CostingProfiles => Set<CostingProfile>();
    public DbSet<CostCalculation> CostCalculations => Set<CostCalculation>();
    public DbSet<CostCalculationInputs> CostCalculationInputs => Set<CostCalculationInputs>();

    // Pro Services rollout — cloud storage substrate (D9 / Artifact 4 §3.2-3.4).
    public DbSet<CloudStorageProvider> CloudStorageProviders => Set<CloudStorageProvider>();
    public DbSet<UserCloudStorageLink> UserCloudStorageLinks => Set<UserCloudStorageLink>();
    public DbSet<EntityCloudLink> EntityCloudLinks => Set<EntityCloudLink>();

    // Pro Services rollout — Deliverable entity (Artifact 4 §4.6, gated by CAP-O2C-DELIVERABLE).
    public DbSet<Deliverable> Deliverables => Set<Deliverable>();

    // Accounting GL foundation (Phase 0, acct_* schema; CAP-ACCT-FULLGL — OFF/dark).
    // Ledger entities derive from BaseEntity (or carry a long Id) so they are
    // exempt from the global soft-delete query filter (§2/§5.6).
    public DbSet<Forge.Core.Entities.Accounting.Book> Books => Set<Forge.Core.Entities.Accounting.Book>();
    public DbSet<Forge.Core.Entities.Accounting.GlAccount> GlAccounts => Set<Forge.Core.Entities.Accounting.GlAccount>();
    public DbSet<Forge.Core.Entities.Accounting.CostCenter> CostCenters => Set<Forge.Core.Entities.Accounting.CostCenter>();
    public DbSet<Forge.Core.Entities.Accounting.FiscalYear> FiscalYears => Set<Forge.Core.Entities.Accounting.FiscalYear>();
    public DbSet<Forge.Core.Entities.Accounting.FiscalPeriod> FiscalPeriods => Set<Forge.Core.Entities.Accounting.FiscalPeriod>();
    public DbSet<Forge.Core.Entities.Accounting.JournalEntry> JournalEntries => Set<Forge.Core.Entities.Accounting.JournalEntry>();
    public DbSet<Forge.Core.Entities.Accounting.JournalLine> JournalLines => Set<Forge.Core.Entities.Accounting.JournalLine>();
    public DbSet<Forge.Core.Entities.Accounting.AccountDeterminationRule> AccountDeterminationRules => Set<Forge.Core.Entities.Accounting.AccountDeterminationRule>();
    public DbSet<Forge.Core.Entities.Accounting.AcctNumberSequence> AcctNumberSequences => Set<Forge.Core.Entities.Accounting.AcctNumberSequence>();
    public DbSet<Forge.Core.Entities.Accounting.LedgerBalance> LedgerBalances => Set<Forge.Core.Entities.Accounting.LedgerBalance>();
    public DbSet<Forge.Core.Entities.Accounting.BankReconciliation> BankReconciliations => Set<Forge.Core.Entities.Accounting.BankReconciliation>();
    public DbSet<Forge.Core.Entities.Accounting.BankReconciliationItem> BankReconciliationItems => Set<Forge.Core.Entities.Accounting.BankReconciliationItem>();
    public DbSet<Forge.Core.Entities.Accounting.JournalTemplate> JournalTemplates => Set<Forge.Core.Entities.Accounting.JournalTemplate>();
    public DbSet<Forge.Core.Entities.Accounting.JournalTemplateLine> JournalTemplateLines => Set<Forge.Core.Entities.Accounting.JournalTemplateLine>();
    public DbSet<Forge.Core.Entities.Accounting.InventoryValuation> InventoryValuations => Set<Forge.Core.Entities.Accounting.InventoryValuation>();
    public DbSet<Forge.Core.Entities.Accounting.FixedAsset> FixedAssets => Set<Forge.Core.Entities.Accounting.FixedAsset>();
    public DbSet<Forge.Core.Entities.Accounting.DepreciationEntry> DepreciationEntries => Set<Forge.Core.Entities.Accounting.DepreciationEntry>();
    public DbSet<Forge.Core.Entities.Accounting.PayRun> PayRuns => Set<Forge.Core.Entities.Accounting.PayRun>();

    // AR-002/AP-001 — per-document open-item sub-ledger (maintained at posting time by the
    // AR/AP posting services, same transaction as the control-account journal; the aging +
    // control reconciliation read from these).
    public DbSet<Forge.Core.Entities.Accounting.ArOpenItem> ArOpenItems => Set<Forge.Core.Entities.Accounting.ArOpenItem>();
    public DbSet<Forge.Core.Entities.Accounting.ApOpenItem> ApOpenItems => Set<Forge.Core.Entities.Accounting.ApOpenItem>();

    // QB-001 — one-way QBO journal-summary push for the CPA (CAP-ACCT-QBO-EXPORT, default OFF).
    public DbSet<Forge.Core.Entities.Accounting.QboAccountMap> QboAccountMaps => Set<Forge.Core.Entities.Accounting.QboAccountMap>();
    public DbSet<Forge.Core.Entities.Accounting.QboExportLog> QboExportLogs => Set<Forge.Core.Entities.Accounting.QboExportLog>();

    // BANK-001 — bank statement import staging (OFX/CSV → dedupe → auto-match → confirm clears
    // the line in the open bank reconciliation; settlement confirmation for in-transit cash).
    public DbSet<Forge.Core.Entities.Accounting.BankStatementImport> BankStatementImports => Set<Forge.Core.Entities.Accounting.BankStatementImport>();
    public DbSet<Forge.Core.Entities.Accounting.BankStatementLine> BankStatementLines => Set<Forge.Core.Entities.Accounting.BankStatementLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Apply snake_case naming convention for all tables and columns
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));

            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName()!));

            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));

            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }

        // Identity FK constraint names: the deployed schema (created by an older EF version) names
        // these AspNet* → asp_net_users FKs with a DOUBLE underscore before the principal table;
        // current EF emits a single underscore. Force the legacy names so the squashed InitialBaseline
        // reproduces the deployed schema exactly AND the boot reconciler (which verifies FKs by name)
        // can confirm the baseline is present on existing installs. See docs/db/MIGRATION_SQUASH_PLAN.md §3.3.
        var legacyIdentityFkNames = new Dictionary<string, string>
        {
            ["fk_asp_net_user_claims_asp_net_users_user_id"] = "fk_asp_net_user_claims__asp_net_users_user_id",
            ["fk_asp_net_user_logins_asp_net_users_user_id"] = "fk_asp_net_user_logins__asp_net_users_user_id",
            ["fk_asp_net_user_roles_asp_net_users_user_id"] = "fk_asp_net_user_roles__asp_net_users_user_id",
            ["fk_asp_net_user_tokens_asp_net_users_user_id"] = "fk_asp_net_user_tokens__asp_net_users_user_id",
        };
        foreach (var entityType in builder.Model.GetEntityTypes())
            foreach (var fk in entityType.GetForeignKeys())
            {
                var name = fk.GetConstraintName();
                if (name != null && legacyIdentityFkNames.TryGetValue(name, out var legacy))
                    fk.SetConstraintName(legacy);
            }

        // Global query filter for soft delete on all BaseAuditableEntity types
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(BaseAuditableEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var deletedAtProperty = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseAuditableEntity.DeletedAt));
            var nullConstant = System.Linq.Expressions.Expression.Constant(null, typeof(DateTimeOffset?));
            var filter = System.Linq.Expressions.Expression.Equal(deletedAtProperty, nullConstant);
            var lambda = System.Linq.Expressions.Expression.Lambda(filter, parameter);

            builder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    public override int SaveChanges()
    {
        NormalizeDateTimes();
        SetTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        NormalizeDateTimes();
        SetTimestamps();

        if (_isCapturingAudit || SuppressAudit)
            return await base.SaveChangesAsync(cancellationToken);

        _isCapturingAudit = true;
        try
        {
            var (readyLogs, pendingAdded, readyAuditLogs, pendingAuditAdded) = CaptureAuditEntries();

            // Add per-entity activity logs and system-wide audit-log entries for
            // Modified/Deleted entities (already have IDs).
            foreach (var log in readyLogs)
                ActivityLogs.Add(log);
            foreach (var auditLog in readyAuditLogs)
                AuditLogEntries.Add(auditLog);

            var result = await base.SaveChangesAsync(cancellationToken);

            // Now handle Added entities — IDs are populated after first save.
            if (pendingAdded.Count > 0 || pendingAuditAdded.Count > 0)
            {
                foreach (var (entity, log) in pendingAdded)
                {
                    log.EntityId = entity.Id;
                    ActivityLogs.Add(log);
                }
                foreach (var (entity, auditLog) in pendingAuditAdded)
                {
                    auditLog.EntityId = entity.Id;
                    AuditLogEntries.Add(auditLog);
                }
                await base.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            _isCapturingAudit = false;
        }
    }

    // Entity types excluded from automatic audit logging
    private static readonly HashSet<Type> AuditExcludedTypes = new()
    {
        typeof(ActivityLog),
        typeof(JobActivityLog),
        typeof(UserPreference),
        typeof(SyncQueueEntry),
        typeof(Notification),
        typeof(DocumentEmbedding),
        typeof(ChatMessage),
        typeof(ChatRoomMember),
        typeof(ChatMessageMention),
        typeof(MachineDataPoint),
        typeof(SpcMeasurement),
        typeof(TrainingProgress),
        typeof(AnnouncementAcknowledgment),
        typeof(WebhookDelivery),
        typeof(AuditLogEntry),
        typeof(ScanActionLog),
        typeof(TrainingScanLog),
        // Phase 4 — capability mutations write to AuditLogEntry via
        // ISystemAuditWriter; per-entity ActivityLog rows would duplicate.
        typeof(Capability),
        typeof(CapabilityConfig),
        // Phase 4 Phase-F — DiscoveryRun is itself an audit row. The bulk
        // toggle it triggers writes its own AuditLogEntry per capability.
        typeof(DiscoveryRun),
    };

    // Properties excluded from field-level change tracking
    private static readonly HashSet<string> AuditExcludedProperties = new()
    {
        nameof(BaseAuditableEntity.CreatedAt),
        nameof(BaseAuditableEntity.UpdatedAt),
        nameof(BaseAuditableEntity.DeletedAt),
        nameof(BaseAuditableEntity.DeletedBy),
        nameof(BaseEntity.Id),
        // Optimistic-locking concurrency token (IConcurrencyVersioned). Bumped
        // on every Modified save, so it would otherwise emit a meaningless
        // "Version changed from N to N+1" history row alongside every real
        // edit. It's plumbing, not a user-facing change — never log it.
        nameof(IConcurrencyVersioned.Version),
    };

    private (
        List<ActivityLog> readyLogs,
        List<(BaseAuditableEntity entity, ActivityLog log)> pendingAdded,
        List<AuditLogEntry> readyAuditLogs,
        List<(BaseAuditableEntity entity, AuditLogEntry auditLog)> pendingAuditAdded
    ) CaptureAuditEntries()
    {
        var readyLogs = new List<ActivityLog>();
        var pendingAdded = new List<(BaseAuditableEntity, ActivityLog)>();
        var readyAuditLogs = new List<AuditLogEntry>();
        var pendingAuditAdded = new List<(BaseAuditableEntity, AuditLogEntry)>();

        var userId = GetCurrentUserId();
        var ipAddress = CurrentIpAddress;
        var userAgent = CurrentUserAgent;
        var now = _clock.UtcNow;

        // Per-request collected field changes (for synthesised system-wide
        // "<Entity>Updated" rows that summarise the diff in one entry).
        // entityType+entityId -> list of (FieldName, OldValue, NewValue)
        var modifiedDiffs = new Dictionary<(string entityType, int entityId), List<(string Field, string Old, string New)>>();

        foreach (var entry in ChangeTracker.Entries<BaseAuditableEntity>())
        {
            if (AuditExcludedTypes.Contains(entry.Entity.GetType()))
                continue;

            var entityType = entry.Entity.GetType().Name;

            switch (entry.State)
            {
                case EntityState.Added:
                {
                    var log = new ActivityLog
                    {
                        EntityType = entityType,
                        EntityId = 0, // populated after save
                        UserId = userId,
                        Action = "Created",
                        Description = $"{FormatEntityTypeName(entityType)} created",
                        CreatedAt = now,
                    };
                    pendingAdded.Add((entry.Entity, log));

                    // System-wide audit log row (only if we have an authenticated actor;
                    // seed/system operations skip the audit log).
                    if (userId is int auditUid)
                    {
                        var auditLog = new AuditLogEntry
                        {
                            UserId = auditUid,
                            Action = $"{entityType}Created",
                            EntityType = entityType,
                            EntityId = 0, // populated after save
                            Details = SafeSerialize(BuildEntitySnapshot(entry, useCurrentValues: true)),
                            IpAddress = ipAddress,
                            UserAgent = userAgent,
                            CreatedAt = now,
                        };
                        pendingAuditAdded.Add((entry.Entity, auditLog));
                    }
                    break;
                }

                case EntityState.Modified:
                {
                    // Check for soft delete
                    var deletedAtProp = entry.Property(nameof(BaseAuditableEntity.DeletedAt));
                    if (deletedAtProp.IsModified && deletedAtProp.CurrentValue != null && deletedAtProp.OriginalValue == null)
                    {
                        readyLogs.Add(new ActivityLog
                        {
                            EntityType = entityType,
                            EntityId = entry.Entity.Id,
                            UserId = userId,
                            Action = "Deleted",
                            Description = $"{FormatEntityTypeName(entityType)} deleted",
                            CreatedAt = now,
                        });

                        if (userId is int delUid)
                        {
                            readyAuditLogs.Add(new AuditLogEntry
                            {
                                UserId = delUid,
                                Action = $"{entityType}Deleted",
                                EntityType = entityType,
                                EntityId = entry.Entity.Id,
                                Details = null,
                                IpAddress = ipAddress,
                                UserAgent = userAgent,
                                CreatedAt = now,
                            });
                        }
                        break;
                    }

                    // Capture field-level changes
                    foreach (var prop in entry.Properties)
                    {
                        if (!prop.IsModified) continue;
                        if (AuditExcludedProperties.Contains(prop.Metadata.Name)) continue;
                        // Skip navigation/shadow properties
                        if (prop.Metadata.IsShadowProperty()) continue;

                        var oldVal = FormatPropertyValue(prop.OriginalValue);
                        var newVal = FormatPropertyValue(prop.CurrentValue);
                        if (oldVal == newVal) continue;

                        readyLogs.Add(new ActivityLog
                        {
                            EntityType = entityType,
                            EntityId = entry.Entity.Id,
                            UserId = userId,
                            Action = "FieldChanged",
                            Description = $"{FormatPropertyName(prop.Metadata.Name)} changed from \"{oldVal}\" to \"{newVal}\"",
                            FieldName = prop.Metadata.Name,
                            OldValue = oldVal,
                            NewValue = newVal,
                            CreatedAt = now,
                        });

                        var key = (entityType, entry.Entity.Id);
                        if (!modifiedDiffs.TryGetValue(key, out var list))
                        {
                            list = new List<(string, string, string)>();
                            modifiedDiffs[key] = list;
                        }
                        list.Add((prop.Metadata.Name, oldVal, newVal));
                    }
                    break;
                }
            }
        }

        // Emit one system-wide AuditLogEntry per modified entity (collapsing
        // per-field ActivityLog rows into a single audit row with a JSON diff
        // payload — better signal for compliance/security review).
        if (userId is int updUid)
        {
            foreach (var ((entityType, entityId), diffs) in modifiedDiffs)
            {
                if (diffs.Count == 0) continue;
                var details = SafeSerialize(diffs.Select(d => new
                {
                    field = d.Field,
                    oldValue = d.Old,
                    newValue = d.New,
                }));

                readyAuditLogs.Add(new AuditLogEntry
                {
                    UserId = updUid,
                    Action = $"{entityType}Updated",
                    EntityType = entityType,
                    EntityId = entityId,
                    Details = details,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    CreatedAt = now,
                });
            }
        }

        return (readyLogs, pendingAdded, readyAuditLogs, pendingAuditAdded);
    }

    private static Dictionary<string, object?> BuildEntitySnapshot(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<BaseAuditableEntity> entry,
        bool useCurrentValues)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in entry.Properties)
        {
            if (AuditExcludedProperties.Contains(prop.Metadata.Name)) continue;
            if (prop.Metadata.IsShadowProperty()) continue;
            var value = useCurrentValues ? prop.CurrentValue : prop.OriginalValue;
            snapshot[prop.Metadata.Name] = value switch
            {
                DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ssK"),
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                _ => value,
            };
        }
        return snapshot;
    }

    private static string? SafeSerialize(object? value)
    {
        if (value == null) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(value, _auditJsonOptions);
        }
        catch
        {
            return value.ToString();
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions _auditJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private int? GetCurrentUserId() => CurrentUserId;

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    private static string FormatPropertyValue(object? value)
    {
        if (value == null) return "";
        if (value is DateTimeOffset dto) return dto.ToString("yyyy-MM-dd HH:mm:ss");
        if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss");
        if (value is decimal d) return d.ToString("G");
        if (value is bool b) return b ? "true" : "false";
        return value.ToString() ?? "";
    }

    private static string FormatPropertyName(string name)
    {
        // Convert PascalCase to spaced words: "CustomerPO" → "Customer PO"
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                // Don't add space between consecutive uppercase (e.g., "PO")
                if (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])))
                    result.Append(' ');
            }
            result.Append(name[i]);
        }
        return result.ToString();
    }

    private static string FormatEntityTypeName(string typeName)
    {
        // "SalesOrder" → "Sales Order"
        return FormatPropertyName(typeName);
    }

    /// <summary>
    /// Walks the change tracker and forces every Added/Modified entity's
    /// DateTime / DateTime? property to <c>DateTimeKind.Utc</c>. Postgres
    /// <c>timestamptz</c> via Npgsql 6+ rejects DateTimes with
    /// <c>Kind != Utc</c> by throwing <c>InvalidCastException</c> at save
    /// time — which catches the most egregious bugs but produces an
    /// uninformative error and surfaces only at save (not at the bug
    /// site). This pre-pass converts <c>Kind=Unspecified</c> values
    /// (the default when constructing a DateTime from local components
    /// or when JSON-deserializing a date-only string) to UTC and
    /// converts <c>Kind=Local</c> values via <see cref="DateTime.ToUniversalTime"/>.
    /// Effectively the project's "always UTC at the boundary" rule
    /// becomes a real invariant rather than an aspiration.
    ///
    /// <para>Reflection-based — runs over every property typed
    /// <see cref="DateTime"/> or <see cref="Nullable{DateTime}"/> on
    /// every Added/Modified entity. Cost is proportional to the change
    /// set size, which in practice is small (single entity per request
    /// in most paths). Caches per-CLR-type property lists in
    /// <see cref="_dateTimePropertyCache"/> so subsequent saves on the
    /// same type don't re-reflect.</para>
    /// </summary>
    private void NormalizeDateTimes()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.State == EntityState.Added || entry.State == EntityState.Modified))
        {
            var props = GetDateTimeProperties(entry.Entity.GetType());
            foreach (var prop in props)
            {
                var raw = prop.GetValue(entry.Entity);
                if (raw == null) continue;
                var dt = (DateTime)raw;
                if (dt.Kind == DateTimeKind.Utc) continue;
                var utc = dt.Kind == DateTimeKind.Local
                    ? dt.ToUniversalTime()
                    : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                prop.SetValue(entry.Entity, utc);
            }
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo[]> _dateTimePropertyCache = new();

    private static System.Reflection.PropertyInfo[] GetDateTimeProperties(Type type)
    {
        return _dateTimePropertyCache.GetOrAdd(type, t =>
            t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p =>
                    p.CanRead && p.CanWrite &&
                    (p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?)))
                .ToArray());
    }

    private void SetTimestamps()
    {
        var entries = ChangeTracker.Entries<BaseAuditableEntity>();
        var now = _clock.UtcNow;
        var currentUserId = GetCurrentUserId()?.ToString();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                    if (entry.Entity.UpdatedAt == default) entry.Entity.UpdatedAt = entry.Entity.CreatedAt;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    // Auto-stamp DeletedBy when a soft delete is being committed:
                    // DeletedAt was modified to a non-null value this save AND
                    // DeletedBy hasn't been explicitly set by the handler. Centralizing
                    // here means soft-delete handlers only have to stamp DeletedAt;
                    // DeletedBy follows from CurrentUserId (set by middleware).
                    // Stored as the stringified user id, matching the convention in
                    // the handlers that already set DeletedBy explicitly.
                    if (entry.Entity.DeletedAt.HasValue
                        && entry.Entity.DeletedBy is null
                        && entry.Property(nameof(BaseAuditableEntity.DeletedAt)).IsModified)
                    {
                        entry.Entity.DeletedBy = currentUserId;
                    }
                    break;
            }
        }

        // WU-11 / TODO E1 — bump optimistic-locking Version on every Modified
        // save for transactional entities. New rows start at 1.
        foreach (var entry in ChangeTracker.Entries<IConcurrencyVersioned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Version == 0) entry.Entity.Version = 1;
                    break;
                case EntityState.Modified:
                    entry.Entity.Version = unchecked(entry.Entity.Version + 1);
                    if (entry.Entity.Version == 0) entry.Entity.Version = 1; // wraparound guard
                    break;
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])
                ? "_" + c
                : c.ToString()))
            .ToLowerInvariant();
    }
}
