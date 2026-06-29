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
            ContentJson = """{"body":"## Shipping Carriers\n\nThis page is the single home for your shipping carriers and the only place carrier API credentials are entered. Each row is a carrier — an integrated one such as UPS, FedEx, USPS, or DHL, or a custom \"shadow\" shipper you define yourself.\n\n### Key actions\n\n- **New Carrier** opens a dialog where you set the name, code, integration kind (Manual or Api), delivery mode (Manual, Poll, or Webhook), an optional integration service ID, and whether a scan is required.\n- **Credentials** (the key icon) opens a write-only form for the client ID, secret, optional account number, and environment (sandbox or production). Secrets are encrypted server-side and never returned, so the secret is always re-entered when you edit.\n- **Test** (the signal icon) runs `POST /carriers/{id}/test` against the live carrier API and reports success or the error in a snackbar. It is disabled until credentials exist.\n\n### Manual vs Api carriers\n\nThe **Integration Kind** you choose changes everything else on the row. A **Manual** carrier is just a label — your shippers write a tracking number by hand, so there are no credentials, no Test button, and no integration service ID. An **Api** carrier talks to the live carrier system, so it surfaces the Integration Service ID field at creation, shows the key (credentials) and signal (test) icons in its row, and warns with a yellow chip until credentials are saved. Pick Manual when you only need to record a tracking number; pick Api when you want live rates, label printing, and automatic tracking on the Shipments module.\n\n### Delivery Update Mode\n\nThis controls how a shipment's tracking status gets refreshed after it ships. **Manual** means a person updates the status by hand. **Poll** means Forge periodically asks the carrier API for the latest tracking events. **Webhook** means the carrier pushes status changes to Forge as they happen — the most timely option, but only for carriers that support it. Manual is the safe default for a Manual-kind carrier.\n\n### Lifecycle — there is no row edit or deactivate\n\nThe table is intentionally lean: a carrier row has no edit or deactivate action. The only per-row controls are Credentials and Test, and those appear on Api carriers only. To change a carrier's name, kind, or delivery mode after creation, you work through the API or recreate it — the screen is built around getting a carrier configured and tested, not editing it repeatedly. Plan the name, code, and kind before you save.\n\n### Reading the table\n\nColumns show Name, Code, Integration Kind, Scan Required, a Credentials Configured chip (green when set, a warning when an Api carrier has none), and an Active chip. Carriers configured here feed the rate comparison, label creation, and tracking features on the Shipments module — get the credentials right and Test green before relying on a carrier in production.","sections":[]}"""
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

        // 14 ── Carriers Walkthrough ─────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Carriers — Guided Tour",
            Slug = "carriers-walkthrough",
            Summary = "A guided tour of Admin > Carriers: create a carrier, enter API credentials, and run a connection test.",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 14,
            AppRoutes = """["/admin/carriers"]""",
            Tags = """["admin","shipping","carriers","walkthrough"]""",
            ContentJson = """{"appRoute":"/admin/carriers","startButtonLabel":"Tour Carriers","steps":[{"element":"[data-testid='carrier-new-btn']","popover":{"title":"New Carrier","description":"Click here to add a carrier. The dialog asks for a name, an optional code, the integration kind (Manual or Api), a delivery update mode (Manual, Poll, or Webhook), and whether a scan is required to ship. Choose Api when you want live rates, labels, and tracking; choose Manual when you only record a tracking number by hand.","side":"bottom"}},{"element":"app-data-table","popover":{"title":"The Carriers Table","description":"Every carrier is a row here. Columns show Name, Code, Integration kind, Scan, a Credentials chip, and Active. An Api carrier with no credentials shows a yellow warning chip — that's your cue to open Credentials next. There is no edit or deactivate action on a row; the only per-row controls are Credentials and Test, and they appear on Api carriers only.","side":"top"}},{"element":"[data-testid='carrier-credentials-btn']","popover":{"title":"Enter Credentials","description":"On an Api carrier, the key icon opens a write-only credentials form: client ID, secret, an optional account number, and the environment (sandbox or production). The secret is encrypted server-side and never read back, so you re-enter it every time you edit. Save to turn the Credentials chip green.","side":"left"}},{"element":"[data-testid='carrier-test-btn']","popover":{"title":"Test the Connection","description":"The signal icon runs a live connection test against the carrier API and reports success or the error in a snackbar. It stays disabled until credentials exist. Get this green in sandbox before you rely on the carrier in production.","side":"left"}}]}"""
        });

        // 15 ── Carriers Field Reference ─────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Carriers Field Reference",
            Slug = "carriers-field-reference",
            Summary = "Reference for every field, chip, and action on the Carriers admin page, including Manual vs Api and the three delivery update modes.",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 15,
            AppRoutes = """["/admin/carriers"]""",
            Tags = """["admin","shipping","carriers","reference"]""",
            ContentJson = """{"title":"Carriers Field Reference","groups":[{"heading":"New Carrier Dialog","items":[{"label":"Name (required)","value":"Text input, max 100 characters. The carrier's display name (e.g. 'UPS', 'FedEx', 'Local Courier'). data-testid: carrier-name"},{"label":"Code","value":"Optional short code, max 50 characters, used to identify the carrier in lists and on documents."},{"label":"Integration Kind (required)","value":"Manual or Api. Manual = label only, no credentials/test. Api = talks to the live carrier system, surfaces the Service ID field plus the Credentials and Test row actions. Default: Manual."},{"label":"Delivery Update Mode (required)","value":"Manual, Poll, or Webhook — how tracking status refreshes after a shipment leaves. Default: Manual."},{"label":"Integration Service ID","value":"Shown only when Integration Kind is Api. An optional identifier that maps this carrier row to the underlying shipping integration. Max 50 characters."},{"label":"Requires Scan to Ship","value":"Toggle (default ON). When on, a package must be scanned before it can be marked shipped for this carrier."}]},{"heading":"Credentials Dialog (Api carriers only)","items":[{"label":"Client ID (required)","value":"The carrier API client/account identifier, max 200 characters. Not secret — it is shown pre-filled when you reopen the dialog. data-testid: carrier-client-id"},{"label":"Secret (required)","value":"The carrier API secret/key. Write-only: encrypted server-side and never returned, so you re-enter it every time you edit. data-testid: carrier-secret"},{"label":"Account Number","value":"Optional carrier account number, max 50 characters."},{"label":"Environment (required)","value":"sandbox or production. Use sandbox to test the connection without affecting real shipments; switch to production when you go live."}]},{"heading":"Integration Kind — what changes","items":[{"label":"Manual","value":"No credentials, no Test button, no Service ID field. Your shippers type the tracking number by hand. Use when you don't need live rates/labels."},{"label":"Api","value":"Surfaces the Service ID field at creation and the key (Credentials) + signal (Test) icons in the row. Powers live rate comparison, label creation, and automatic tracking on the Shipments module."}]},{"heading":"Delivery Update Mode","items":[{"label":"Manual","value":"A person updates the shipment's tracking status by hand. The safe default for a Manual-kind carrier."},{"label":"Poll","value":"Forge periodically asks the carrier API for the latest tracking events and updates the status."},{"label":"Webhook","value":"The carrier pushes status changes to Forge as they happen — the most timely option, available only for carriers that support webhooks."}]},{"heading":"Table Columns","items":[{"label":"Carrier / Code","value":"Name and optional short code. Both sortable."},{"label":"Integration","value":"Manual or Api."},{"label":"Scan","value":"A scanner icon when Requires Scan to Ship is on, otherwise a dash."},{"label":"Credentials","value":"Green 'Configured' chip when credentials exist; yellow 'Not configured' warning chip when an Api carrier has none; a dash for Manual carriers."},{"label":"Active","value":"Active or Inactive chip."},{"label":"Actions","value":"Key (Credentials) and signal (Test) icons — Api carriers only. Test is disabled until credentials exist. There is no edit or deactivate action on a row."}]},{"heading":"Actions","items":[{"label":"New Carrier","value":"Opens the create dialog. data-testid: carrier-new-btn"},{"label":"Credentials (key icon)","value":"Opens the write-only credentials form for an Api carrier. data-testid: carrier-credentials-btn"},{"label":"Test (signal icon)","value":"Runs a live connection test against the carrier API; reports success or the error in a snackbar. Disabled until credentials exist. data-testid: carrier-test-btn"}]}]}"""
        });

        // 16 ── Carriers Quiz ────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Carriers Knowledge Check",
            Slug = "carriers-quiz",
            Summary = "Test your knowledge of the Carriers admin page: Manual vs Api, credentials, the connection test, and delivery update modes.",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 16,
            AppRoutes = """["/admin/carriers"]""",
            Tags = """["admin","shipping","carriers","quiz"]""",
            ContentJson = """{"passingScore":80,"questionsPerQuiz":6,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"ca1","text":"You add a carrier and set its Integration Kind to Manual. Which controls will you NOT see for that carrier?","options":[{"id":"a","text":"The Name and Code fields"},{"id":"b","text":"The Credentials (key) and Test (signal) row actions, and the Integration Service ID field","isCorrect":true},{"id":"c","text":"The Active chip in the table"},{"id":"d","text":"The Delivery Update Mode dropdown"}],"explanation":"A Manual carrier is just a label — your shippers type the tracking number by hand. So it has no credentials, no Test button, and no Integration Service ID field. Only Api carriers surface those because they talk to the live carrier system."},{"id":"ca2","text":"After saving an Api carrier with valid credentials, you reopen the Credentials dialog. What do you notice about the Secret field?","options":[{"id":"a","text":"It shows the saved secret so you can confirm it's correct"},{"id":"b","text":"It is empty — the secret is encrypted server-side and never returned, so you must re-enter it to save again","isCorrect":true},{"id":"c","text":"It is locked and cannot be changed"},{"id":"d","text":"It shows the first and last four characters as a masked preview"}],"explanation":"The secret is write-only. It is encrypted server-side and never read back, so the dialog pre-fills the (non-secret) client ID and environment but leaves the secret blank. You re-enter the secret each time you edit credentials."},{"id":"ca3","text":"The Test (signal) button is greyed out on a carrier row. Why?","options":[{"id":"a","text":"The carrier is set to Manual integration kind, or it's an Api carrier with no credentials saved yet","isCorrect":true},{"id":"b","text":"Your user role can't test carriers"},{"id":"c","text":"The Shipments module is disabled"},{"id":"d","text":"The carrier has already been tested once and can't be re-tested"}],"explanation":"Test only appears on Api carriers, and it stays disabled until credentials exist. If you don't see it at all, the carrier is Manual; if it's there but greyed out, save credentials first. The test runs a live connection against the carrier API."},{"id":"ca4","text":"You want a carrier's tracking status to update the instant the carrier reports a scan event, without Forge having to keep asking. Which Delivery Update Mode supports this?","options":[{"id":"a","text":"Manual"},{"id":"b","text":"Poll"},{"id":"c","text":"Webhook","isCorrect":true},{"id":"d","text":"Auto"}],"explanation":"Webhook means the carrier pushes status changes to Forge as they happen — the most timely option. Poll means Forge periodically asks the carrier API for updates. Manual means a person updates the status by hand. Webhook is available only for carriers that support it."},{"id":"ca5","text":"You need to rename a carrier and change its delivery update mode. What does the Carriers table let you do?","options":[{"id":"a","text":"Click the row's Edit pencil to change any field"},{"id":"b","text":"There is no edit or deactivate row action — the only per-row controls are Credentials and Test (Api carriers only)","isCorrect":true},{"id":"c","text":"Double-click the cell to edit it inline"},{"id":"d","text":"Drag the row to a 'Pending changes' area"}],"explanation":"The Carriers table is intentionally lean. A row exposes only Credentials and Test, and those appear on Api carriers. There is no inline edit or deactivate action, so plan the name, code, and kind before you save."},{"id":"ca6","text":"Before relying on a new Api carrier for real shipments, what's the recommended sequence?","options":[{"id":"a","text":"Save the carrier in production environment and ship a real package to confirm"},{"id":"b","text":"Create the carrier, save credentials with the environment set to sandbox, run Test until it's green, then switch to production","isCorrect":true},{"id":"c","text":"Create the carrier and immediately mark it active — testing isn't needed"},{"id":"d","text":"Email the carrier to confirm the account before configuring anything in Forge"}],"explanation":"Create the carrier, enter credentials with Environment = sandbox, and run the Test until it reports success. Once the connection is verified in sandbox, switch the environment to production. The green Test result is your confirmation the integration works before real shipments depend on it."}]}"""
        });

        // 17 ── Sales Tax Rates (Article) ────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Sales Tax Rates",
            Slug = "sales-tax-rates",
            Summary = "Add, edit, and delete the sales tax rates Forge applies to customers, and understand how default vs state-scoped rates resolve.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 17,
            AppRoutes = """["/admin/sales-tax"]""",
            Tags = """["admin","sales-tax","finance"]""",
            ContentJson = """{"body":"## Sales Tax Rates\n\nThe Sales Tax tab under Admin holds the simple per-state sales tax rates Forge applies when standalone invoicing is in use. (When an external accounting provider is connected, that provider owns tax — these rates back Forge's own invoicing.) This is where you add a rate, edit it, delete it, and decide which one is the system default.\n\n### Reading the list\n\nEach row shows the rate's Name, Code, Rate (formatted as a percentage), a Default chip on the one default rate, and an Active or Inactive chip. The list shows a count of how many rates you have at the top. Click the **Add Rate** button to create one; each row carries an **edit** (pencil) and a **delete** (trash) icon.\n\n### Adding or editing a rate\n\nThe dialog asks for:\n- **Name** (required) — a human label such as 'California Sales Tax'.\n- **Code** (required) — a short identifier such as 'CA-SALES'.\n- **State** — pick a US state, or leave it as '-- All States / Non-state --' for a rate that isn't tied to a specific state.\n- **Rate (%)** (required) — entered as a percentage (for example 8.25), stored internally as a decimal.\n- **Effective From** — the date the rate takes effect.\n- **Description** — optional notes.\n- **Set as default rate** — marks this as the fallback rate (see resolution below).\n- **Exempt rate** — flags this row as an exemption marker (a local concern, for zero/exempt situations).\n- **GL Posting Account** — optional; the ledger account this tax posts to. It's typically populated by the accounting sync when one is configured.\n\nEditing reopens the same dialog pre-filled. Saving updates the row in place.\n\n### Deleting a rate\n\nThe trash icon prompts a confirmation before removing the rate. Deleting is an Admin-only action.\n\n### Default vs state-scoped resolution\n\nWhen Forge needs the tax rate for a customer, it looks at the customer's default billing address **state** and uses the matching state-scoped rate. If no rate matches that state, it falls back to the rate you marked as **default**. This is why exactly one rate should carry the Default chip — it is the safety net for any customer whose state you haven't configured a specific rate for.\n\n### Who can do what\n\nCreating and editing rates is open to Admin and Manager roles; deleting is restricted to Admin.","sections":[]}"""
        });

        // 18 ── Sales Tax Field Reference (QuickRef) ─────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Sales Tax Field Reference",
            Slug = "sales-tax-field-reference",
            Summary = "Reference for every field, column, and action on the Sales Tax admin tab, including Exempt and GL Posting Account.",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 18,
            AppRoutes = """["/admin/sales-tax"]""",
            Tags = """["admin","sales-tax","reference"]""",
            ContentJson = """{"title":"Sales Tax Field Reference","groups":[{"heading":"Rate Dialog Fields","items":[{"label":"Name (required)","value":"Human label for the rate, max 100 characters (e.g. 'California Sales Tax')."},{"label":"Code (required)","value":"Short identifier, max 20 characters (e.g. 'CA-SALES'). Used to reference the rate."},{"label":"State","value":"US state dropdown. Leave as '-- All States / Non-state --' for a rate not tied to a specific state. Drives default-vs-state resolution."},{"label":"Rate (%) (required)","value":"Entered as a percentage (e.g. 8.25). Must be between 0 and 100. Stored internally as a decimal (8.25% → 0.0825)."},{"label":"Effective From","value":"Date the rate takes effect. Defaults to today."},{"label":"Description","value":"Optional free-text notes about the rate."},{"label":"Set as default rate","value":"Toggle. Marks this row as the system fallback rate used when no state-specific rate matches a customer."},{"label":"Exempt rate","value":"Toggle. Flags this row as an exemption marker — a local concern for zero/exempt situations. data-testid: sales-tax-exempt-flag"},{"label":"GL Posting Account","value":"Optional ledger account the tax posts to (e.g. '2200-Sales-Tax'). Typically populated by the accounting sync when a provider is configured. data-testid: sales-tax-gl-posting-account"}]},{"heading":"List Columns","items":[{"label":"Name","value":"Sortable. The rate's label."},{"label":"Code","value":"Sortable. The short identifier."},{"label":"Rate","value":"Sortable. The rate shown as a trimmed percentage (trailing zeros removed)."},{"label":"Default","value":"A Default chip on the single default rate; blank otherwise."},{"label":"Active","value":"Active (green) or Inactive (muted) chip."},{"label":"Actions","value":"Edit (pencil) opens the dialog pre-filled; Delete (trash) prompts a confirmation."}]},{"heading":"Resolution & Permissions","items":[{"label":"Customer resolution","value":"Forge picks the rate matching the customer's default billing address state; if none matches, it uses the rate flagged Default."},{"label":"Default rate","value":"Exactly one rate should be the Default — it's the fallback for any unconfigured state."},{"label":"Create / Edit","value":"Allowed for Admin and Manager roles."},{"label":"Delete","value":"Restricted to Admin only. Always behind a confirmation dialog."},{"label":"Accounting boundary","value":"These rates back Forge's standalone invoicing. When an external accounting provider is connected, that provider owns tax."}]}]}"""
        });

        // 19 ── Capability Detail Page (Article) ─────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Capability Detail Page",
            Slug = "capability-detail-page",
            Summary = "Drill into a single capability to see its dependencies, what depends on it, conflicts, configuration, and audit history.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 19,
            AppRoutes = """["/admin/capabilities"]""",
            Tags = """["admin","capabilities","configuration"]""",
            ContentJson = """{"body":"## Capability Detail Page\n\nClicking the chevron on any row in the Capabilities browser opens that capability's detail page at `/admin/capabilities/:code`. Where the grid is for scanning and bulk-toggling, the detail page is for understanding one capability in depth before you flip it — what it needs, what needs it, and what it conflicts with.\n\n### Header and toggle\n\nThe top of the page shows the capability's name, its code, its functional area, whether it is on by default, and any roles it requires. A **toggle** on the right enables or disables the capability directly from here, with a spinner while the change saves. If the change is blocked, a snackbar explains why — missing dependencies to enable first, dependents to disable first, or a mutually-exclusive peer to turn off first.\n\n### Relationships\n\nThe Relationships section is the heart of the page. It lists three groups as clickable chips:\n- **Depends on** — the capabilities this one needs. Each chip shows whether that prerequisite is currently enabled, so you can see at a glance what's still missing.\n- **Required by** — the capabilities that depend on this one. An empty list reads 'No dependents — safe to disable.'\n- **Mutually exclusive with** — peers that cannot be on at the same time (for example, built-in vs external accounting). This group only appears when a mutex exists.\n\nEvery chip is a link: click it to jump to that related capability's detail page and keep tracing the graph.\n\n### Configuration\n\nSome capabilities carry an opaque JSON configuration payload. The Configuration section shows whether one exists. Editing that payload from the UI is a future iteration — for now it is read-only and changes go through the API directly.\n\n### Recent activity\n\nThe Recent Activity section shows a scoped audit log for just this capability — the last several enable/disable/config/preset-apply actions, each with a timestamp, the action, the user (or '(system)'), and an on→off style summary that includes the reason when one was supplied. This is your record of who changed this capability and when.\n\n### Consultant mode\n\nWith consultant mode on, every relationship chip also shows the raw capability code alongside the friendly name — handy when you're cross-referencing the catalog.","sections":[]}"""
        });

        // 20 ── Custom Configuration (Article) ───────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Custom Capability Configuration",
            Slug = "custom-configuration",
            Summary = "Hand-pick the exact set of capabilities for your install, resolve constraint violations, and apply with a preview.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 20,
            AppRoutes = """["/admin/presets/custom"]""",
            Tags = """["admin","capabilities","presets","configuration"]""",
            ContentJson = """{"body":"## Custom Capability Configuration\n\nWhen no preset fits and you'd rather not toggle capabilities one-by-one in the grid, the Custom Configuration screen lets you hand-pick the exact set you want. You reach it from the Custom card on the Presets browser (`/admin/presets/custom`). It starts from the catalog defaults and lets you build up your install from there.\n\n### How it works\n\nEvery capability is listed grouped by area, each with a checkbox. Toggling a checkbox sets that capability on or off. A capability you change away from its default is tagged with an **Override** chip so it's clear what differs from the out-of-the-box state; capabilities still at their default show a **Default on** tag where applicable. Toggle a capability back to its default and the Override tag disappears.\n\n### Live constraint checking\n\nEvery toggle re-validates against the system's dependency and mutex rules. Three counters at the top keep you oriented: **Capabilities enabled** (how many will be on), **Will change if applied** (the delta versus your current install), and **Constraint violations**. If a violation exists — say you enabled something whose prerequisite is off, or turned on two mutually-exclusive capabilities — a Constraint Violations panel lists each one in plain language, and the **Apply Custom** button is disabled until you resolve them all.\n\n### Reset to defaults\n\nThe **Reset to defaults** button in the toolbar clears every override at once, returning the whole set to the catalog defaults so you can start over cleanly.\n\n### Applying\n\nWhen the violation count is zero, **Apply Custom** opens a preview dialog showing the exact deltas (what turns on, what turns off) and asks you to confirm, optionally with a reason for the audit log. Confirming writes the new configuration, after which you land back on the Capabilities grid. If your selection already matches the current install, applying is a no-op and Forge tells you so.\n\n### How it fits\n\nCustom Configuration, the Presets browser, the Discovery wizard, and the Capabilities grid all write to the same capability state. Use Custom when you want full manual control with the safety net of live violation checking.","sections":[]}"""
        });

        // 21 ── Advanced Reference (Capabilities / Carriers / Tax & Currencies) ──
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Capability system; Carrier integrations; Sales Tax & Currencies — Advanced Reference",
            Slug = "page-specific-reference",
            Summary = "Edge-case and power-user notes for the capability system, carrier integrations, and the sales-tax and currency admin surfaces.",
            ContentType = TrainingContentType.Reference,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 90,
            AppRoutes = """["/admin/capabilities"]""",
            Tags = """["page-specific","reference","advanced"]""",
            ContentJson = """{"body":"## Capability system\n\nEdge-case and admin-config notes beyond the browse/toggle basics.\n\n- **Audit log + apply Reason.** Every mutation (`PUT /api/v1/capabilities/{id}/enabled`, `PUT {id}/config`, `bulk-toggle`, preset/discovery apply) can carry an optional `Reason` string that is persisted on the audit row. The per-capability scoped log lives at `GET /api/v1/capabilities/{id}/audit-log`; the global history page is `/admin/capabilities/audit-log`. Each entry records before/after state, the actor (or '(system)'), the action, a timestamp, and the reason when supplied.\n- **Concurrency / version mismatch.** Toggle and config edits use optimistic concurrency via an `If-Match` header against the capability's current version. A stale version returns **412 Precondition Failed** — reload the row and retry. This prevents two admins from silently clobbering each other.\n- **Dependency / mutex enforcement.** Enabling a capability with an unmet prerequisite, disabling one with active dependents, or enabling a mutex peer returns **409** with a typed envelope (e.g. `capability-mutex-violation`). The bulk-toggle endpoint validates ALL items atomically — the whole batch is rejected if any single item violates a constraint.\n- **Accounting mutex.** The only declared mutex in the catalog is `CAP-ACCT-EXTERNAL ⊥ CAP-ACCT-BUILTIN`. Enabling one while its peer is on returns the mutex 409; disable the peer first. `CAP-ACCT-FULLGL` is an aspirational placeholder — never enabled; gating returns **403** with a 'not yet available' tone.\n- **Discovery branch model + jump-to-recommendation.** The Discovery wizard tracks position in the URL (`?step=N`) and surfaces a live leaning recommendation with a confidence level as you answer. The final screen lets you jump straight to the recommended preset or pick an alternative; **Skip Discovery** drops you on the Capabilities grid to configure by hand.\n- **Compare matrix power options.** On `/admin/presets`, Compare mode lets you select two to four presets (the Custom card is excluded) and opens a side-by-side diff matrix before you commit. Consultant mode reveals raw `CAP-*` codes throughout the grid, detail page, and relationship chips.\n\n## Carrier integrations\n\nAncillary fields and lifecycle notes for `/admin/carriers` (gated by `CAP-O2C-SHIP`; roles Admin, Manager, OfficeManager).\n\n- **Integration Service ID + SCAC.** Create accepts both an `IntegrationServiceId` (maps the row to the underlying shipping integration) and a `Scac` (Standard Carrier Alpha Code) alongside name, code, integration kind, delivery update mode, and `RequiresScanToShip`. The Service ID field shows only for Api-kind carriers.\n- **requires-scan-to-ship enforcement.** `RequiresScanToShip` (default ON) is enforced downstream on the Shipments module — a package must be scanned before it can be marked shipped for that carrier. It is a per-carrier flag set at creation.\n- **Account-number requirements.** The credentials form (`PUT /api/v1/carriers/{id}/credentials`) takes `ClientId`, `Secret`, an optional `AccountNumber`, and `Environment`. Some carriers require the account number for rating/labeling; the secret is write-only (encrypted server-side, never returned).\n- **Sandbox → production promotion.** `Environment` is `sandbox` or `production`. Configure in sandbox, run `POST /api/v1/carriers/{id}/test` until it reports success, then re-save credentials with `Environment = production`. Switching environments is a credentials re-save, not a separate action.\n- **Active / inactive lifecycle.** `GET /api/v1/carriers?activeOnly=true` is the default — inactive carriers are hidden unless `activeOnly=false`. There is no per-row edit or deactivate control in the table; lifecycle changes go through the API. Active/inactive status feeds rate comparison, label creation, and tracking visibility.\n\n## Sales Tax & Currencies\n\nValidation, gating, and FX-provenance notes for the tax and currency admin surfaces.\n\n- **Sales Tax validation + role gating.** `/admin/sales-tax` is gated by `CAP-MD-TAXCODES`. Read is open to any authenticated user; **create/edit require Admin or Manager**; **delete is Admin-only**. Validation: `Name` required (≤100), `Code` required (≤20), `GlPostingAccount` ≤100, and `Rate` is `InclusiveBetween(0, 1)` — stored as a decimal fraction (0.07 = 7%), even though the dialog accepts a percentage. Code is unique.\n- **Currency catalog validation + CAP-MD-CURRENCIES.** Both `/admin/currencies` and the exchange-rate endpoints are gated by `CAP-MD-CURRENCIES`; both controllers additionally require the **Admin** role. The catalog carries code, name, symbol, decimal places (JPY = 0, most = 2), a base flag, an active flag, and sort order; the base currency is resolved from the `currency.base` system setting (5-minute cache).\n- **Exchange-rate convert endpoint + source provenance.** `GET /api/v1/admin/exchange-rates/convert?fromCurrencyId=&toCurrencyId=&amount=&date=` returns `{ convertedAmount }`. The FX layer is currently a stub: `ConvertAsync` returns the input amount and `GetExchangeRateAsync` returns 1.0, so a conversion is effectively identity until a real FX provider ships. Stored rates carry an `ExchangeRateSource` of `Manual`, `Api`, or `Bank` — manually entered rates are always stamped `Manual` with a null `FetchedAt`.\n- **Self-pair guard.** Converting or rating a currency against itself resolves to a rate of 1.0 and returns the amount unchanged (the stub returns the input), so a same-currency pair is a safe no-op rather than an error.\n- **History filtering.** `GET /api/v1/admin/exchange-rates` accepts `fromCurrencyId`, `toCurrencyId`, `dateFrom`, and `dateTo` filters and returns newest-first. The rate table is **append-only**: setting a rate for an existing (from, to, effectiveDate) triple updates that row, but a new effective date adds a superseding row, preserving the audit trail.","sections":[]}"""
        });

        Log.Information("Seeded page-specific training modules");
    }
}
