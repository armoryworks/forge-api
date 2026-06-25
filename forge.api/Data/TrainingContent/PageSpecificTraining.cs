using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

using Serilog;

namespace Forge.Api.Data.TrainingContent;

public class PageSpecificTraining : TrainingContentBase
{
    public PageSpecificTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override async Task SeedAsync()
    {
        // 1 ── Carriers ──────────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Shipping Carriers",
            Slug = "carriers-overview",
            Summary = "Manage shipping carriers and the API credentials that power live rates, labels, and tracking.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/admin/carriers"]""",
            Tags = """["admin","shipping","carriers"]""",
            ContentJson = """{"body":"## Shipping Carriers\n\nThis page is the single home for your shipping carriers and the only place carrier API credentials are entered. Each row is a carrier — an integrated one such as UPS, FedEx, USPS, or DHL, or a custom \"shadow\" shipper you define yourself.\n\n### Key actions\n\n- **New Carrier** opens a dialog where you set the name, code, integration kind (Manual or Api), delivery mode (Manual, Poll, or Webhook), an optional integration service ID, and whether a scan is required.\n- **Credentials** (the key icon) opens a write-only form for the client ID, secret, optional account number, and environment (sandbox or production). Secrets are encrypted server-side and never returned, so the secret is always re-entered when you edit.\n- **Test** (the signal icon) runs `POST /carriers/{id}/test` against the live carrier API and reports success or the error in a snackbar. It is disabled until credentials exist.\n\n### Reading the table\n\nColumns show Name, Code, Integration Kind, Scan Required, a Credentials Configured chip (green when set, a warning when an Api carrier has none), and an Active chip. Carriers configured here feed the rate comparison, label creation, and tracking features on the Shipments module — get the credentials right and Test green before relying on a carrier in production.","sections":[]}"""
        });

        // 2 ── Capabilities ──────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Capabilities Browser",
            Slug = "capabilities-overview",
            Summary = "Browse and toggle the named capabilities that turn Forge features on or off for your install.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/admin/capabilities"]""",
            Tags = """["admin","capabilities","configuration"]""",
            ContentJson = """{"body":"## Capabilities Browser\n\nForge ships as one product whose features are gated by named capabilities. This page is the central grid where you browse those capabilities, grouped by functional area (Accounting, Logistics, Quality, and so on), and turn each one on or off for your install.\n\n### Key actions\n\n- **Search** filters live by name, description, or code. The **Area** dropdown narrows to one functional area, **Enabled only** hides what is off, and **Consultant mode** reveals the raw capability codes (hidden by default).\n- Each row has a **toggle**. Flipping it saves immediately and shows a brief spinner; on error it rolls back.\n- The **chevron** opens the per-capability detail page at `/admin/capabilities/:code`.\n\n### Dependencies and conflicts\n\nCapabilities have dependencies and mutual exclusions enforced on the server. If you enable one with unmet prerequisites, a snackbar lists what to enable first; disabling one that others depend on lists the dependents; and mutex conflicts (such as built-in vs. external accounting) prompt you to disable the peer first.\n\n### Getting started\n\nOn a fresh install a banner offers to **Run Discovery** (a guided questionnaire) or **Browse Presets** (curated bundles), both of which apply changes here. You can also configure everything by hand from this grid.","sections":[]}"""
        });

        // 3 ── Discovery ─────────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Discovery Wizard",
            Slug = "discovery-overview",
            Summary = "Answer a short questionnaire and let Forge recommend a capability preset tailored to your business.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/admin/discovery"]""",
            Tags = """["admin","discovery","onboarding"]""",
            ContentJson = """{"body":"## Discovery Wizard\n\nDiscovery is a guided questionnaire that recommends the right capability preset for your business so you do not have to toggle 157 capabilities by hand. It is the fastest way to configure a fresh install.\n\n### How it works\n\nYou answer roughly 22 questions about your business profile — size, regulation, multi-site, and similar. Question types vary: single-choice and yes/no use radio buttons, multi-choice uses checkboxes, and free-text fields are optional but improve the recommendation. **Next** and **Back** move between steps, and your position is tracked in the URL (`?step=N`) so browser navigation works.\n\n### The recommendation\n\nAs you answer, a sidebar shows which preset you are leaning toward, with a confidence level. The final screen presents the recommended preset with its name, confidence badge, the reasons behind it, and any alternative presets you can choose instead. A delta list shows exactly which capabilities would be enabled (green) or disabled (red).\n\n### Applying\n\n**Apply** opens a preview dialog showing the same deltas and any conflicts before committing. **Consultant mode** expands the question set, and **Skip Discovery** jumps you straight back to the Capabilities page to configure manually.","sections":[]}"""
        });

        // 4 ── Presets ───────────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Capability Presets",
            Slug = "presets-overview",
            Summary = "Browse curated capability bundles, compare them side by side, and apply one to configure your install.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/admin/presets"]""",
            Tags = """["admin","presets","configuration"]""",
            ContentJson = """{"body":"## Capability Presets\n\nPresets are curated bundles of capabilities tuned for common business profiles. This browser shows eight preset cards — seven named profiles plus a Custom option — as an alternative to answering the Discovery questionnaire or toggling capabilities one by one.\n\n### Reading the cards\n\nEach card shows the preset name and ID, a short description, a target profile (for example Small Retail or Food Manufacturing), recommended-for tags, and a count of how many capabilities it enables. The preset currently in effect is marked with a green **Active** chip.\n\n### Key actions\n\n- **Click a card** to open its detail page, where you see the full capability list and an apply button. The Custom card opens the manual-configuration flow.\n- **Compare** enters a selection mode. Pick two to four presets (the Custom card is excluded) and the **Compare** button opens a side-by-side diff matrix so you can weigh the differences before deciding.\n- **Back to Capabilities** returns to the main grid.\n\n### How it fits\n\nApplying a preset shows the capability changes for confirmation first, then updates your install's capabilities. Presets, Discovery, and the Capabilities grid all write to the same capability state, so you can start from a preset and fine-tune from there.","sections":[]}"""
        });

        // 5 ── Currencies ────────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Currencies & FX Rates",
            Slug = "currencies-overview",
            Summary = "Maintain the currency catalog and an append-only history of daily exchange rates.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 5,
            AppRoutes = """["/admin/currencies"]""",
            Tags = """["admin","currencies","finance"]""",
            ContentJson = """{"body":"## Currencies & FX Rates\n\nThis page has two sections: the currency catalog and the exchange-rate history. Together they provide the multi-currency foundation used by invoicing, AR/AP, and financial reporting.\n\n### Currencies\n\nThe catalog lists each currency with its code, name, symbol, decimal places, a Base chip, an Active chip, and a sort order. **New Currency** opens a dialog to add one — you set the code, name, symbol, decimal places (JPY uses 0, most use 2), whether it is the base currency, whether it is active, and its sort order. Click any row to edit. The base currency is the system-wide denominator for reporting.\n\n### Exchange rates\n\nThe rate history is **append-only by design** — entering a new rate for a pair on a new date adds a row that supersedes the prior one rather than overwriting it, preserving an audit trail. **New Exchange Rate** (enabled once at least two currencies exist) lets you pick the from- and to-currency, an effective date, the rate, and a source. The table is ordered newest-first and shows the effective date, the pair, the rate to several decimals, the source, and when it was fetched.","sections":[]}"""
        });

        // 6 ── Role Templates ────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Role Templates",
            Slug = "role-templates-overview",
            Summary = "Bundle several base roles into a reusable template so staff who wear many hats are granted permissions in one step.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 6,
            AppRoutes = """["/admin/role-templates"]""",
            Tags = """["admin","roles","permissions"]""",
            ContentJson = """{"body":"## Role Templates\n\nRole templates bundle several base roles into a single named collection so a user who wears multiple hats can be granted all the right permissions at once, rather than role by role. This page manages those templates.\n\n### Reading the table\n\nEach row shows the template name, description, the included base roles as chips, an assignees count, and a source of either System or Custom. System-default templates are seeded at install and are read-only — they show a lock icon, and the tooltip suggests duplicating one to customize it.\n\n### Key actions\n\n- **New Template** creates a custom template. You give it a name (required, up to 100 characters), an optional description, and select at least one base role to include.\n- **Edit** (custom templates only) reopens that dialog to change the name, description, or included roles.\n- **Delete** (custom templates only) deactivates the template and unassigns everyone currently using it; a confirmation dialog tells you how many users are affected. Their underlying identity roles are not changed.\n\n### How it fits\n\nWhen a template is assigned to a user, they receive every base role it includes in their permission claims. This keeps role management consistent across staff with similar responsibilities.","sections":[]}"""
        });

        // 7 ── Automations ───────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Automations — Event Failures",
            Slug = "automations-overview",
            Summary = "Monitor background domain-event handlers that failed, then retry or resolve them.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 7,
            AppRoutes = """["/admin/automations"]""",
            Tags = """["admin","automations","reliability"]""",
            ContentJson = """{"body":"## Automations — Event Failures\n\nMany Forge features run work in the background through domain events — for example, reacting after a job or order is created. This page gives admins visibility into the handlers that failed, so a background hiccup never goes silently unnoticed.\n\n### Reading the table\n\nEach row is a failed event handler showing the event type, the handler name, a truncated error message (hover for the full text), a color-coded status (red Failed, orange Retrying, green Resolved), the retry count, when it first failed, and when it was last retried. Clicking a row expands an inline panel with the full error and the formatted event payload.\n\n### Key actions\n\n- The **status filter** narrows to All, Failed, Retrying, or Resolved, and **Refresh** reloads the list.\n- **Retry** (on a failed row) re-queues the handler to run again, after a confirmation.\n- **Resolve** (on a failed or retrying row) marks the failure as handled without retrying — useful once you have fixed the underlying cause or decided no retry is needed.\n\n### How it fits\n\nThese controls are an admin troubleshooting tool. A buildup of failures points to a problem in an integration, an external service, or an internal subscriber that could not reach consistency.","sections":[]}"""
        });

        // 8 ── Customer Segments ─────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Customer Segments",
            Slug = "customer-segments-overview",
            Summary = "Preview of saved customer segments — named filter sets for targeting cohorts of customers.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 8,
            AppRoutes = """["/customers/segments"]""",
            Tags = """["customers","segments","preview"]""",
            ContentJson = """{"body":"## Customer Segments\n\nCustomer segments are saved filter sets that group your customers into named cohorts — for example aerospace high-value accounts, ITAR-ready customers, dormant accounts, or those on credit watch. They let you target a defined group for campaigns, outreach, or risk management.\n\n### What you see today\n\nThis page is currently a **preview**. It shows a handful of representative example segments as cards so you can understand what the feature will deliver; full create, edit, and delete functionality is not yet wired up. Each example card displays a segment name, a short description, a human-readable filter summary (such as customers flagged aerospace with an open invoice total over a threshold), and an estimated count of matching customers.\n\n### How filters work\n\nThe example filters reference real customer fields — aerospace and ITAR flags, open invoice totals, credit limits and credit-hold status, reference-OK status, active status, and last-order date. These are the same attributes the finished feature will let you combine into your own saved segments.\n\n### How it fits\n\nWhen fully delivered, segments will offer a filter-builder, saved-segment management, and the ability to select matching customers directly from the customer list for bulk actions.","sections":[]}"""
        });

        // 9 ── Portal Access ─────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Customer Portal Access",
            Slug = "portal-access-overview",
            Summary = "Provision and toggle self-service portal logins for individual customer contacts.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 9,
            AppRoutes = """["/customers/portal-access"]""",
            Tags = """["customers","portal","access"]""",
            ContentJson = """{"body":"## Customer Portal Access\n\nThis page controls which customer contacts can sign in to the self-service customer portal, where they view their invoices, quotes, and shipments. It lists every contact that has been provisioned and lets you grant or revoke access from one place.\n\n### Reading the table\n\nEach row shows the contact name (in Last, First format), the customer they belong to, their email, when they last logged in (or \"Never signed in\"), when access was created, and an Enabled toggle. Clicking a row opens that customer's contacts page for context.\n\n### Key actions\n\n- **Provision Portal Access** opens a picker of eligible contacts — those who have an email address but no portal account yet — so you can grant access. Email is the login identifier; there is no separate username.\n- The **Enabled toggle** on each row activates or deactivates a contact's login. It is briefly disabled while the change saves.\n\n### Requirements\n\nA contact can use the portal only when three things are true: they have an email address, they have a provisioned access row, and that row is enabled. Re-provisioning a contact who already has access is safe — the server handles it idempotently.","sections":[]}"""
        });

        // 10 ── Lead Queue ───────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Lead Work Queue",
            Slug = "lead-queue-overview",
            Summary = "A keyboard-first calling queue: pull a batch of leads, dial, and record a disposition for each.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 10,
            AppRoutes = """["/leads/queue"]""",
            Tags = """["leads","queue","outreach"]""",
            ContentJson = """{"body":"## Lead Work Queue\n\nThe lead queue is a worker screen for calling through leads one at a time. An operator pulls a batch, dials each lead, and records the outcome — a keyboard-first workflow built for speed.\n\n### Working the queue\n\n**Pull Leads** fetches the next batch into your working queue (a configurable size, defaulting to five). Each lead card shows the company, contact, email, a click-to-dial phone number, the lead source, the last activity time, and any notes, plus chips for the campaign, cooldown date, or opt-out status. A progress indicator shows your position in the batch, and the J and K keys (or the chevrons) move between leads.\n\n### Recording a disposition\n\nSix disposition buttons, each with a keyboard shortcut, record the result and auto-advance to the next lead: Engaged (E), No Answer (N), Voicemail Left (V), Callback Scheduled (C), Bad Data (B), and Suppressed (S). Choosing Callback Scheduled opens a modal to pick a date and time; cancelling that modal aborts the disposition. You can add notes that save with each action.\n\n### How it fits\n\nDispositions update each lead's status and feed campaign reporting and follow-up scheduling, bridging raw intake into the sales pipeline.","sections":[]}"""
        });

        // 11 ── Lead Campaigns ───────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Lead Campaigns",
            Slug = "lead-campaigns-overview",
            Summary = "Create and manage outreach campaigns that organize bulk-imported leads by strategy.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 11,
            AppRoutes = """["/leads/campaigns"]""",
            Tags = """["leads","campaigns","outreach"]""",
            ContentJson = """{"body":"## Lead Campaigns\n\nCampaigns organize your lead outreach by strategy. Each campaign is a label that bulk-imported leads are tagged with, grouping them for batch calling and reporting.\n\n### Reading the table\n\nThe list shows each campaign's name, strategy, lead count, active status, start date, and creation date, and every column is sortable. The lead count is derived automatically from intake. An Active or Inactive chip shows whether the campaign is currently in use.\n\n### Key actions\n\n- **New Campaign** opens the create dialog. Click a row or its edit icon to modify an existing campaign.\n- In the dialog you set the name (required, up to 200 characters), an optional description, and a strategy: Cold Call, Cold Email, Trade Show Follow-up, Webinar Attendee, List Purchase, or Manual Entry. The strategy is set at creation and is read-only afterward.\n- You can set a default cooldown (0 to 730 days) that enforces a minimum retry interval, optional start and end dates, and — when editing — an Is Active toggle.\n\n### How it fits\n\nThe strategy informs calling scripts and follow-up sequencing, and the cooldown governs retries. Deactivating a campaign excludes it from queue pulls without deleting its history, so managers can pause outreach cleanly.","sections":[]}"""
        });

        // 12 ── Lead Intake ──────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Bulk Lead Intake",
            Slug = "lead-intake-overview",
            Summary = "Import leads in bulk from a CSV file or pasted text, with preview, deduplication, and opt-out checks.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 12,
            AppRoutes = """["/leads/intake"]""",
            Tags = """["leads","intake","import"]""",
            ContentJson = """{"body":"## Bulk Lead Intake\n\nThis page imports leads in bulk from a CSV file or pasted text — ideal for trade-show lists, webinar sign-ups, or purchased lists. It validates, deduplicates, and commits the rows in a guided three-step flow.\n\n### The flow\n\nFirst, choose a strategy (Cold Call, Cold Email, Trade Show, Webinar, List Purchase, or Manual) and an optional campaign tag. Then upload a .csv or .txt file, or paste comma- or tab-separated text and click Parse Paste. Finally, click **Preview** to validate, review the results, and click **Commit** to insert the valid rows; on success you are returned to the leads list.\n\n### Columns and validation\n\nThe parser recognizes common column-name aliases — for example company, tel, and contact map to the right fields — and a collapsible help table lists them all. Each row needs at least a company name, email, or phone; rows missing all three are dropped. The preview tags each row: Created (will be inserted); a duplicate status when the lead, contact, or another row in the batch already matches; Suppressed or In Cooldown when an opt-out or retry window applies; and Missing Required Field or Invalid when validation fails. A summary tallies Created, Skipped, and Total.\n\n### How it fits\n\nCreated leads enter the work queue for calling. The deduplication and opt-out checks keep your pipeline clean and compliant.","sections":[]}"""
        });

        // 13 ── Recurring Orders ─────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Recurring Orders",
            Slug = "recurring-orders-overview",
            Summary = "Define templates that automatically generate a fresh sales order on a set schedule.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 13,
            AppRoutes = """["/sales-orders/recurring"]""",
            Tags = """["sales-orders","recurring","automation"]""",
            ContentJson = """{"body":"## Recurring Orders\n\nRecurring orders are templates that automatically generate a fresh sales order on a schedule you set — ideal for standing or repeat customers such as consumables or recurring service. A nightly background job spins each due template into a live sales order with its line items cloned.\n\n### Reading the table\n\nEach template shows its name, customer, interval in days, next generation date, last generated date (\"Never\" until it first runs), line count, and an active status chip. All columns sort. Inactive templates are skipped by the nightly job.\n\n### Key actions\n\n- **New Recurring** opens the create dialog. There is no inline edit — changing a template means deleting it and recreating it, which keeps the auto-generation logic stable.\n- The **delete** icon permanently removes a template and cancels its future generation, after a confirmation.\n- In the dialog you set a name (up to 200 characters), pick the customer, choose the next generation date, set the interval in days (1 to 365 — 30 is roughly monthly), and add optional notes. You then add at least one line, each with a part, description, quantity, and unit price.\n\n### How it fits\n\nEach night the job finds templates whose next generation date has arrived, creates a sales order from the template's lines, and advances the next date by the interval.","sections":[]}"""
        });

        Log.Information("Seeded page-specific training modules");
    }
}
