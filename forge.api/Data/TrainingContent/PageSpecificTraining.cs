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
            ContentJson = """{"body":"## Shipping Carriers\n\nThis is the one place your shipping and receiving team sets up every carrier you ship with, and the only place you enter a carrier's sign-in details. Each row is a carrier — an integrated one such as UPS, FedEx, USPS, or DHL, or a custom shipper you set up yourself, like a house courier or a freight broker.\n\n### Key actions\n\n- **New Carrier** opens a window where you set the name, code, the Integration type (Manual / custom or API integration), how Delivery Updates arrive (Manual, Poll tracking, or Webhook), and whether a label scan is required before a package ships.\n- **Credentials** (the key icon) opens a form for the carrier's sign-in details — the API key or client ID, the secret, an optional account number, and whether you're on test or live. The secret is kept safe and never shown back, so you re-enter it each time you edit.\n- **Test** (the signal icon) checks the live connection to the carrier and tells you whether it worked. It stays greyed out until sign-in details are saved.\n\n### Manual carriers vs API-integrated carriers\n\nThe **Integration** type you choose changes everything else on the row. A **Manual / custom** carrier is just a label — your shippers type a tracking number by hand, so there are no sign-in details, no Test button, and no service link. An **API integration** carrier connects to the live carrier system, so it shows the Integration Service ID field when you create it, adds the key and signal icons to its row, and shows a yellow warning until sign-in details are saved. Pick Manual when you only need to record a tracking number. Pick API integration when you want live rates, printed labels, and automatic tracking on the Shipments page.\n\n### How Delivery Updates arrive\n\nThis controls how a shipment's tracking status refreshes after it ships. **Manual** means someone updates the status by hand. **Poll tracking** means Forge checks the carrier for the latest tracking events every so often. **Webhook** means the carrier sends status changes to Forge the moment they happen — the most up-to-date option, but only for carriers that support it. Manual is the safe default for a manual carrier.\n\n### There is no row edit or deactivate\n\nThe table is kept simple on purpose: a carrier row has no edit or deactivate button. The only per-row controls are Credentials and Test, and those appear on API-integrated carriers only. So plan the name, code, and type before you save.\n\n### Reading the table\n\nColumns show the name, code, Integration type, whether a label scan is required, a sign-in status (green when set, a warning when an API carrier has none), and whether the carrier is active. Carriers you set up here power rate comparison, label printing, and tracking on the Shipments page — get the sign-in details right and Test passing before you rely on a carrier for real shipments.","sections":[]}"""
        });

        // 2 ── Capabilities ──────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Capabilities Browser",
            Slug = "capabilities-overview",
            Summary = "Browse and switch on or off the features that make up your Forge install.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/admin/capabilities"]""",
            Tags = """["admin","capabilities","configuration"]""",
            ContentJson = """{"body":"## Capabilities Browser\n\nForge is one product, and each feature can be switched on or off. This page is where you browse those features, grouped by area (Accounting, Logistics, Quality, and so on), and turn each one on or off for your install.\n\n### Key actions\n\n- **Search** filters as you type by name or description. **Area** narrows to one area, **Enabled only** hides what's off, and **Consultant mode** shows the underlying feature codes (hidden by default).\n- Each row has an on/off switch. Flipping it saves right away, with a brief spinner; if it can't be saved, it flips back.\n- Click the arrow at the end of a row to open that feature's detail page.\n\n### Dependencies and conflicts\n\nSome features need others to be on first, and a few can't be on at the same time. If you turn one on that's missing something it needs, a message lists what to turn on first. Turn one off that others rely on, and it lists what depends on it. And when two features conflict (such as built-in vs external accounting), it asks you to turn the other one off first.\n\n### Getting started\n\nOn a fresh install, a banner offers to **Run Discovery** (a short questionnaire) or **Browse Presets** (ready-made bundles), and both apply their changes here. You can also set everything up by hand from this page.","sections":[]}"""
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
            ContentJson = """{"body":"## Discovery Wizard\n\nDiscovery is a short questionnaire that recommends the right setup for your business, so you don't have to turn features on and off one by one. It's the fastest way to set up a fresh install.\n\n### How it works\n\nYou answer about 22 questions about your business — its size, whether you're regulated, whether you have multiple sites, and so on. Some questions let you pick one answer, some let you pick several, and a few open text boxes are optional but sharpen the recommendation. **Next** and **Back** move between steps, and your place is saved as you go, so the browser's back button works too.\n\n### The recommendation\n\nAs you answer, a side panel shows which setup you're leaning toward and how confident it is. The final screen shows the recommended setup with its name, a confidence level, the reasons behind it, and any alternatives you can pick instead. A list shows exactly which features would be turned on (green) or off (red).\n\n### Applying\n\n**Apply** opens a preview showing those same changes and any conflicts before you commit. **Consultant mode** adds more questions, and **Skip Discovery** takes you straight back to the Capabilities page to set things up by hand.","sections":[]}"""
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
            ContentJson = """{"body":"## Capability Presets\n\nPresets are ready-made bundles of features tuned for common kinds of business. This page shows eight preset cards — seven named profiles plus a Custom option — as an alternative to answering the Discovery questionnaire or turning features on one by one.\n\n### Reading the cards\n\nEach card shows the preset name, a short description, the kind of business it suits (for example Small Retail or Food Manufacturing), a few recommended-for tags, and how many features it turns on. The preset you're using now is marked with a green **Active** label.\n\n### Key actions\n\n- **Click a card** to open its details, where you see its full feature list and an apply button. The Custom card opens the set-it-up-by-hand flow.\n- **Compare** lets you pick two to four presets (Custom is left out) and shows them side by side, so you can weigh the differences before deciding.\n- **Back to Capabilities** returns to the main list.\n\n### How it fits\n\nApplying a preset shows you the changes to confirm first, then updates your install. Presets, Discovery, and the Capabilities page all change the same settings, so you can start from a preset and fine-tune from there.","sections":[]}"""
        });

        // 5 ── Currencies ────────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Currencies & FX Rates",
            Slug = "currencies-overview",
            Summary = "Maintain the list of currencies you work in and a running history of daily exchange rates.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 5,
            AppRoutes = """["/admin/currencies"]""",
            Tags = """["admin","currencies","finance"]""",
            ContentJson = """{"body":"## Currencies & FX Rates\n\nThis page has two parts: the list of currencies and the history of exchange rates. Together they support working in more than one currency across invoicing, money you're owed and money you owe, and financial reports.\n\n### Currencies\n\nThe list shows each currency with its code, name, symbol, how many decimal places it uses, whether it's the base currency, whether it's active, and its display order. **New Currency** opens a window to add one — set the code, name, symbol, decimal places (the yen uses 0, most use 2), whether it's the base currency, whether it's active, and where it sits in the list. Click any row to edit it. The base currency is the one everything else is reported against.\n\n### Exchange rates\n\nThe rate history keeps every rate you enter. Adding a new rate for a currency pair on a new date adds a fresh entry that takes over from the old one, rather than replacing it — so you always have a record of past rates. **New Exchange Rate** (available once you have at least two currencies) lets you pick the from and to currencies, the date it takes effect, the rate, and where it came from. The list shows the newest first, with the date, the pair, the rate to several decimals, the source, and when it was recorded.","sections":[]}"""
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
            ContentJson = """{"body":"## Role Templates\n\nA role template bundles several roles into one named set, so someone who wears many hats can be given all the right access at once, rather than role by role. This page manages those templates.\n\n### Reading the table\n\nEach row shows the template name, its description, the roles it includes, how many people are using it, and whether it's a built-in template or one you made. Built-in templates come with your install and can't be changed — they show a lock, and hovering suggests copying one to customize it.\n\n### Key actions\n\n- **New Template** makes a custom template. Give it a name (required, up to 100 characters), an optional description, and pick at least one role to include.\n- **Edit** (custom templates only) reopens the window to change the name, description, or included roles.\n- **Delete** (custom templates only) turns off the template and removes it from everyone using it; a confirmation tells you how many people are affected. Their other access isn't changed.\n\n### How it fits\n\nWhen you give a template to someone, they get every role it includes. This keeps access consistent across staff with similar responsibilities.","sections":[]}"""
        });

        // 7 ── Automations ───────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Automations — Event Failures",
            Slug = "automations-overview",
            Summary = "Keep an eye on the automatic background tasks that failed, then retry or resolve them.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 7,
            AppRoutes = """["/admin/automations"]""",
            Tags = """["admin","automations","reliability"]""",
            ContentJson = """{"body":"## Automations — Event Failures\n\nForge quietly does a lot of follow-up work on its own — for example, once a job or an order is created, it kicks off the next step without anyone clicking a button. This page shows admins the automatic tasks that failed, so a hiccup never slips by unnoticed.\n\n### Reading the table\n\nEach row is a failed task. It shows what set it off, what it was trying to do, a short error message (hover for the full text), its status by color (red for failed, orange for retrying, green for resolved), how many times it's been retried, when it first failed, and when it was last retried. Click a row to open a panel with the full error and the related details.\n\n### Key actions\n\n- The **status filter** narrows to All, Failed, Retrying, or Resolved, and **Refresh** reloads the list.\n- **Retry** (on a failed row) runs the task again, after you confirm.\n- **Resolve** (on a failed or retrying row) marks the failure as handled without retrying — useful once you've fixed the cause or decided a retry isn't needed.\n\n### How it fits\n\nThese controls are a troubleshooting tool for admins. A pile-up of failures points to a problem with an outside service or something inside Forge that couldn't finish its work.","sections":[]}"""
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
            ContentJson = """{"body":"## Customer Segments\n\nCustomer segments are saved groups of customers — for example high-value aerospace accounts, ITAR-ready customers, dormant accounts, or those on credit watch. They let you focus on a defined group for campaigns, outreach, or managing risk.\n\n### What you see today\n\nThis page is a **preview** for now. It shows a few example segments as cards so you can see what the feature will do; creating, editing, and deleting segments isn't available yet. Each example card shows a segment name, a short description, a plain-language summary of what it groups (such as aerospace customers who owe more than a set amount), and a rough count of matching customers.\n\n### How the grouping works\n\nThe examples group customers by real details — whether they're marked aerospace or ITAR, how much they owe, their credit limit and whether they're on credit hold, whether their references checked out, whether they're active, and when they last ordered. These are the same details the finished feature will let you combine into your own saved segments.\n\n### How it fits\n\nWhen it's finished, segments will let you build your own groups, save and manage them, and pick matching customers straight from the customer list to act on in bulk.","sections":[]}"""
        });

        // 9 ── Portal Access ─────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Customer Portal Access",
            Slug = "portal-access-overview",
            Summary = "Set up and switch on or off self-service portal sign-ins for individual customer contacts.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 9,
            AppRoutes = """["/customers/portal-access"]""",
            Tags = """["customers","portal","access"]""",
            ContentJson = """{"body":"## Customer Portal Access\n\nThis page controls which of your customers' contacts can sign in to the self-service portal, where they view their invoices, quotes, and shipments. It lists everyone who's been set up and lets you grant or remove access from one place.\n\n### Reading the table\n\nEach row shows the contact name (Last, First), the customer they belong to, their email, when they last signed in (or \"Never signed in\"), when their access was created, and an on/off switch. Click a row to open that customer's contacts page for context.\n\n### Key actions\n\n- **Provision Portal Access** opens a list of contacts you can add — those who have an email address but no portal account yet. Their email is how they sign in; there's no separate username.\n- The on/off switch on each row turns a contact's sign-in on or off. It's briefly disabled while the change saves.\n\n### Requirements\n\nA contact can use the portal only when all three are true: they have an email address, they've been set up for access, and that access is turned on. Adding a contact who already has access is safe — nothing breaks if you do it twice.","sections":[]}"""
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
            ContentJson = """{"body":"## Lead Work Queue\n\nThe lead queue is a screen for calling through leads one at a time. You pull a batch, call each lead, and record how it went — a keyboard-friendly flow built for speed.\n\n### Working the queue\n\n**Pull Leads** brings the next batch into your queue (a size you can set, five by default). Each lead card shows the company, contact, email, a phone number you can click to call, where the lead came from, when it was last worked, and any notes, plus labels for its campaign, cooldown date, or opt-out status. A counter shows where you are in the batch, and the J and K keys (or the arrows) move between leads.\n\n### Recording the outcome\n\nSix buttons, each with a keyboard shortcut, record the result and move you to the next lead: Engaged (E), No Answer (N), Voicemail Left (V), Callback Scheduled (C), Bad Data (B), and Suppressed (S). Callback Scheduled opens a small window to pick a date and time; cancelling it drops the outcome. You can add notes that save with each one.\n\n### How it fits\n\nEach outcome updates the lead's status and feeds campaign reporting and follow-up scheduling, moving new leads into the sales pipeline.","sections":[]}"""
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
            ContentJson = """{"body":"## Lead Campaigns\n\nCampaigns organize your lead outreach by strategy. Each campaign is a label that imported leads are tagged with, grouping them for batch calling and reporting.\n\n### Reading the table\n\nThe list shows each campaign's name, strategy, number of leads, whether it's active, its start date, and when it was created, and you can sort by any column. The lead count updates on its own as leads come in. A label shows whether the campaign is active or inactive.\n\n### Key actions\n\n- **New Campaign** opens the create window. Click a row or its edit icon to change an existing campaign.\n- In the window, set the name (required, up to 200 characters), an optional description, and a strategy: Cold Call, Cold Email, Trade Show Follow-up, Webinar Attendee, List Purchase, or Manual Entry. The strategy is set when you create the campaign and can't be changed later.\n- You can set a default cooldown (0 to 730 days) that keeps a minimum wait between call attempts, optional start and end dates, and — when editing — an active on/off switch.\n\n### How it fits\n\nThe strategy shapes calling scripts and follow-up timing, and the cooldown controls how soon a lead can be called again. Turning a campaign off keeps it out of the calling queue without deleting its history, so managers can pause outreach cleanly.","sections":[]}"""
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
            ContentJson = """{"body":"## Bulk Lead Intake\n\nThis page imports many leads at once from a spreadsheet file or pasted text — ideal for trade-show lists, webinar sign-ups, or purchased lists. It checks the rows, removes duplicates, and adds them in three guided steps.\n\n### The flow\n\nFirst, choose a strategy (Cold Call, Cold Email, Trade Show, Webinar, List Purchase, or Manual) and an optional campaign to tag them with. Then upload a CSV or text file, or paste text separated by commas or tabs and click Parse Paste. Finally, click **Preview** to check the rows, review the results, and click **Commit** to add the good ones; when it succeeds, you return to the leads list.\n\n### Columns and checks\n\nThe importer understands common column names — for example company, tel, and contact all land in the right place — and a help table you can open lists them all. Each row needs at least a company name, email, or phone; rows missing all three are dropped. The preview labels each row: Created (will be added); a duplicate label when the lead, contact, or another row in the batch already matches; Suppressed or In Cooldown when someone opted out or was called too recently; and Missing Required Field or Invalid when a row doesn't check out. A summary counts Created, Skipped, and Total.\n\n### How it fits\n\nCreated leads go into the work queue for calling. The duplicate and opt-out checks keep your pipeline clean and compliant.","sections":[]}"""
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
            ContentJson = """{"body":"## Recurring Orders\n\nRecurring orders are templates that create a fresh sales order on a schedule you set — ideal for standing or repeat customers, such as consumables or recurring service. Each night, Forge turns every template that's due into a real sales order with its lines copied over.\n\n### Reading the table\n\nEach template shows its name, customer, how often it runs (in days), the next date it will run, the last date it ran (\"Never\" until it first runs), how many lines it has, and whether it's active. You can sort by any column. Inactive templates are skipped each night.\n\n### Key actions\n\n- **New Recurring** opens the create window. There's no in-place edit — to change a template, delete it and make a new one, which keeps things reliable.\n- The **delete** icon permanently removes a template and stops it from running again, after you confirm.\n- In the window, set a name (up to 200 characters), pick the customer, choose the next run date, set how often it runs in days (1 to 365 — 30 is about monthly), and add optional notes. Then add at least one line, each with a part, description, quantity, and unit price.\n\n### How it fits\n\nEach night, Forge finds templates whose next run date has arrived, creates a sales order from their lines, and moves the next run date forward.","sections":[]}"""
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
            ContentJson = """{"appRoute":"/admin/carriers","startButtonLabel":"Tour Carriers","steps":[{"element":"[data-testid='carrier-new-btn']","popover":{"title":"New Carrier","description":"Click here to add a carrier. You'll set a name, an optional code, the Integration type (Manual / custom or API integration), how Delivery Updates arrive (Manual, Poll tracking, or Webhook), and whether a label scan is required before a package ships. Choose API integration for live rates, labels, and tracking. Choose Manual when you only type a tracking number by hand.","side":"bottom"}},{"element":"app-data-table","popover":{"title":"The Carriers Table","description":"Every carrier is a row here. You'll see the name, code, Integration type, whether a label scan is required, its sign-in status, and whether it's active. An API-integrated carrier with no sign-in details shows a yellow warning — your cue to open Credentials next. A row has no edit or deactivate button; the only per-row controls are Credentials and Test, and they appear on API-integrated carriers only.","side":"top"}},{"element":"[data-testid='carrier-credentials-btn']","popover":{"title":"Enter Credentials","description":"On an API-integrated carrier, the key icon opens the sign-in form: the API key or client ID, the secret, an optional account number, and whether you're on test or live. The secret is kept safe and never shown back, so you re-enter it each time you edit. Save it to turn the sign-in status green.","side":"left"}},{"element":"[data-testid='carrier-test-btn']","popover":{"title":"Test the Connection","description":"The signal icon checks the live connection to the carrier and tells you whether it worked. It stays greyed out until sign-in details are saved. Get this passing on test before you rely on the carrier for real shipments.","side":"left"}}]}"""
        });

        // 15 ── Carriers Field Reference ─────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Carriers Field Reference",
            Slug = "carriers-field-reference",
            Summary = "Reference for every field, label, and action on the Carriers admin page, including Manual vs API integration and the three delivery-update options.",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 15,
            AppRoutes = """["/admin/carriers"]""",
            Tags = """["admin","shipping","carriers","reference"]""",
            ContentJson = """{"title":"Carriers Field Reference","groups":[{"heading":"New Carrier Dialog","items":[{"label":"Name (required)","value":"The carrier's name, up to 100 characters (e.g. 'UPS', 'FedEx', 'Local Courier')."},{"label":"Code","value":"An optional short code, up to 50 characters, used to spot the carrier in lists and on documents."},{"label":"Integration (required)","value":"Manual / custom or API integration. Manual is a label only — no sign-in details, no test. API integration connects to the live carrier and adds the service ID field plus the Credentials and Test buttons. Starts as Manual."},{"label":"Delivery Updates (required)","value":"Manual, Poll tracking, or Webhook — how tracking status refreshes after a shipment leaves. Starts as Manual."},{"label":"Integration Service ID","value":"Shown only for API-integrated carriers. Names which live carrier service to use (ups, fedex, usps, dhl). Up to 50 characters."},{"label":"Require label scan before shipping","value":"On/off switch (on by default). When on, a package must be scanned before it can be marked shipped for this carrier."}]},{"heading":"Credentials Dialog (API-integrated carriers only)","items":[{"label":"API Key / Client ID (required)","value":"The carrier's key or account ID, up to 200 characters. Not secret — it's shown filled in when you reopen the form."},{"label":"API Secret (required)","value":"The carrier's secret key. It's kept safe and never shown back, so you re-enter it each time you edit."},{"label":"Account Number","value":"An optional carrier account number, up to 50 characters."},{"label":"Environment (required)","value":"Test or live. Use test to check the connection without touching real shipments; switch to live when you're ready."}]},{"heading":"Integration type — what changes","items":[{"label":"Manual / custom","value":"No sign-in details, no Test button, no service ID field. Your shippers type the tracking number by hand. Use when you don't need live rates or labels."},{"label":"API integration","value":"Adds the service ID field when you create it, plus the key (Credentials) and signal (Test) icons in the row. Powers live rate comparison, label creation, and automatic tracking on the Shipments page."}]},{"heading":"Delivery Updates","items":[{"label":"Manual","value":"Someone updates the shipment's tracking status by hand. The safe default for a manual carrier."},{"label":"Poll tracking","value":"Forge checks the carrier for the latest tracking events every so often and updates the status."},{"label":"Webhook","value":"The carrier sends status changes to Forge as they happen — the most up-to-date option, available only for carriers that support it."}]},{"heading":"Table Columns","items":[{"label":"Carrier / Code","value":"Name and optional short code. Sort by either."},{"label":"Integration","value":"Manual / custom or API integration."},{"label":"Scan","value":"A scanner icon when a label scan is required to ship, otherwise a dash."},{"label":"Credentials","value":"A green 'Configured' label when sign-in details exist; a yellow 'Not configured' warning when an API carrier has none; a dash for manual carriers."},{"label":"Active","value":"Shows whether the carrier is active or inactive."},{"label":"Actions","value":"The key (Credentials) and signal (Test) icons — API-integrated carriers only. Test is greyed out until sign-in details exist. A row has no edit or deactivate button."}]},{"heading":"Actions","items":[{"label":"New Carrier","value":"Opens the create window."},{"label":"Credentials (key icon)","value":"Opens the sign-in form for an API-integrated carrier."},{"label":"Test (signal icon)","value":"Checks the live connection to the carrier and tells you whether it worked. Greyed out until sign-in details exist."}]}]}"""
        });

        // 16 ── Carriers Quiz ────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Carriers Knowledge Check",
            Slug = "carriers-quiz",
            Summary = "Test your knowledge of the Carriers admin page: Manual vs API integration, credentials, the connection test, and delivery update modes.",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 16,
            AppRoutes = """["/admin/carriers"]""",
            Tags = """["admin","shipping","carriers","quiz"]""",
            ContentJson = """{"passingScore":80,"questionsPerQuiz":6,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"ca1","text":"You add a carrier and set its Integration to Manual / custom. Which controls will you NOT see for that carrier?","options":[{"id":"a","text":"The Name and Code fields"},{"id":"b","text":"The Credentials (key) and Test (signal) buttons, and the service ID field","isCorrect":true},{"id":"c","text":"Whether the carrier is active in the table"},{"id":"d","text":"The Delivery Updates setting"}],"explanation":"A manual carrier is just a label — your shippers type the tracking number by hand. So it has no sign-in details, no Test button, and no service ID field. Only API-integrated carriers show those, because they connect to the live carrier."},{"id":"ca2","text":"After saving an API-integrated carrier with valid sign-in details, you reopen the Credentials form. What do you notice about the Secret field?","options":[{"id":"a","text":"It shows the saved secret so you can confirm it's correct"},{"id":"b","text":"It's empty — the secret is kept safe and never shown back, so you must re-enter it to save again","isCorrect":true},{"id":"c","text":"It's locked and can't be changed"},{"id":"d","text":"It shows the first and last four characters as a masked preview"}],"explanation":"The secret is never shown back. It's kept safe, so the form fills in the API key and environment but leaves the secret blank. You re-enter the secret each time you edit the sign-in details."},{"id":"ca3","text":"The Test (signal) button is greyed out on a carrier row. Why?","options":[{"id":"a","text":"The carrier is set to Manual / custom, or it's an API-integrated carrier with no sign-in details saved yet","isCorrect":true},{"id":"b","text":"Your role can't test carriers"},{"id":"c","text":"The Shipments feature is turned off"},{"id":"d","text":"The carrier has already been tested once and can't be tested again"}],"explanation":"Test only appears on API-integrated carriers, and it stays greyed out until sign-in details exist. If you don't see it at all, the carrier is manual; if it's there but greyed out, save the sign-in details first. The test checks a live connection to the carrier."},{"id":"ca4","text":"You want a carrier's tracking status to update the moment the carrier reports a scan, without Forge having to keep asking. Which Delivery Updates option does this?","options":[{"id":"a","text":"Manual"},{"id":"b","text":"Poll tracking"},{"id":"c","text":"Webhook","isCorrect":true},{"id":"d","text":"Auto"}],"explanation":"Webhook means the carrier sends status changes to Forge as they happen — the most up-to-date option. Poll tracking means Forge checks the carrier for updates every so often. Manual means someone updates the status by hand. Webhook works only for carriers that support it."},{"id":"ca5","text":"You need to rename a carrier and change how its tracking updates arrive. What does the Carriers table let you do?","options":[{"id":"a","text":"Click the row's edit pencil to change any field"},{"id":"b","text":"There's no edit or deactivate button — the only per-row controls are Credentials and Test (API-integrated carriers only)","isCorrect":true},{"id":"c","text":"Double-click the cell to edit it in place"},{"id":"d","text":"Drag the row to a 'Pending changes' area"}],"explanation":"The Carriers table is kept simple on purpose. A row shows only Credentials and Test, and those appear on API-integrated carriers. There's no in-place edit or deactivate, so plan the name, code, and type before you save."},{"id":"ca6","text":"Before relying on a new API-integrated carrier for real shipments, what's the recommended order?","options":[{"id":"a","text":"Save the carrier in live mode and ship a real package to confirm"},{"id":"b","text":"Create the carrier, save the sign-in details on test, run Test until it passes, then switch to live","isCorrect":true},{"id":"c","text":"Create the carrier and mark it active right away — no testing needed"},{"id":"d","text":"Email the carrier to confirm the account before setting anything up in Forge"}],"explanation":"Create the carrier, enter the sign-in details on test, and run Test until it passes. Once the connection works on test, switch to live. A passing test is your sign that the connection works before real shipments depend on it."}]}"""
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
            ContentJson = """{"body":"## Sales Tax Rates\n\nThe Sales Tax tab under Admin holds the simple per-state sales tax rates Forge applies when you invoice through Forge itself. (When you're connected to an outside accounting system, that system handles tax — these rates only back Forge's own invoicing.) Here you add a rate, edit it, delete it, and decide which one is the default.\n\n### Reading the list\n\nEach row shows the rate's name, code, rate (as a percentage), a Default label on the one default rate, and whether it's active. A count of your rates sits at the top. Click **Add Rate** to create one; each row has an **edit** (pencil) and a **delete** (trash) icon.\n\n### Adding or editing a rate\n\nThe window asks for:\n- **Name** (required) — a plain label such as 'California Sales Tax'.\n- **Code** (required) — a short identifier such as 'CA-SALES'.\n- **State** — pick a US state, or leave it as '-- All States / Non-state --' for a rate that isn't tied to one state.\n- **Rate (%)** (required) — entered as a percentage (for example 8.25).\n- **Effective From** — the date the rate takes effect.\n- **Description** — optional notes.\n- **Set as default rate** — makes this the fallback rate (see how it's chosen below).\n- **Exempt rate** — marks this row as an exemption, for zero or tax-exempt situations.\n- **Posting account** — optional; the accounting account this tax posts to. It's usually filled in automatically when you're connected to an accounting system.\n\nEditing reopens the same window, filled in. Saving updates the row in place.\n\n### Deleting a rate\n\nThe trash icon asks you to confirm before removing the rate. Only an Admin can delete.\n\n### How the right rate is chosen\n\nWhen Forge needs a customer's tax rate, it looks at the **state** on their default billing address and uses the matching rate. If no rate matches that state, it falls back to the one you marked as the **default**. That's why exactly one rate should be the default — it's the safety net for any customer whose state you haven't set up a specific rate for.\n\n### Who can do what\n\nAdmins and Managers can create and edit rates; only Admins can delete.","sections":[]}"""
        });

        // 18 ── Sales Tax Field Reference (QuickRef) ─────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Sales Tax Field Reference",
            Slug = "sales-tax-field-reference",
            Summary = "Reference for every field, column, and action on the Sales Tax admin tab, including the Exempt switch and the Posting Account.",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 18,
            AppRoutes = """["/admin/sales-tax"]""",
            Tags = """["admin","sales-tax","reference"]""",
            ContentJson = """{"title":"Sales Tax Field Reference","groups":[{"heading":"Rate Dialog Fields","items":[{"label":"Name (required)","value":"A plain label for the rate, up to 100 characters (e.g. 'California Sales Tax')."},{"label":"Code (required)","value":"A short identifier, up to 20 characters (e.g. 'CA-SALES'), used to refer to the rate."},{"label":"State","value":"Pick a US state, or leave it as '-- All States / Non-state --' for a rate not tied to one state. This is how Forge chooses which rate to use."},{"label":"Rate (%) (required)","value":"Entered as a percentage (e.g. 8.25). Must be between 0 and 100."},{"label":"Effective From","value":"The date the rate takes effect. Defaults to today."},{"label":"Description","value":"Optional notes about the rate."},{"label":"Set as default rate","value":"An on/off switch. Makes this the fallback rate, used when no state-specific rate matches a customer."},{"label":"Exempt rate","value":"An on/off switch. Marks this row as an exemption, for zero or tax-exempt situations."},{"label":"Posting account","value":"An optional accounting account the tax posts to (e.g. '2200-Sales-Tax'). Usually filled in automatically when you're connected to an accounting system."}]},{"heading":"List Columns","items":[{"label":"Name","value":"Sortable. The rate's label."},{"label":"Code","value":"Sortable. The short identifier."},{"label":"Rate","value":"Sortable. The rate shown as a tidy percentage (no trailing zeros)."},{"label":"Default","value":"A Default label on the one default rate; blank otherwise."},{"label":"Active","value":"Shows whether the rate is active (green) or inactive (muted)."},{"label":"Actions","value":"Edit (pencil) opens the window filled in; Delete (trash) asks you to confirm."}]},{"heading":"Resolution & Permissions","items":[{"label":"Which rate a customer gets","value":"Forge uses the rate matching the state on the customer's default billing address; if none matches, it uses the default rate."},{"label":"Default rate","value":"Exactly one rate should be the default — it's the fallback for any state you haven't set up."},{"label":"Create / Edit","value":"Allowed for Admins and Managers."},{"label":"Delete","value":"Admins only, and always behind a confirmation."},{"label":"Accounting boundary","value":"These rates back Forge's own invoicing. When you're connected to an outside accounting system, that system handles tax."}]}]}"""
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
            ContentJson = """{"body":"## Capability Detail Page\n\nClick the arrow on any row in the Capabilities browser to open that feature's detail page. Where the main list is for scanning and switching things on and off quickly, the detail page is for understanding one feature in depth before you change it — what it needs, what needs it, and what it conflicts with.\n\n### Header and switch\n\nThe top of the page shows the feature's name, its code, its area, whether it's on by default, and any roles it needs. An on/off switch on the right turns the feature on or off right here, with a spinner while it saves. If the change is blocked, a message explains why — things to turn on first, things that depend on it, or a conflicting feature to turn off first.\n\n### Relationships\n\nThe Relationships section is the heart of the page. It lists three groups you can click through:\n- **Depends on** — the features this one needs. Each one shows whether it's currently on, so you can see at a glance what's still missing.\n- **Required by** — the features that depend on this one. An empty list reads 'No dependents — safe to disable.'\n- **Conflicts with** — features that can't be on at the same time (for example, built-in vs external accounting). This group appears only when there's a conflict.\n\nEach one is a link: click it to jump to that related feature's detail page and keep exploring.\n\n### Configuration\n\nSome features carry extra settings. The Configuration section shows whether any exist. Editing them from this screen is coming later — for now it's view-only.\n\n### Recent activity\n\nThe Recent Activity section shows a history for just this feature — the last several times it was turned on or off, reconfigured, or changed by a preset, each with the date, what happened, who did it (or 'system'), and the reason when one was given. It's your record of who changed this feature and when.\n\n### Consultant mode\n\nWith consultant mode on, each related feature also shows its underlying code next to the friendly name — handy when you're cross-referencing.","sections":[]}"""
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
            ContentJson = """{"body":"## Custom Capability Configuration\n\nWhen no preset fits and you'd rather not switch features on one by one in the main list, the Custom Configuration screen lets you hand-pick the exact set you want. You reach it from the Custom card on the Presets browser. It starts from the standard defaults and lets you build up your install from there.\n\n### How it works\n\nEvery feature is listed by area, each with a checkbox. Checking or unchecking one turns it on or off. A feature you change away from its default is marked **Override**, so it's clear what differs from the standard setup; features still at their default show a **Default on** tag where it applies. Set a feature back to its default and the Override mark goes away.\n\n### Live checking\n\nEach change is checked against the rules — which features need others, and which can't be on together. Three counters at the top keep you oriented: **Capabilities enabled** (how many will be on), **Will change if applied** (how it differs from your install now), and **Conflicts**. If there's a conflict — say you turned on something that's missing what it needs, or turned on two features that can't both be on — a panel lists each one in plain language, and the **Apply Custom** button stays disabled until you fix them all.\n\n### Reset to defaults\n\nThe **Reset to defaults** button in the toolbar clears every change at once, returning everything to the standard defaults so you can start over cleanly.\n\n### Applying\n\nWhen there are no conflicts, **Apply Custom** opens a preview showing exactly what turns on and what turns off, and asks you to confirm — optionally with a reason for the record. Confirming saves it, and you land back on the Capabilities list. If your choices already match your install, there's nothing to apply and Forge tells you so.\n\n### How it fits\n\nCustom Configuration, the Presets browser, the Discovery wizard, and the Capabilities list all change the same settings. Use Custom when you want full manual control with the safety net of live checking.","sections":[]}"""
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
            ContentJson = """{"body":"## Capability system\n\nDeeper notes for an admin who sets up and maintains which features are turned on, beyond the browse-and-switch basics.\n\n- **History and reasons.** Every change — turning a feature on or off, editing its settings, or applying a preset or discovery result — can carry an optional reason that's saved with it. Each feature has its own history, and there's a full history page for all of them. Every entry records the before and after, who did it (or 'system'), what happened, when, and the reason when one was given. This is your audit trail when someone asks why a feature is the way it is.\n- **Two admins at once.** If someone else changes a feature while you have it open, saving your change is blocked so you don't overwrite them without knowing. Reload the row and try again.\n- **Dependencies and conflicts.** Turning on a feature that's missing something it needs, turning off one that others rely on, or turning on two features that conflict is refused with a clear message. When you turn several on or off at once, it's all-or-nothing — if any one breaks a rule, the whole batch is rejected, so you never end up half-changed.\n- **Accounting conflict.** The one built-in conflict is between built-in accounting and outside accounting — only one can be on. Turn on one while the other is on and it's refused; turn the other off first. Full general-ledger accounting is listed but not available yet; trying to turn it on tells you it's not ready.\n- **Discovery and jumping to the recommendation.** Discovery remembers your place as you answer, so the browser's back button works, and it shows a live leaning recommendation with a confidence level. The final screen lets you jump straight to the recommended preset or pick an alternative; **Skip Discovery** drops you on the Capabilities list to set things up by hand.\n- **Comparing presets.** On the Presets browser, Compare mode lets you pick two to four presets (Custom is left out) and shows them side by side before you commit. Consultant mode shows each feature's short code throughout the list, detail pages, and relationships — handy when a consultant is cross-referencing with you.\n\n## Carrier integrations\n\nDeeper field and setup notes for the shipping and receiving team that runs the Carriers page (Admins, Managers, and Office Managers can reach it once shipping is turned on).\n\n- **Service ID and carrier code.** When you create a carrier, you can set an Integration Service ID (which names the live carrier service to use — ups, fedex, usps, dhl) and its standard carrier code, alongside the name, code, Integration type, Delivery Updates setting, and whether a label scan is required to ship. The service ID field shows only for API-integrated carriers.\n- **Require label scan before shipping.** This is on by default and is enforced on the Shipments page — a package must be scanned before it can be marked shipped for that carrier. It's set per carrier when you create it, so you can opt a courier out where a scan doesn't make sense.\n- **Account number.** The sign-in form takes the API key or client ID, the secret, an optional account number, and whether you're on test or live. Some carriers need the account number for rates and labels; the secret is kept safe and never shown back.\n- **Moving from test to live.** Set things up on test, run the connection test until it passes, then re-save the sign-in details switched to live. Going live is just a re-save of the sign-in details, not a separate step.\n- **Active vs inactive.** Inactive carriers are hidden by default. There's no per-row edit or deactivate button in the table. Whether a carrier is active feeds rate comparison, label printing, and tracking.\n\n## Sales Tax & Currencies\n\nRules, access, and rate-source notes for the office team that maintains the tax and currency admin pages.\n\n- **Sales tax rules and access.** Anyone signed in can view the rates; Admins and Managers can create and edit; only Admins can delete. A rate needs a name (up to 100 characters) and a code (up to 20), the posting account allows up to 100 characters, and the rate must be between 0 and 100 percent. Each code must be unique, so you can't accidentally create two rates with the same identifier.\n- **Currency access.** Both the currencies page and the exchange rates are Admin-only. Each currency carries a code, name, symbol, decimal places (the yen uses 0, most use 2), whether it's the base currency, whether it's active, and its display order. The base currency is the one everything else is reported against.\n- **Converting amounts.** There's a convert tool that takes a from-currency, a to-currency, an amount, and a date. For now the tool returns the amount unchanged — real rate-based conversion arrives when a live rate feed is connected. Stored rates note where they came from — entered by hand, from a provider, or from a bank — and rates you type in are always marked as entered by hand.\n- **Same-currency conversions.** Converting a currency to itself always returns the amount unchanged, so it's always safe to do.\n- **Filtering the history.** You can filter the rate history by the from-currency, to-currency, and a date range, and it shows the newest first. The history keeps every rate: setting a rate for a pair on a date you already have updates that entry, but a new date adds a fresh entry that takes over from the old one, so past rates stay on record.","sections":[]}"""
        });

        Log.Information("Seeded page-specific training modules");
    }
}
