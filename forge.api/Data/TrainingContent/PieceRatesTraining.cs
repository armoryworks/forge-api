using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data.TrainingContent;

public class PieceRatesTraining : TrainingContentBase
{
    public PieceRatesTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override IReadOnlyList<string> Capabilities => ["CAP-HR-PIECE-RATES"];

    public override async Task SeedAsync()
    {
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Piece Rates and the Weekly Wage Check",
            Slug = "piece-rates-overview",
            Summary = "Setting a per-piece rate on a part, logging the pieces a worker finished, and checking each week that piece pay clears the minimum wage",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/piece-rates"]""",
            Tags = """["piece-rates","hr","payroll","minimum-wage"]""",
            ContentJson = """{"body":"## Piece Rates and the Weekly Wage Check\n\nSome shops pay for output rather than hours: so much per bracket welded, per box packed, per casting ground. Forge's **Piece Rates** page (under People) keeps three things on one screen — the rate per part, the pieces each worker logged, and a weekly check that nobody's piece pay fell below the minimum wage for the hours they clocked. An admin turns it on with the **Piece rates + minimum-wage compliance** capability.\n\n### A rate is a timeline\n\nAt the top of the page you pick a part, type the **Rate per piece**, choose an **Effective from** date (it defaults to today) and click **Set rate**. Forge does not overwrite the old rate. It closes the old row the day before and starts a new one, so a rate is really a history: $0.40 through March 31, $0.45 from April 1. Work already logged keeps the rate it was earned under.\n\nThe table below lists every part with a rate, its current rate and the date it started. Expand a row to see the full history. A new rate has to start after the current one began — if you need to correct a mistake, set the right rate from tomorrow rather than trying to rewrite yesterday.\n\n### Logging pieces\n\nThe **Log pieces** row records what a worker finished: pick the **Worker**, the **Part**, the **Date** and the number of **Pieces**, then click **Log pieces**. Forge looks up the rate in force on that date, multiplies, and shows what the entry earned. If the part had no rate on that date the entry is refused — set the rate first.\n\nThe table underneath shows this week's entries with the rate that was applied and the earnings. A wrong entry can be deleted with the trash icon and re-logged; there is no edit.\n\n### The weekly check\n\nPiece pay on its own can drift below the legal minimum in a slow week. The **Weekly minimum-wage check** at the bottom compares, for every worker with piece entries that week, what they earned in pieces against their clocked hours times the minimum wage that applies to them. Pick **any date in the week** and the report covers the seven days starting from it.\n\nEach row shows the worker, the state whose minimum applies, that minimum, hours worked, piece pay, the **Floor** (hours times minimum), the **Make-up owed** if piece pay came in under the floor, and the effective hourly rate. The chip in the heading totals the make-up across everyone: green when it is zero, red when someone is owed.\n\n### Where the hours and the minimum come from\n\nHours come from the worker's ordinary time entries — clock-ins on the phone or kiosk, or manual time. If a piece worker does not clock time, the check has no hours to compare against and shows zero, so make sure piece workers clock in and out like everyone else.\n\nThe minimum wage is picked by location: the worker's work location's state if one is set, otherwise the company's default location, otherwise the federal floor. A state's rate never goes below the federal one. Forge ships with state base rates, but rates change — confirm them with your payroll provider.\n\n### What the check is and is not\n\nThis is a simple weekly average: total piece pay against total hours. That is enough for a plain FLSA check, and it is assistance, not legal advice. Some states, California under AB 1513 in particular, require separate pay for rest breaks and non-productive time and do not allow weekly averaging. If you operate in one of those states, treat the report as a first look and have your payroll provider apply the state rules. The report also assumes piece workers are paid only by the piece; any hourly pay recorded elsewhere is not netted against the floor.\n\n### A good weekly routine\n\n1. Log pieces daily, or have leads do it, while the counts are fresh.\n2. On payroll day, pick a date in the week just finished and read the check.\n3. Any red row is a make-up payment to add to that worker's paycheck. Keep a note of it with the pay run.\n4. If the same worker is red every week, the rate on that part is probably too low for the time it really takes.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Piece Rates Page — Guided Tour",
            Slug = "piece-rates-walkthrough",
            Summary = "A guided tour of the three sections of the Piece Rates page",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/piece-rates"]""",
            Tags = """["piece-rates","walkthrough"]""",
            ContentJson = """{"appRoute":"/piece-rates","startButtonLabel":"Tour Piece Rates","steps":[{"element":"[data-testid='piece-rate-set']","popover":{"title":"Set a Rate","description":"Pick a part, type the rate per piece and an effective date, then click Set rate. The old rate is kept in history; work already logged keeps the rate it was earned under.","side":"bottom"}},{"element":"[data-testid='piece-work-log']","popover":{"title":"Log Pieces","description":"Worker, part, date, pieces. Forge applies the rate in force on that date and shows what the entry earned. This week's entries are listed below.","side":"bottom"}},{"element":".piece-rates__week-head","popover":{"title":"Weekly Minimum-Wage Check","description":"Pick any date in a week. Every worker with piece pay that week is compared against hours times the minimum wage; the chip totals any make-up owed.","side":"top"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Piece Rates Field Reference",
            Slug = "piece-rates-field-reference",
            Summary = "Every field and column on the Piece Rates page, including the weekly minimum-wage check",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/piece-rates"]""",
            Tags = """["piece-rates","reference","fields"]""",
            ContentJson = """{"title":"Piece Rates Field Reference","groups":[{"heading":"Rates","items":[{"label":"Part","value":"The part the rate applies to, picked by part number."},{"label":"Rate per piece","value":"Dollars paid per finished piece. Up to four decimals are shown."},{"label":"Effective from","value":"First day the new rate applies. Defaults to today. Must be after the current rate's start date."},{"label":"Set rate","value":"Closes the current rate the day before and starts the new one. History is never edited."},{"label":"Current rate / Since","value":"The rate in force now and the date it started. Expand the row for the full timeline."}]},{"heading":"Log pieces","items":[{"label":"Worker","value":"The person who did the work."},{"label":"Part","value":"What they made."},{"label":"Date","value":"The work date. The rate in force on this date is applied, not today's."},{"label":"Pieces","value":"How many they finished. Fractions are allowed."},{"label":"Rate / Earned","value":"The rate snapshot applied to the entry and pieces times rate. Fixed once logged; delete and re-log to change."},{"label":"Delete entry","value":"Trash icon on the row. Removes the entry from earnings and from the weekly check."}]},{"heading":"Weekly minimum-wage check","items":[{"label":"Any date in the week","value":"The report covers seven days starting on the date you pick."},{"label":"State","value":"Whose minimum applies: the worker's work location, else the company default location, else US (federal)."},{"label":"Min wage","value":"The minimum hourly rate in force for that state at the end of the week, never below the federal floor."},{"label":"Hours","value":"The worker's time entries for the week, in hours."},{"label":"Piece pay","value":"Total earnings from piece entries in the week."},{"label":"Floor","value":"Hours times Min wage — the least the worker must be paid for the week."},{"label":"Make-up owed","value":"Floor minus Piece pay when Piece pay is lower; otherwise zero. Red when owed, green when clear."},{"label":"Effective /hr","value":"Piece pay divided by hours. Zero when no hours were clocked."},{"label":"Make-up owed (chip)","value":"Total make-up across all workers for the week."}]}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Piece Rates — Knowledge Check",
            Slug = "piece-rates-quiz",
            Summary = "Five questions on rate timelines, logging pieces and the weekly minimum-wage check",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/piece-rates"]""",
            Tags = """["piece-rates","quiz"]""",
            ContentJson = """{"passingScore":80,"questionsPerQuiz":5,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"pr1","text":"You raise a part's rate from $0.40 to $0.45 effective April 1. What happens to pieces logged on March 28?","options":[{"id":"a","text":"They are recalculated at $0.45"},{"id":"b","text":"They keep the $0.40 they were earned under","isCorrect":true},{"id":"c","text":"They are deleted and must be re-logged"},{"id":"d","text":"They are averaged to $0.425"}],"explanation":"A rate is a timeline. Setting a new rate closes the old row the day before; entries already logged keep their rate snapshot."},{"id":"pr2","text":"Log pieces refuses an entry with the message that no rate is in force. What is wrong?","options":[{"id":"a","text":"The worker is not clocked in"},{"id":"b","text":"The part had no piece rate on the work date","isCorrect":true},{"id":"c","text":"The quantity is a fraction"},{"id":"d","text":"The week has already been checked"}],"explanation":"Forge applies the rate in force on the work date. If the part's first rate starts after that date, there is nothing to apply — set a rate first."},{"id":"pr3","text":"A worker earned $280 in pieces this week and clocked 40 hours where the minimum wage is $7.25. What does the check show?","options":[{"id":"a","text":"Make-up owed of $10","isCorrect":true},{"id":"b","text":"Make-up owed of $0"},{"id":"c","text":"An effective rate of $7.25"},{"id":"d","text":"Nothing — the worker is not listed"}],"explanation":"Floor = 40 × $7.25 = $290. Piece pay of $280 is $10 short, so $10 of make-up is owed and the row shows red."},{"id":"pr4","text":"A piece worker shows zero hours and zero make-up every week. What is the most likely cause?","options":[{"id":"a","text":"Their rate is too high"},{"id":"b","text":"They are not clocking time, so the check has no hours to compare against","isCorrect":true},{"id":"c","text":"Their state has no minimum wage"},{"id":"d","text":"Piece workers are exempt from the check"}],"explanation":"Hours come from ordinary time entries. No clocked time means no floor, which hides a shortfall instead of catching it."},{"id":"pr5","text":"Which minimum wage does the check use for a worker?","options":[{"id":"a","text":"Always the federal rate"},{"id":"b","text":"The worker's work-location state, else the company default location, else federal — never below federal","isCorrect":true},{"id":"c","text":"Whatever was typed on the Log pieces row"},{"id":"d","text":"The highest state rate in the country"}],"explanation":"Location decides the state; the state rate is floored at the federal minimum. Rates ship with Forge but should be confirmed with payroll."}]}"""
        });
    }
}
