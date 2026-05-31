namespace Forge.Api.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — Seed payload for the Part entity. The seed
/// strings are inlined here (instead of loaded from a resource file) so the
/// seeder can run without filesystem access. Mirrors the worked example in
/// <c>docs/workflow-pattern.md</c>.
///
/// <para>Pillar 6 (per the audit at <c>phase-4-output/part-type-field-relevance.md §5</c>)
/// authors 14 combo-specific definitions covering the 11 viable
/// (procurement × inventory) combos. Each combo carries both an express
/// template and a guided steps list per resolved decision #10 (user-pickable
/// mode at runtime, with a per-combo recommended default).</para>
///
/// <para>Step <c>componentName</c> values map 1:1 to Angular components
/// registered in <c>register-part-workflow-steps.ts</c>. All combo-specific
/// step components (Sourcing, Inventory, Quality, Shipping, SourcePart,
/// Vendor, ToolAsset, Flags, SalesHooks, VendorParts) are built and wired —
/// the legacy <c>PartManufacturerStepComponent</c> was retired pre-beta when
/// OEM identity moved off Part onto VendorPart.</para>
/// </summary>
public static class WorkflowSeedData
{
    public sealed record ValidatorSeed(
        string ValidatorId,
        string Predicate,
        string DisplayNameKey,
        string MissingMessageKey);

    public sealed record DefinitionSeed(
        string DefinitionId,
        string EntityType,
        string DefaultMode,
        string StepsJson,
        string? ExpressTemplateComponent);

    public static IReadOnlyList<ValidatorSeed> PartReadinessValidators { get; } =
    [
        new(
            ValidatorId: "hasBasics",
            // Pre-beta: the legacy `partType` + `material` text fields were
            // dropped along with the single-axis enum. Basics now requires the
            // three orthogonal axes' answer (procurement source + inventory
            // class) to be present — they are emitted by the fork dialog
            // before the workflow even starts, so this gate is always
            // satisfied when name is set.
            Predicate: """
            {"type":"all","of":[
              {"type":"fieldPresent","field":"name"},
              {"type":"fieldPresent","field":"procurementSource"},
              {"type":"fieldPresent","field":"inventoryClass"}
            ]}
            """.Replace("\r", "").Replace("\n", "").Replace(" ", ""),
            DisplayNameKey: "validators.parts.hasBasics",
            MissingMessageKey: "validators.parts.hasBasicsMissing"),
        new(
            ValidatorId: "hasBom",
            Predicate: """{"type":"relationExists","relation":"bomEntries","minCount":1}""",
            DisplayNameKey: "validators.parts.hasBom",
            MissingMessageKey: "validators.parts.hasBomMissing"),
        new(
            ValidatorId: "hasRouting",
            Predicate: """{"type":"relationExists","relation":"operations","minCount":1}""",
            DisplayNameKey: "validators.parts.hasRouting",
            MissingMessageKey: "validators.parts.hasRoutingMissing"),
        new(
            ValidatorId: "hasCost",
            Predicate: """
            {"type":"any","of":[
              {"type":"fieldPresent","field":"manualCostOverride"},
              {"type":"fieldPresent","field":"currentCostCalculationId"}
            ]}
            """.Replace("\r", "").Replace("\n", "").Replace(" ", ""),
            DisplayNameKey: "validators.parts.hasCost",
            MissingMessageKey: "validators.parts.hasCostMissing"),
        // Sourcing — the user has picked a preferred vendor for the part.
        // Optional in some flows (Make/Phantom parts may not have a single
        // preferred vendor); the workflow definitions decide whether the
        // sourcing step is required. The validator only answers "is it
        // satisfied?" — it doesn't dictate whether the step must pass.
        new(
            ValidatorId: "hasSourcing",
            Predicate: """{"type":"fieldPresent","field":"preferredVendorId"}""",
            DisplayNameKey: "validators.parts.hasSourcing",
            MissingMessageKey: "validators.parts.hasSourcingMissing"),
        // Inventory — the user has picked the part's primary unit of
        // measure for stock tracking. Threshold/reorder fields stay
        // optional (can be set later or driven by ABC analysis); UoM
        // is the minimum to enable any inventory transactions.
        new(
            ValidatorId: "hasInventory",
            Predicate: """{"type":"fieldPresent","field":"stockUomId"}""",
            DisplayNameKey: "validators.parts.hasInventory",
            MissingMessageKey: "validators.parts.hasInventoryMissing"),
        // NOTE: hasVendorParts and hasQuality are NOT defined here today.
        //   • vendorParts: Part doesn't expose a navigation collection to
        //     VendorPart in the entity model (PartReadinessLoader doesn't
        //     Include it), so `relationExists` would always return false.
        //     Adding the navigation is a separate refactor.
        //   • quality: TraceabilityType is non-nullable enum defaulting
        //     to None; `fieldPresent` can't distinguish "user picked None
        //     intentionally" from "untouched default". Needs a different
        //     evaluator approach (e.g. fieldEquals for non-default).
        // Both steps remain on the pointer-based completion fallback in
        // the WorkflowComponent shell — visited == complete.
    ];

