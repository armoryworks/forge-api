using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data.TrainingContent;

/// <summary>
/// Per-integration training for the external-service capabilities that have a
/// user-facing surface: communication tracking (email + voice) under
/// Account > Communications, and the phone web app at /m. Chat-platform push
/// and cloud-storage folder links are intentionally not taught here — neither
/// has a page a user can operate yet.
/// </summary>
public class IntegrationsTraining : TrainingContentBase
{
    public IntegrationsTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override IReadOnlyList<string> Capabilities =>
    [
        "CAP-EXT-EMAIL-SYNC", "CAP-EXT-MOBILE",
    ];

    public override async Task SeedAsync()
    {
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Communication Tracking: Email and Phone",
            Slug = "integrations-communication-tracking",
            Summary = "Connect your work mailbox or phone system so messages and calls with leads and contacts log themselves",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/account/communications"]""",
            Tags = """["integrations","email","voip","communications","leads"]""",
            ContentJson = """{"body":"## Communication Tracking: Email and Phone\n\nEvery salesperson already emails and calls leads. Communication tracking makes that traffic show up in Forge without anyone retyping it. Once you connect a mailbox, any message to or from an address that belongs to an active lead or customer contact is logged on that record as a communication. Connect a phone system and calls do the same.\n\nThis is a **personal** connection: each user links their own mailbox from **Account > Communications**. An admin does not connect one mailbox for the whole company. That keeps the privacy line clear: Forge only reads mail in the mailbox you chose to connect, and only keeps messages that match a lead or contact.\n\n### Two switches an admin controls\n\nBoth kinds of tracking are off on a new install. An admin turns them on under **Admin > Capabilities**:\n\n- **Email sync** enables the Email group on the Communications page.\n- **VoIP sync** enables the Voice & Phone group.\n\nUntil a switch is on, the matching group shows a lock banner that says the capability is not enabled. You can still see anything you connected earlier, but you cannot add new connections.\n\n### Connecting a mailbox\n\nOpen **Account > Communications**. The Email group lists the providers you can connect:\n\n- **Gmail** and **Outlook / Microsoft 365** send you to Google's or Microsoft's own sign-in page. Approve the request and you are returned to Forge with the mailbox connected. No password is typed into Forge.\n- **IMAP Mailbox** works with almost any email service. Pick your provider from the preset list so the server address and port fill in for you, then enter your email address and an **app password** or access token. Most providers no longer accept your normal login password over IMAP; create an app-specific password in your email account's security settings and paste it here. Forge tests the connection before saving, so a wrong host or password fails right away instead of silently later.\n\nEvery connection can carry a **Display Label** such as *Sales mailbox* so you can tell two connections apart.\n\n### What happens after you connect\n\nThe first sync runs within about fifteen minutes and then repeats on its own. Each connection card shows a status: **Connected**, **Pending handshake** while the first exchange is still in progress, or **Sync failed** with the error text underneath. The health strip at the top of the page adds them up: total, healthy, errored and pending.\n\nUse the **Sync now** button on a card when you do not want to wait for the next cycle. **Disconnect** removes the connection; communications already logged stay on their leads and contacts.\n\nMatched messages appear on the lead or contact under their communications. When a customer's email reads like an order or an approval, Forge can also flag it for proof-of-intent review; that workflow has its own training.\n\n### Phone and voice\n\nThe Voice & Phone group works the same way in principle: a connected phone system reports calls to Forge, and calls to or from a lead's or contact's number log as communications. Today the phone providers listed there are marked **Coming soon** and cannot be connected from this page yet. A **Mock Voice Provider** tagged *Mock* is available for trying the flow; it never places or receives real calls. Because recording calls carries consent rules that differ by state, expect your admin to enable voice tracking deliberately rather than by default.\n\n### Good habits\n\n1. Connect the mailbox you actually use for customers, not a shared inbox you rarely read.\n2. Use an app password for IMAP, never your main login password.\n3. Glance at the health strip after the first sync; fix a **Sync failed** card the same day, since nothing is logged while it is broken.\n4. If you change your email password, reconnect; the old connection will start failing.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Communications Page: Guided Tour",
            Slug = "integrations-communications-tour",
            Summary = "A guided tour of Account > Communications: the health strip, the Email and Voice groups, provider tiles and connection cards",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/account/communications"]""",
            Tags = """["integrations","communications","walkthrough"]""",
            ContentJson = """{"appRoute":"/account/communications","startButtonLabel":"Tour Communications","steps":[{"element":".comms__header","popover":{"title":"Communication Tracking","description":"This page is personal to you. Connect your own mailbox or phone system here and Forge logs messages and calls with leads and contacts automatically.","side":"bottom"}},{"element":".kind-group","popover":{"title":"Email and Voice Groups","description":"Connections are grouped by kind. Each group shows what you already connected on top and the providers you can still add underneath. A lock banner here means your admin has not enabled that kind yet.","side":"bottom"}},{"element":".kind-group__count","popover":{"title":"Connected Count","description":"How many connections of this kind you have. Most people need exactly one mailbox.","side":"left"}},{"element":".connection-card","popover":{"title":"Provider Tile or Connection Card","description":"Before you connect, a tile shows the provider name and a short description; tap it to start. Once connected, the card shows status, the account it belongs to and the last sync time, with Sync now and Disconnect buttons on the right.","side":"top"}},{"element":".connection-card__connect","popover":{"title":"Connect","description":"Gmail and Microsoft open the provider's own sign-in page. IMAP opens a small form where you pick a preset and paste an app password. A Mock provider is only for testing and never touches a real account.","side":"left"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Communication Tracking Reference",
            Slug = "integrations-communication-tracking-reference",
            Summary = "Providers, statuses, buttons and IMAP form fields on the Communications page",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/account/communications"]""",
            Tags = """["integrations","communications","reference"]""",
            ContentJson = """{"title":"Communication Tracking Reference","groups":[{"heading":"Where and who","items":[{"label":"Page","value":"Account > Communications. Each user connects their own mailbox or phone; there is no company-wide connection."},{"label":"Admin switches","value":"Email sync and VoIP sync capabilities under Admin > Capabilities. Off by default; a locked group means the switch is off."},{"label":"What gets logged","value":"Only messages and calls whose other party matches an active lead's or contact's email address or phone number."}]},{"heading":"Email providers","items":[{"label":"Gmail","value":"Google Workspace or Gmail. Sign in on Google's page and approve; no password entered in Forge."},{"label":"Outlook / Microsoft 365","value":"Sign in on Microsoft's page; works with multi-factor and work or school accounts."},{"label":"IMAP Mailbox","value":"Any provider that supports IMAP. Uses a preset plus an app password or access token."},{"label":"Mock Email Provider","value":"Generates pretend messages for testing. Never connects to a real mailbox."}]},{"heading":"Voice providers","items":[{"label":"Twilio, RingCentral","value":"Listed as Coming soon; cannot be connected from this page yet."},{"label":"Mock Voice Provider","value":"Pretend calls for testing the flow. Never places or receives a real call."}]},{"heading":"IMAP form","items":[{"label":"Preset","value":"Pick your email provider to fill in host and port. Choose a custom preset for an in-house server."},{"label":"IMAP Host / Port","value":"Your provider's incoming mail server and port. Filled by the preset."},{"label":"Use SSL/TLS","value":"Leave on unless your provider specifically says otherwise."},{"label":"Email Address","value":"The mailbox login, usually your full email address."},{"label":"Personal Access Token","value":"An app password or access token from your provider's security settings, not your everyday login password."},{"label":"Mailbox Folder","value":"Which folder to read, normally INBOX."},{"label":"Display Label","value":"Optional friendly name shown on the card, for example Sales mailbox."}]},{"heading":"Connection card","items":[{"label":"Connected","value":"Healthy; syncs run on their own."},{"label":"Pending handshake","value":"Created but the first exchange with the provider has not completed."},{"label":"Sync failed","value":"The last sync hit an error. The message shows under the card; fix it or reconnect."},{"label":"Last sync","value":"When Forge last read this connection. Never until the first cycle runs."},{"label":"Sync now","value":"Runs a sync immediately instead of waiting for the next cycle."},{"label":"Disconnect","value":"Removes the connection. Communications already logged are kept."}]},{"heading":"Health strip","items":[{"label":"Total / Healthy / Errored / Pending","value":"Counts across all your connections. Shown only once you have at least one connection."}]}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Communication Tracking: Knowledge Check",
            Slug = "integrations-communication-tracking-quiz",
            Summary = "Five questions on connecting mailboxes, app passwords, statuses and what gets logged",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/account/communications"]""",
            Tags = """["integrations","communications","quiz"]""",
            ContentJson = """{"passingScore":80,"shuffleQuestions":false,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"q1","text":"Who connects a mailbox for communication tracking?","options":[{"id":"q1a","text":"Each user connects their own under Account > Communications","isCorrect":true},{"id":"q1b","text":"An admin connects one company mailbox under Admin > Integrations","isCorrect":false},{"id":"q1c","text":"Forge connects automatically when a lead is created","isCorrect":false},{"id":"q1d","text":"The customer connects it from the portal","isCorrect":false}],"explanation":"Connections are personal. Every salesperson links their own mailbox; admins only turn the capability on."},{"id":"q2","text":"What do you enter in the Personal Access Token field of the IMAP form?","options":[{"id":"q2a","text":"Your everyday email login password","isCorrect":false},{"id":"q2b","text":"An app password or access token created in your email account's security settings","isCorrect":true},{"id":"q2c","text":"Your Forge password","isCorrect":false},{"id":"q2d","text":"The lead's email address","isCorrect":false}],"explanation":"Most providers reject a normal password over IMAP. Create an app-specific password and paste that."},{"id":"q3","text":"Which messages does Forge keep from a connected mailbox?","options":[{"id":"q3a","text":"Every message in the mailbox","isCorrect":false},{"id":"q3b","text":"Only messages you star","isCorrect":false},{"id":"q3c","text":"Only messages to or from an address that matches an active lead or customer contact","isCorrect":true},{"id":"q3d","text":"Only messages with attachments","isCorrect":false}],"explanation":"The matcher looks for a lead's or contact's address. Unmatched mail is not logged."},{"id":"q4","text":"A connection card shows Sync failed. What is true?","options":[{"id":"q4a","text":"Nothing new is logged from that mailbox until it is fixed or reconnected","isCorrect":true},{"id":"q4b","text":"Forge deletes the communications it already logged","isCorrect":false},{"id":"q4c","text":"The admin must turn the capability off and on again","isCorrect":false},{"id":"q4d","text":"It fixes itself after fifteen minutes","isCorrect":false}],"explanation":"A failed sync keeps failing until the cause, often a changed password, is fixed. Logged history is not removed."},{"id":"q5","text":"The Email group shows a lock banner and no provider tiles. Why?","options":[{"id":"q5a","text":"You already connected the maximum number of mailboxes","isCorrect":false},{"id":"q5b","text":"Your admin has not enabled the email sync capability on this install","isCorrect":true},{"id":"q5c","text":"Your browser blocked the camera","isCorrect":false},{"id":"q5d","text":"The mailbox is in Pending handshake","isCorrect":false}],"explanation":"Each group is gated by its own capability. The lock banner names the capability an admin needs to enable."}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Forge on Your Phone: The Web App",
            Slug = "integrations-mobile-web-overview",
            Summary = "The phone-sized web version of Forge at /m: clock in, see your jobs, run a timer, scan a barcode, chat and check notifications",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 5,
            AppRoutes = """["/m/clock","/m/jobs","/m/scan","/m/chat","/m/account"]""",
            Tags = """["mobile","phone","clock","scan","jobs"]""",
            ContentJson = """{"body":"## Forge on Your Phone: The Web App\n\nOpen Forge in your phone's browser and you land on a phone-sized version of the app instead of the full desktop layout. Nothing to install: it is the same sign-in and the same data, trimmed to what a person on the floor needs in one hand. (Forge also has a separate installable app from the app stores; that one is covered in its own training.)\n\nIf you would rather have the full desktop layout on your phone, tap **Account > Open Desktop View**. Forge remembers that choice until you close the browser.\n\n### Clock first\n\nThe app opens on **Clock**. The big status card says whether you are In, Out or on a break and since when, and the buttons below it are the actions you can take right now. Tap one and Forge records it immediately.\n\nClocking in matters here: **My Jobs** and **Scan** are locked until you are clocked in. A banner across the top reminds you when they are, and those two tabs stay grayed out until you clock in.\n\n### The tabs\n\nThe bar along the bottom has five tabs.\n\n- **Chat** is the same messaging as the desktop: direct messages, channels, threads and file attachments, with a search box for finding a conversation.\n- **My Jobs** lists the jobs assigned to you, each with its job number, title, stage and priority. Overdue jobs are flagged. Tap one to open it.\n- **Scan** (the large center button) opens your camera to read a barcode or QR code on a traveler, part label or asset tag. Forge tells you what it found and gives you an **Open** button to go straight to it. If the camera is unavailable, tap **Manual Entry** and type the value instead.\n- **Clock** is the status card described above.\n- **Account** shows your profile, a dark or light mode switch, links to your full account pages, the desktop view button and **Log Out**.\n\nThe bell in the top-right corner opens **Notifications**, where you can read, dismiss or mark everything read.\n\n### Working a job\n\nOpen a job from My Jobs. The top card shows stage, priority, customer, part and due date. Below it is one big button: **Start Timer** begins logging your time against this job, and it turns into **Stop Timer** while running, showing when you started. Further down you can read the job description and **Add Note** to leave an update that the office sees on the job.\n\n### Checking your hours\n\n**My Hours** shows one week at a time with a total and a row per day; tap a day to see its entries with times and job numbers. Use the arrows to look at earlier weeks. Hours are read-only on the phone; corrections happen on the desktop app.\n\n### Tips\n\n1. Allow camera access the first time Scan asks; otherwise you will be typing codes by hand.\n2. Scanning only works over a secure (https) address.\n3. If a tab looks gray, you are probably clocked out.\n4. Notes you add on the phone are ordinary job notes; the office sees them right away.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Phone Web App: Guided Tour",
            Slug = "integrations-mobile-web-tour",
            Summary = "A guided tour of the phone web app's Clock screen and bottom tab bar",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 6,
            AppRoutes = """["/m/clock"]""",
            Tags = """["mobile","walkthrough"]""",
            ContentJson = """{"appRoute":"/m/clock","startButtonLabel":"Tour the Phone App","steps":[{"element":".mobile-header","popover":{"title":"Header","description":"The Forge name on the left and the notifications bell on the right. The bell opens the same notifications you get on the desktop.","side":"bottom"}},{"element":".clock-status","popover":{"title":"Clock Status","description":"Your current state, In, Out or on a break, and the time it started.","side":"bottom"}},{"element":".clock-actions","popover":{"title":"Clock Actions","description":"Only the actions that make sense right now are shown. Tap one and it is recorded immediately.","side":"top"}},{"element":".mobile-nav","popover":{"title":"Tab Bar","description":"Chat, My Jobs, Scan, Clock and Account. My Jobs and Scan are grayed out until you clock in.","side":"top"}},{"element":".mobile-nav__tab--scan","popover":{"title":"Scan","description":"The center button opens the camera to read a job, part or asset code and jump straight to it. Manual Entry is there when the camera is not.","side":"top"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Phone Web App Reference",
            Slug = "integrations-mobile-web-reference",
            Summary = "Every screen of the phone web app and what each button does",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 7,
            AppRoutes = """["/m/clock","/m/jobs","/m/scan","/m/time","/m/chat","/m/notifications","/m/account"]""",
            Tags = """["mobile","reference"]""",
            ContentJson = """{"title":"Phone Web App Reference","groups":[{"heading":"Getting there","items":[{"label":"Address","value":"Sign in from a phone browser and Forge switches to the phone layout on its own. Account and setup pages stay in the desktop layout."},{"label":"Open Desktop View","value":"On the Account screen. Shows the full desktop layout on the phone until you close the browser."},{"label":"Clock gate","value":"My Jobs and Scan are locked while you are clocked out. A banner at the top says so."}]},{"heading":"Clock","items":[{"label":"Status card","value":"In, Out or a break type, plus the time it began."},{"label":"Action buttons","value":"The clock events available from your current state. Tap to record; a confirmation appears at the bottom."}]},{"heading":"My Jobs","items":[{"label":"Job row","value":"Job number, title, stage color, stage name and priority. Overdue jobs carry an Overdue flag."},{"label":"Tap a job","value":"Opens the job detail screen."}]},{"heading":"Job detail","items":[{"label":"Back arrow","value":"Returns to My Jobs."},{"label":"Status card","value":"Stage, priority, customer, part and due date; overdue dates are marked."},{"label":"Start Timer / Stop Timer","value":"Starts or stops time logging against this job. Shows the start time while running."},{"label":"Add Note","value":"Type an update and tap Add Note. It becomes a note on the job for everyone."}]},{"heading":"Scan","items":[{"label":"Camera view","value":"Point at a barcode or QR code. Needs camera permission and a secure (https) address."},{"label":"Result card","value":"What the code matched, job, part or asset, with Open and Scan Again. No matching entity means the value is not in Forge."},{"label":"Manual Entry","value":"Type or paste the code value when the camera is unavailable."},{"label":"Try Again","value":"Retries the camera after you grant permission or close another app that was using it."}]},{"heading":"My Hours","items":[{"label":"Week arrows","value":"Move to earlier weeks; you cannot move past the current week."},{"label":"Week Total","value":"Hours for the displayed week."},{"label":"Day row","value":"Tap to expand the day's entries: start and end time, job number, description, hours."},{"label":"Read-only","value":"Corrections are made in the desktop app."}]},{"heading":"Chat","items":[{"label":"Search","value":"Filters your conversations and channels."},{"label":"Conversation view","value":"Messages, attachments and threads; the back arrow returns to the list."}]},{"heading":"Notifications","items":[{"label":"Bell","value":"Top-right corner of every screen."},{"label":"Mark All Read","value":"Shown when you have unread items."},{"label":"Tap / X","value":"Tap an item to mark it read; the X dismisses it."}]},{"heading":"Account","items":[{"label":"Dark Mode / Light Mode","value":"Switches the color theme."},{"label":"Profile, Integrations, Security, Customization","value":"Open your full account pages."},{"label":"Setup Wizard","value":"Reopens onboarding."},{"label":"Log Out","value":"Signs you out on this phone."}]}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Phone Web App: Knowledge Check",
            Slug = "integrations-mobile-web-quiz",
            Summary = "Five questions on the clock gate, timers, scanning and hours on the phone web app",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 8,
            AppRoutes = """["/m/clock"]""",
            Tags = """["mobile","quiz"]""",
            ContentJson = """{"passingScore":80,"shuffleQuestions":false,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"q1","text":"The My Jobs and Scan tabs are grayed out. What is the most likely reason?","options":[{"id":"q1a","text":"You are clocked out","isCorrect":true},{"id":"q1b","text":"Your phone has no camera","isCorrect":false},{"id":"q1c","text":"No jobs are assigned to you","isCorrect":false},{"id":"q1d","text":"Dark mode is on","isCorrect":false}],"explanation":"Both tabs are locked until you clock in. The banner at the top of the screen says the same."},{"id":"q2","text":"How do you log time against a specific job from the phone?","options":[{"id":"q2a","text":"Type the hours into My Hours","isCorrect":false},{"id":"q2b","text":"Open the job from My Jobs and tap Start Timer","isCorrect":true},{"id":"q2c","text":"Send the job number in Chat","isCorrect":false},{"id":"q2d","text":"Tap the bell icon","isCorrect":false}],"explanation":"The timer button on the job detail screen starts and stops time on that job. My Hours is read-only."},{"id":"q3","text":"The camera will not start on the Scan screen. What can you do right now?","options":[{"id":"q3a","text":"Nothing; scanning requires the camera","isCorrect":false},{"id":"q3b","text":"Tap Manual Entry and type the code value","isCorrect":true},{"id":"q3c","text":"Clock out and back in","isCorrect":false},{"id":"q3d","text":"Log out","isCorrect":false}],"explanation":"Manual Entry accepts a typed or pasted code and looks it up the same way a scan would."},{"id":"q4","text":"Where do notes added from the phone's job screen go?","options":[{"id":"q4a","text":"Only to your own phone","isCorrect":false},{"id":"q4b","text":"To a chat channel","isCorrect":false},{"id":"q4c","text":"Onto the job as a regular note everyone can see","isCorrect":true},{"id":"q4d","text":"To your time entries","isCorrect":false}],"explanation":"Add Note writes an ordinary job note; the office sees it on the job immediately."},{"id":"q5","text":"You want the full desktop layout on your phone. What do you do?","options":[{"id":"q5a","text":"Install the app from the app store","isCorrect":false},{"id":"q5b","text":"Ask an admin to change your role","isCorrect":false},{"id":"q5c","text":"Tap Open Desktop View on the Account screen","isCorrect":true},{"id":"q5d","text":"Rotate the phone sideways","isCorrect":false}],"explanation":"Open Desktop View switches to the desktop layout for the rest of the browser session."}]}"""
        });
    }
}
