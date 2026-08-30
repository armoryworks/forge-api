using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data.TrainingContent;

public class QualityAdvancedTraining : TrainingContentBase
{
    public QualityAdvancedTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override IReadOnlyList<string> Capabilities =>
    [
        "CAP-EXT-WATCHTOWER",
    ];

    public override async Task SeedAsync()
    {
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Regulatory Watchtower",
            Slug = "quality-adv-watchtower-overview",
            Summary = "How Watchtower watches outside regulators for you, what a proposal is, and how applying one puts a deadline on the compliance calendar",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/watchtower/proposals","/watchtower/sources","/compliance"]""",
            Tags = """["quality","compliance","watchtower","regulatory"]""",
            ContentJson = """{"body":"## Regulatory Watchtower\n\nRules change. OSHA updates a standard, the EPA opens a comment period, the FDA or ATF publishes a new requirement, and unless someone on your team is reading the Federal Register every week, the first you hear of it is an auditor asking why you are not already doing it. Watchtower is the part of Forge that does that reading for you.\n\nIt watches a list of outside regulatory sources, and when one of them publishes something that looks like it applies to your shop, it writes a **proposal** and drops it into an inbox. Nothing happens to your compliance calendar until a person reads the proposal and decides. Watchtower proposes; you confirm.\n\n### Turning it on\n\nWatchtower is off until an admin enables it, and it stays off on purpose for shops that run without an internet connection, because it has to reach out to the regulators' websites to work. Once it is on, **Watchtower** appears in the navigation under Quality, alongside **Compliance**. Admins and managers can open it.\n\n### The two tabs\n\n**Proposals** is the inbox. Each card is one thing Watchtower found: which source it came from, a title, a short summary if the source gave one, the date it was picked up, and a **View source** link to the original notice so you can read it in full. Cards are color-coded by status: **Pending** means nobody has looked at it yet, **Applied** means someone accepted it, **Dismissed** means someone decided it does not apply. The status filter at the top starts on Pending so you only see what needs a decision; switch it to All when you want the history.\n\n**Sources** is the watch list: every regulator feed Forge is monitoring, the agency that issues it, the area it covers, whether it is active, and when it was last checked. Some sources carry an industry tag, which means they only make sense for certain kinds of shops. This tab is read-only; it is there so you can see what is and is not being watched.\n\n### Deciding on a proposal\n\nOpen the Proposals tab, read the card, and follow the source link if you need the details. Then pick one of two buttons.\n\n**Dismiss** marks the proposal as not applicable. Forge asks you to confirm, records who dismissed it and when, and the card moves out of the Pending view. Nothing is added to the calendar. Use this for notices that concern a different industry, a state you do not operate in, or a rule you already meet.\n\n**Apply** marks it as something your shop needs to act on. A small dialog opens with two optional fields: a **due date** and a **calendar event type**. Fill in both and Forge creates a deadline on the compliance calendar for that date, filed under the event type you picked, so it shows up next to your other recurring compliance obligations and gets the same reminders. Leave them blank and the proposal is simply recorded as applied with no calendar entry, which is fine when you want to acknowledge a change without scheduling anything yet.\n\nEither way, the reviewer's name and the time of the decision are kept with the proposal. That record is what an auditor wants to see: not just that you found out about a change, but who evaluated it and what they decided.\n\n### Poll now\n\nForge checks the sources on its own schedule in the background. If you have just enabled Watchtower, added a source, or heard about a notice and want to see whether it has been picked up, click **Poll now** in the toolbar. Forge checks every active source right away and tells you how many new proposals it found. Polling takes a moment; the button spins while it runs.\n\n### Good habits\n\n1. Check the Proposals tab once a week. The badge on the tab shows how many are still pending.\n2. Always open the source link before applying. The proposal is a summary, not the rule.\n3. When you apply something with a real deadline, set the due date and an event type so it lands on the compliance calendar instead of living only in Watchtower.\n4. Dismiss confidently. A dismissed proposal is still on record and can be found again by switching the filter to All.\n5. Glance at the Sources tab now and then to make sure the feeds that matter to your shop are active and were polled recently.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Watchtower — Guided Tour",
            Slug = "quality-adv-watchtower-walkthrough",
            Summary = "A guided tour of the Watchtower page: the proposal inbox, the status filter, Poll now and the Sources tab",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/watchtower/proposals"]""",
            Tags = """["quality","compliance","watchtower","walkthrough"]""",
            ContentJson = """{"appRoute":"/watchtower/proposals","startButtonLabel":"Tour Watchtower","steps":[{"element":".page-layout__title","popover":{"title":"Regulatory Watchtower","description":"Forge watches outside regulators for you and turns anything that looks relevant into a proposal. Nothing reaches your compliance calendar until a person reviews it here.","side":"bottom"}},{"element":".tab-bar","popover":{"title":"Two Tabs","description":"Proposals is the inbox of things to decide on; the badge counts how many are still pending. Sources is the watch list of regulator feeds being monitored.","side":"bottom"}},{"element":".wt-filter","popover":{"title":"Status Filter","description":"Starts on Pending so you only see what needs a decision. Switch to Applied, Dismissed or All to review past decisions.","side":"bottom"}},{"element":".tab-panel","popover":{"title":"Proposal Cards","description":"Each card shows the source, a title, a summary, the pickup date and a View source link to the original notice. Pending cards have Dismiss and Apply buttons; Apply lets you set a due date and event type to create a compliance-calendar deadline.","side":"top"}},{"element":".page-layout__header .action-btn","popover":{"title":"Poll Now","description":"Forge checks the sources on a schedule, but you can check every active source right away. The button spins while it runs and reports how many new proposals it found.","side":"left"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Watchtower Field Reference",
            Slug = "quality-adv-watchtower-field-reference",
            Summary = "Every field on the Proposals and Sources tabs, the status filter and the Apply Proposal dialog",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/watchtower/proposals","/watchtower/sources"]""",
            Tags = """["quality","compliance","watchtower","reference","fields"]""",
            ContentJson = """{"title":"Watchtower Field Reference","groups":[{"heading":"Toolbar","items":[{"label":"Poll now","value":"Checks every active source immediately instead of waiting for the scheduled check. Reports how many new proposals were found. Disabled while a poll is running."}]},{"heading":"Proposals tab","items":[{"label":"Status filter","value":"Pending (default), Applied, Dismissed or All. Only Pending proposals have decision buttons."},{"label":"Pending badge","value":"The count on the Proposals tab of proposals nobody has decided on yet."},{"label":"Source","value":"The regulator feed the proposal came from, shown as a chip on the card."},{"label":"Title / details","value":"The notice title and, when the source supplies one, a short summary. Read the original before deciding."},{"label":"Status chip","value":"Pending, Applied or Dismissed, color-coded on the right of the card."},{"label":"Date","value":"When Watchtower picked the notice up, not when the regulator published it."},{"label":"View source","value":"Opens the regulator's original notice in a new tab."},{"label":"Dismiss","value":"Marks the proposal as not applicable after a confirmation. Records who dismissed it. Adds nothing to the calendar."},{"label":"Apply","value":"Marks the proposal as applicable and opens the Apply Proposal dialog. Records who applied it."}]},{"heading":"Apply Proposal dialog","items":[{"label":"Due date","value":"Optional. The date the shop must have acted by. Together with an event type it creates a compliance-calendar deadline."},{"label":"Calendar event type","value":"Optional. Which kind of compliance event the deadline is filed under, so it gets that type's reminders and shows with similar obligations."},{"label":"Apply","value":"Confirms the decision. With both fields filled, a deadline is created on the compliance calendar; with either blank, the proposal is recorded as applied with no calendar entry."}]},{"heading":"Sources tab","items":[{"label":"Name","value":"The feed being watched, for example a Federal Register or agency rulemaking feed."},{"label":"Active / Inactive","value":"Whether the feed is currently polled. Inactive feeds produce no proposals."},{"label":"Issuing body","value":"The agency behind the feed: OSHA, EPA, ATF, FDA and so on."},{"label":"Domain","value":"The regulatory area the feed covers, such as workplace safety or environmental."},{"label":"Feed type","value":"The technical format of the feed. Informational only."},{"label":"Industry tag","value":"Shown when a source only applies to certain kinds of shops."},{"label":"URL","value":"The address Forge polls. Opens in a new tab."},{"label":"Last polled","value":"When Forge last checked the source, or Never if it has not been checked since it was added."}]}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Watchtower — Knowledge Check",
            Slug = "quality-adv-watchtower-quiz",
            Summary = "Five questions on proposals, applying and dismissing, the compliance calendar link and polling",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/watchtower/proposals"]""",
            Tags = """["quality","compliance","watchtower","quiz"]""",
            ContentJson = """{"passingScore":80,"shuffleQuestions":false,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"q1","text":"Watchtower has picked up a new OSHA notice. What has happened to your compliance calendar?","options":[{"id":"q1a","text":"Nothing yet — the notice is a pending proposal until a person applies it","isCorrect":true},{"id":"q1b","text":"A deadline was added automatically with a 30-day due date"},{"id":"q1c","text":"All matching compliance events were updated"},{"id":"q1d","text":"The notice was emailed to every manager"}],"explanation":"Watchtower proposes and a person confirms. Nothing changes on the calendar until someone reviews the proposal and applies it."},{"id":"q2","text":"You apply a proposal, fill in a due date and pick a calendar event type. What does Forge create?","options":[{"id":"q2a","text":"A new monitored source"},{"id":"q2b","text":"A deadline on the compliance calendar for that date, filed under that event type","isCorrect":true},{"id":"q2c","text":"A nonconformance report"},{"id":"q2d","text":"Nothing — the fields are only notes"}],"explanation":"With both fields filled, applying a proposal creates a compliance-calendar deadline so it gets the same reminders as your other obligations."},{"id":"q3","text":"A proposal concerns a food-safety rule and your shop machines metal parts. What is the right action?","options":[{"id":"q3a","text":"Apply it with no due date, just in case"},{"id":"q3b","text":"Leave it pending forever"},{"id":"q3c","text":"Dismiss it — the decision and who made it are still recorded","isCorrect":true},{"id":"q3d","text":"Delete the source so it never appears again"}],"explanation":"Dismiss is for proposals that do not apply. The dismissal is kept on record with the reviewer's name, and the Sources tab is read-only."},{"id":"q4","text":"Why does the Proposals tab open with the status filter set to Pending?","options":[{"id":"q4a","text":"Because applied and dismissed proposals are deleted"},{"id":"q4b","text":"So you see only the proposals that still need a decision","isCorrect":true},{"id":"q4c","text":"Because only pending proposals have a source link"},{"id":"q4d","text":"It is a display bug"}],"explanation":"Pending is the work queue. Switch the filter to Applied, Dismissed or All to review the history."},{"id":"q5","text":"What does the Poll now button do?","options":[{"id":"q5a","text":"Sends a survey to your employees"},{"id":"q5b","text":"Marks every pending proposal as applied"},{"id":"q5c","text":"Checks every active source right away and reports how many new proposals it found","isCorrect":true},{"id":"q5d","text":"Adds a new regulator to the watch list"}],"explanation":"Forge polls on a schedule, but Poll now checks all active sources immediately — useful right after enabling Watchtower or when you have heard about a notice."}]}"""
        });
    }
}