    public static IReadOnlyList<DefinitionSeed> PartWorkflowDefinitions { get; } =
    [
        // -------- Buy combos (B1-B6) --------
        new(
            DefinitionId: "part-buy-raw-v1",
            EntityType: "Part",
            DefaultMode: "express",
            StepsJson: BuildBuyRawStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-buy-component-v1",
            EntityType: "Part",
            DefaultMode: "express",
            StepsJson: BuildBuyComponentStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-buy-subassembly-v1",
            EntityType: "Part",
            DefaultMode: "guided",
            StepsJson: BuildBuySubassemblyStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-buy-finishedgood-v1",
            EntityType: "Part",
            DefaultMode: "express",
            StepsJson: BuildBuyFinishedGoodStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-buy-consumable-v1",
            EntityType: "Part",
            DefaultMode: "express",
            StepsJson: BuildBuyConsumableStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-buy-tool-v1",
            EntityType: "Part",
            DefaultMode: "express",
            StepsJson: BuildBuyToolStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),

        // -------- Make combos (M1-M4) --------
        new(
            DefinitionId: "part-make-component-v1",
            EntityType: "Part",
            DefaultMode: "guided",
            StepsJson: BuildMakeComponentStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-make-subassembly-v1",
            EntityType: "Part",
            DefaultMode: "guided",
            StepsJson: BuildMakeSubassemblyStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-make-finishedgood-v1",
            EntityType: "Part",
            DefaultMode: "guided",
            StepsJson: BuildMakeFinishedGoodStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-make-tool-v1",
            EntityType: "Part",
            DefaultMode: "guided",
            StepsJson: BuildMakeToolStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),

        // -------- Subcontract combos (S1-S2) --------
        new(
            DefinitionId: "part-subcontract-component-v1",
            EntityType: "Part",
            DefaultMode: "guided",
            StepsJson: BuildSubcontractComponentStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-subcontract-subassembly-v1",
            EntityType: "Part",
            DefaultMode: "guided",
            StepsJson: BuildSubcontractSubassemblyStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),

        // -------- Phantom combos (P1, P3) --------
        new(
            DefinitionId: "part-phantom-subassembly-v1",
            EntityType: "Part",
            DefaultMode: "express",
            StepsJson: BuildPhantomSubassemblyStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
        new(
            DefinitionId: "part-phantom-finishedgood-v1",
            EntityType: "Part",
            DefaultMode: "express",
            StepsJson: BuildPhantomFinishedGoodStepsJson(),
            ExpressTemplateComponent: "PartExpressFormComponent"),
    ];

    // ---------- Buy combo step builders ----------

    // B1 — Buy + Raw. Audit §5.B1: 4 guided steps (Identity, Sourcing,
    // Inventory, Quality). No BOM, no Routing. Gates on hasBasics + hasCost.
    // VendorParts step inserted post-Sourcing — captures OEM identity (mfr
    // name, mfr PN, vendor SKU) + per-vendor pricing per (Part, Vendor) row.
    private static string BuildBuyRawStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"sourcing","labelKey":"workflow.parts.steps.sourcing","componentName":"PartSourcingStepComponent","required":true,"completionGates":["hasSourcing"]},
      {"id":"vendorParts","labelKey":"workflow.parts.steps.vendorParts","componentName":"PartVendorPartsStepComponent","required":false,"completionGates":[]},
      {"id":"inventory","labelKey":"workflow.parts.steps.inventory","componentName":"PartInventoryStepComponent","required":true,"completionGates":["hasInventory"]},
      {"id":"quality","labelKey":"workflow.parts.steps.quality","componentName":"PartQualityStepComponent","required":false,"completionGates":[]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // B2 — Buy + Component. Audit §5.B2: 5 guided steps. Pre-beta: the
    // standalone Manufacturer step was retired — OEM identity moved onto
    // VendorPart, captured in the new VendorParts step after Sourcing.
    private static string BuildBuyComponentStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"sourcing","labelKey":"workflow.parts.steps.sourcing","componentName":"PartSourcingStepComponent","required":true,"completionGates":["hasSourcing"]},
      {"id":"vendorParts","labelKey":"workflow.parts.steps.vendorParts","componentName":"PartVendorPartsStepComponent","required":false,"completionGates":[]},
      {"id":"inventory","labelKey":"workflow.parts.steps.inventory","componentName":"PartInventoryStepComponent","required":true,"completionGates":["hasInventory"]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // B3 — Buy + Subassembly. Audit §5.B3: same as B2 + Quality step. Gates
    // on hasBasics + hasCost. Manufacturer step retired pre-beta — OEM
    // identity now captured per-vendor in the VendorParts step.
    private static string BuildBuySubassemblyStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"sourcing","labelKey":"workflow.parts.steps.sourcing","componentName":"PartSourcingStepComponent","required":true,"completionGates":["hasSourcing"]},
      {"id":"vendorParts","labelKey":"workflow.parts.steps.vendorParts","componentName":"PartVendorPartsStepComponent","required":false,"completionGates":[]},
      {"id":"inventory","labelKey":"workflow.parts.steps.inventory","componentName":"PartInventoryStepComponent","required":true,"completionGates":["hasInventory"]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]},
      {"id":"quality","labelKey":"workflow.parts.steps.quality","componentName":"PartQualityStepComponent","required":true,"completionGates":[]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // B4 — Buy + FinishedGood (resold). Audit §5.B4: 5 guided steps.
    // Adds Shipping step (WeightEach, Dimensions). Manufacturer step retired
    // pre-beta; VendorParts step (post-Sourcing) carries OEM identity.
    private static string BuildBuyFinishedGoodStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"sourcing","labelKey":"workflow.parts.steps.sourcing","componentName":"PartSourcingStepComponent","required":true,"completionGates":["hasSourcing"]},
      {"id":"vendorParts","labelKey":"workflow.parts.steps.vendorParts","componentName":"PartVendorPartsStepComponent","required":false,"completionGates":[]},
      {"id":"inventory","labelKey":"workflow.parts.steps.inventory","componentName":"PartInventoryStepComponent","required":true,"completionGates":["hasInventory"]},
      {"id":"shipping","labelKey":"workflow.parts.steps.shipping","componentName":"PartShippingStepComponent","required":true,"completionGates":[]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // B5 — Buy + Consumable. Audit §5.B5: express-only, 5 sections. We still
    // expose a "sections" express form here. Mode default is express; when
    // users opt into guided we collapse the sections into individual steps
    // for parity. Gates: hasBasics + hasCost.
    private static string BuildBuyConsumableStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"sourcing","labelKey":"workflow.parts.steps.sourcing","componentName":"PartSourcingStepComponent","required":true,"completionGates":["hasSourcing"]},
      {"id":"vendorParts","labelKey":"workflow.parts.steps.vendorParts","componentName":"PartVendorPartsStepComponent","required":false,"completionGates":[]},
      {"id":"inventory","labelKey":"workflow.parts.steps.inventory","componentName":"PartInventoryStepComponent","required":true,"completionGates":["hasInventory"]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // B6 — Buy + Tool. Audit §5.B6: same as B5 plus a Tool section
    // (ToolingAssetId, calibration). Gates: hasBasics + hasCost.
    private static string BuildBuyToolStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"toolAsset","labelKey":"workflow.parts.steps.toolAsset","componentName":"PartToolAssetStepComponent","required":true,"completionGates":[]},
      {"id":"sourcing","labelKey":"workflow.parts.steps.sourcing","componentName":"PartSourcingStepComponent","required":true,"completionGates":["hasSourcing"]},
      {"id":"vendorParts","labelKey":"workflow.parts.steps.vendorParts","componentName":"PartVendorPartsStepComponent","required":false,"completionGates":[]},
      {"id":"inventory","labelKey":"workflow.parts.steps.inventory","componentName":"PartInventoryStepComponent","required":true,"completionGates":["hasInventory"]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // ---------- Make combo step builders ----------

    // M1 — Make + Component. Audit §5.M1: 5 guided steps. BOM is optional
    // (single-piece fab). Routing required for promotion to Active. Gates:
    // hasBasics + hasRouting + hasCost. (BOM not gated since some Make+Component
    // parts are single-piece fabricated from a single raw stock.)
    private static string BuildMakeComponentStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"manufacturing","labelKey":"workflow.parts.steps.manufacturing","componentName":"PartInventoryStepComponent","required":true,"completionGates":["hasInventory"]},
      {"id":"bom","labelKey":"workflow.parts.steps.bom","componentName":"PartBomStepComponent","required":false,"completionGates":[]},
      {"id":"routing","labelKey":"workflow.parts.steps.routing","componentName":"PartRoutingStepComponent","required":true,"completionGates":["hasRouting"]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]},
      {"id":"alternates","labelKey":"workflow.parts.steps.alternates","componentName":"PartAlternatesStepComponent","required":false,"completionGates":[]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // M2 — Make + Subassembly. Audit §5.M2: same as M1 with BOM required +
    // optional Quality. Gates: hasBasics + hasBom + hasRouting + hasCost.
    private static string BuildMakeSubassemblyStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"bom","labelKey":"workflow.parts.steps.bom","componentName":"PartBomStepComponent","required":true,"completionGates":["hasBom"]},
      {"id":"routing","labelKey":"workflow.parts.steps.routing","componentName":"PartRoutingStepComponent","required":true,"completionGates":["hasRouting"]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]},
      {"id":"quality","labelKey":"workflow.parts.steps.quality","componentName":"PartQualityStepComponent","required":false,"completionGates":[]},
      {"id":"alternates","labelKey":"workflow.parts.steps.alternates","componentName":"PartAlternatesStepComponent","required":false,"completionGates":[]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // M3 — Make + FinishedGood. Audit §5.M3: same as M2 + Sales & Shipping
    // step. Gates: hasBasics + hasBom + hasRouting + hasCost.
    private static string BuildMakeFinishedGoodStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"bom","labelKey":"workflow.parts.steps.bom","componentName":"PartBomStepComponent","required":true,"completionGates":["hasBom"]},
      {"id":"routing","labelKey":"workflow.parts.steps.routing","componentName":"PartRoutingStepComponent","required":true,"completionGates":["hasRouting"]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]},
      {"id":"shipping","labelKey":"workflow.parts.steps.shipping","componentName":"PartShippingStepComponent","required":true,"completionGates":[]},
      {"id":"quality","labelKey":"workflow.parts.steps.quality","componentName":"PartQualityStepComponent","required":false,"completionGates":[]},
      {"id":"alternates","labelKey":"workflow.parts.steps.alternates","componentName":"PartAlternatesStepComponent","required":false,"completionGates":[]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // M4 — Make + Tool. Audit §5.M4: 4 guided steps. No inventory thresholds
    // (tools aren't reordered as parts). Gates: hasBasics + hasBom +
    // hasRouting. Cost is not gated for tool builds — tooling cost flows via
    // the asset record, not the part.
    private static string BuildMakeToolStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"toolAsset","labelKey":"workflow.parts.steps.toolAsset","componentName":"PartToolAssetStepComponent","required":true,"completionGates":[]},
      {"id":"bom","labelKey":"workflow.parts.steps.bom","componentName":"PartBomStepComponent","required":true,"completionGates":["hasBom"]},
      {"id":"routing","labelKey":"workflow.parts.steps.routing","componentName":"PartRoutingStepComponent","required":true,"completionGates":["hasRouting"]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // ---------- Subcontract combo step builders ----------

    // S1 — Subcontract + Component. Audit §5.S1: 4 guided steps. SourcePart
    // pointer + Vendor + Quality. Gates: hasBasics + hasCost. (No internal
    // routing — the subcontractor owns the operations; we represent that
    // via the source-part pointer rather than an Operations list, hence
    // hasRouting is not gated here.)
    private static string BuildSubcontractComponentStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"sourcePart","labelKey":"workflow.parts.steps.sourcePart","componentName":"PartSourcePartStepComponent","required":true,"completionGates":[]},
      {"id":"vendor","labelKey":"workflow.parts.steps.vendor","componentName":"PartVendorStepComponent","required":true,"completionGates":["hasSourcing"]},
      {"id":"vendorParts","labelKey":"workflow.parts.steps.vendorParts","componentName":"PartVendorPartsStepComponent","required":false,"completionGates":[]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]},
      {"id":"quality","labelKey":"workflow.parts.steps.quality","componentName":"PartQualityStepComponent","required":true,"completionGates":[]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // S2 — Subcontract + Subassembly. Audit §5.S2: same as S1 with BOM step
    // inserted (we own the design even though vendor builds). Gates:
    // hasBasics + hasBom + hasCost.
    private static string BuildSubcontractSubassemblyStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"sourcePart","labelKey":"workflow.parts.steps.sourcePart","componentName":"PartSourcePartStepComponent","required":true,"completionGates":[]},
      {"id":"bom","labelKey":"workflow.parts.steps.bom","componentName":"PartBomStepComponent","required":true,"completionGates":["hasBom"]},
      {"id":"vendor","labelKey":"workflow.parts.steps.vendor","componentName":"PartVendorStepComponent","required":true,"completionGates":["hasSourcing"]},
      {"id":"vendorParts","labelKey":"workflow.parts.steps.vendorParts","componentName":"PartVendorPartsStepComponent","required":false,"completionGates":[]},
      {"id":"costing","labelKey":"workflow.parts.steps.costing","componentName":"PartCostingStepComponent","required":true,"completionGates":["hasCost"]},
      {"id":"quality","labelKey":"workflow.parts.steps.quality","componentName":"PartQualityStepComponent","required":true,"completionGates":[]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // ---------- Phantom combo step builders ----------

    // P1 — Phantom + Subassembly. Audit §5.P1: express-only short form.
    // Identity + BOM + Flags. No sourcing, inventory, cost, or quality —
    // phantoms are never stocked. Gates: hasBasics + hasBom.
    private static string BuildPhantomSubassemblyStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"bom","labelKey":"workflow.parts.steps.bom","componentName":"PartBomStepComponent","required":true,"completionGates":["hasBom"]},
      {"id":"flags","labelKey":"workflow.parts.steps.flags","componentName":"PartFlagsStepComponent","required":false,"completionGates":[]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // P3 — Phantom + FinishedGood (configure-to-order parent). Audit §5.P3:
    // 3-step guided, recommended express. No sourcing, inventory thresholds,
    // MRP, quality, or shipping — parent is never stocked. Gates: hasBasics
    // + hasBom.
    private static string BuildPhantomFinishedGoodStepsJson() => """
    [
      {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
      {"id":"bom","labelKey":"workflow.parts.steps.bom","componentName":"PartBomStepComponent","required":true,"completionGates":["hasBom"]},
      {"id":"salesHooks","labelKey":"workflow.parts.steps.salesHooks","componentName":"PartSalesHooksStepComponent","required":false,"completionGates":[]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // (Pre-beta: the legacy `part-assembly-guided-v1` and
    // `part-raw-material-express-v1` transitional alias builders were
    // retired along with the single-axis fork dialog. New runs always go
    // through one of the 14 canonical combo definitions above.)

    // ─────────────────────────────────────────────────────────────────
    // Vendor — single guided + express pairing (2026-05-31 migration).
    //
    // Migrates the legacy guided-vendor-dialog (mat-stepper) off the
    // visual divergence onto the same shared WorkflowComponent shell that
    // parts uses. Five steps after dropping the relationship-type picker
    // — that picker only folded into the Notes column anyway; admins who
    // want to label vendors can type it into Notes manually (see CLAUDE.md
    // self-maintenance entry).
    //
    // Identity is the only hard gate; address / terms / supply items are
    // optional collection-ish steps that admins can fill now or later via
    // the vendor detail page. Review is a read-only summary step that
    // re-asserts hasIdentity so completeRun can run its gates.
    // ─────────────────────────────────────────────────────────────────

    public static IReadOnlyList<ValidatorSeed> VendorReadinessValidators { get; } =
    [
        new(
            ValidatorId: "hasIdentity",
            Predicate: """{"type":"fieldPresent","field":"companyName"}""",
            DisplayNameKey: "validators.vendors.hasIdentity",
            MissingMessageKey: "validators.vendors.hasIdentityMissing"),
    ];

    public static IReadOnlyList<DefinitionSeed> VendorWorkflowDefinitions { get; } =
    [
        new(
            DefinitionId: "vendor-guided-v1",
            EntityType: "Vendor",
            DefaultMode: "guided",
            StepsJson: BuildVendorGuidedStepsJson(),
            ExpressTemplateComponent: "VendorExpressFormComponent"),
    ];

    private static string BuildVendorGuidedStepsJson() => """
    [
      {"id":"identity","labelKey":"workflow.vendors.steps.identity","componentName":"VendorIdentityStepComponent","required":true,"completionGates":["hasIdentity"]},
      {"id":"address","labelKey":"workflow.vendors.steps.address","componentName":"VendorAddressStepComponent","required":false,"completionGates":[]},
      {"id":"terms","labelKey":"workflow.vendors.steps.terms","componentName":"VendorTermsStepComponent","required":false,"completionGates":[]},
      {"id":"supplyItems","labelKey":"workflow.vendors.steps.supplyItems","componentName":"VendorSupplyItemsStepComponent","required":false,"completionGates":[]},
      {"id":"review","labelKey":"workflow.vendors.steps.review","componentName":"VendorReviewStepComponent","required":true,"completionGates":["hasIdentity"]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");

    // ─────────────────────────────────────────────────────────────────
    // Customer — single guided + express pairing (2026-05-31 migration).
    //
    // Mirrors the vendor migration: drops the legacy guided-customer-dialog
    // (mat-stepper) for the shared WorkflowComponent shell. Four steps
    // after the scope cuts documented in CustomerWorkflowAdapter:
    //   identity → addresses-stub → creditAndTax → review.
    //
    // Engagement-shape picker dropped (was dead-letter UI — folded into
    // a notes string that the server never received). Addresses deferred
    // to the customer detail page like vendor supply items.
    // ─────────────────────────────────────────────────────────────────

    public static IReadOnlyList<ValidatorSeed> CustomerReadinessValidators { get; } =
    [
        new(
            ValidatorId: "hasIdentity",
            Predicate: """{"type":"fieldPresent","field":"name"}""",
            DisplayNameKey: "validators.customers.hasIdentity",
            MissingMessageKey: "validators.customers.hasIdentityMissing"),
    ];

    public static IReadOnlyList<DefinitionSeed> CustomerWorkflowDefinitions { get; } =
    [
        new(
            DefinitionId: "customer-guided-v1",
            EntityType: "Customer",
            DefaultMode: "guided",
            StepsJson: BuildCustomerGuidedStepsJson(),
            ExpressTemplateComponent: "CustomerExpressFormComponent"),
    ];

    private static string BuildCustomerGuidedStepsJson() => """
    [
      {"id":"identity","labelKey":"workflow.customers.steps.identity","componentName":"CustomerIdentityStepComponent","required":true,"completionGates":["hasIdentity"]},
      {"id":"addresses","labelKey":"workflow.customers.steps.addresses","componentName":"CustomerAddressesStepComponent","required":false,"completionGates":[]},
      {"id":"creditAndTax","labelKey":"workflow.customers.steps.creditAndTax","componentName":"CustomerCreditAndTaxStepComponent","required":false,"completionGates":[]},
      {"id":"review","labelKey":"workflow.customers.steps.review","componentName":"CustomerReviewStepComponent","required":true,"completionGates":["hasIdentity"]}
    ]
    """.Replace("\r", "").Replace("\n", "").Replace("  ", "");
}
