using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

using Serilog;

namespace Forge.Api.Data.TrainingContent;

public class NavigationTraining : TrainingContentBase
{
    public NavigationTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override async Task SeedAsync()
    {
        // ── Overview (Article) ───────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Navigating the App",
            Slug = "navigating-the-app-overview",
            Summary = "A comprehensive overview of the sidebar, header, notifications, search, theme switching, and navigation structure.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            IsOnboardingRequired = true,
            SortOrder = 2,
            AppRoutes = """["/dashboard"]""",
            Tags = """["onboarding","navigation"]""",
            ContentJson = """{"body":"## Navigating the App\n\nQB Engineer has a consistent layout across every page: a collapsible sidebar on the left, a fixed header bar at the top, and a scrollable content area in the center.\n\n### Sidebar Navigation\n\nThe sidebar is your primary navigation tool. In its collapsed state, it shows only icons (52px wide). Hover or click the expand toggle to see full labels (200px wide). Your sidebar state is saved to your preferences — it persists across sessions and devices.\n\n**Sidebar sections:**\n- **Dashboard** — your daily home page with widgets\n- **Board** — the kanban board for active jobs\n- **Backlog** — all jobs not on the board\n- **Planning** — planning cycle management\n- **Parts** — parts catalog with BOMs and process steps\n- **Inventory** — stock levels, bins, movements\n- **Customers** — CRM with contacts and addresses\n- **Vendors** — vendor management and PO history\n- **Purchase Orders** — PO creation and receiving\n- **Sales Orders** — order management\n- **Quotes** — price quoting\n- **Shipments** — shipping and tracking\n- **Invoices** — billing (standalone mode)\n- **Payments** — payment recording (standalone mode)\n- **Leads** — sales pipeline\n- **Expenses** — expense submission and approval\n- **Assets** — equipment and tooling register\n- **Time Tracking** — timers and manual entries\n- **Quality** — QC templates and inspections\n- **Reports** — report builder and saved reports\n- **Calendar** — job due dates and PO deliveries\n- **Chat** — internal messaging\n- **AI** — AI assistant\n- **Training** — learning modules and paths\n- **Admin** — system configuration (admin only)\n- **Account** — your profile, compliance, security\n\n### Header Bar\n\nThe 44px header bar stays fixed at the top of every page. It contains:\n- **Search icon** — opens global search (searches jobs, parts, customers, vendors, and more)\n- **Notification bell** — shows unread count badge; click to open the notification panel\n- **Chat icon** — opens the chat panel; shows unread message count\n- **User avatar** — opens the user menu with theme toggle, account link, and logout\n\n### Theme Switching\n\nClick your avatar in the top-right and toggle between Light and Dark themes. Your choice is saved to localStorage and syncs across tabs immediately via the storage event. Dark theme swaps all CSS custom properties (--bg, --surface, --text, etc.).\n\n### Keyboard Shortcuts\n\nPress **?** (question mark) anywhere to see available keyboard shortcuts. Common shortcuts include:\n- **/** — focus global search\n- **Ctrl+K** — open command palette\n- **Escape** — close current dialog or panel\n\n### URL-Based Navigation\n\nEvery significant view state is reflected in the URL. This means:\n- You can bookmark any page, tab, or filtered view\n- Browser back/forward buttons work naturally\n- Sharing a URL puts the recipient on the exact same view\n- Refreshing the page preserves your state","sections":[]}"""
        });

        // ── Walkthrough ──────────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Navigating the App — Guided Tour",
            Slug = "navigating-the-app",
            Summary = "A guided tour of the sidebar, header, notifications, and main navigation areas.",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 5,
            IsPublished = true,
            IsOnboardingRequired = true,
            SortOrder = 2,
            AppRoutes = """["/dashboard"]""",
            Tags = """["onboarding","navigation","walkthrough"]""",
            ContentJson = """{"appRoute":"/dashboard","startButtonLabel":"Take the Tour","steps":[{"element":".sidebar","popover":{"title":"Sidebar Navigation","description":"The sidebar holds all your navigation links. Icons on the left expand to labeled menus when you hover over them. Click any icon to navigate to that section.","side":"right"}},{"element":".app-header","popover":{"title":"App Header","description":"The header stays visible on every page. Use the search icon to do a global search across jobs, parts, customers, and more.","side":"bottom"}},{"element":".app-header__actions","popover":{"title":"Notifications","description":"The bell icon shows your unread notification count. Click it to open the notification panel — you'll see job updates, assignments, and system alerts here.","side":"bottom"}},{"element":".user-trigger","popover":{"title":"User Menu","description":"Your avatar in the top-right opens the user menu. From here you can switch themes, access your account settings, or log out.","side":"bottom"}},{"element":".dashboard-content","popover":{"title":"Dashboard Widgets","description":"Your dashboard shows open jobs, today's tasks, active timers, and cycle progress. It updates in real time as work moves through the board.","side":"top"}}]}"""
        });

        // ── Field Reference (QuickRef) ───────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Navigation Field Reference",
            Slug = "navigation-field-reference",
            Summary = "Complete reference for every navigation element, header control, sidebar section, and keyboard shortcut.",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/dashboard"]""",
            Tags = """["navigation","reference"]""",
            ContentJson = """{"title":"Navigation Field Reference","groups":[{"heading":"Header Bar Controls","items":[{"label":"Search icon (magnifying glass)","value":"Opens global search overlay. Type to search across jobs, parts, customers, vendors, POs, and more. Results grouped by entity type. Press / to focus."},{"label":"Notification bell","value":"Shows unread notification count as a red badge. Click to open notification panel. Panel has tabs: All, Messages, Alerts. Supports mark-read, pin, dismiss, and dismiss-all."},{"label":"Chat icon","value":"Opens the chat slide-out panel. Shows unread message count badge. Panel lists conversations with last message preview and timestamp."},{"label":"User avatar","value":"Opens user menu dropdown. Contains: theme toggle (light/dark), Account link, Logout. Avatar shows your initials on your chosen background color."}]},{"heading":"Sidebar Sections","items":[{"label":"Sidebar width","value":"Collapsed: 52px (icons only). Expanded: 200px (icons + labels). State saved to user preferences."},{"label":"Collapse/expand","value":"Click the chevron toggle at the bottom of the sidebar, or hover to temporarily expand."},{"label":"Active indicator","value":"Current page is highlighted with a left border accent and background tint on the sidebar icon."},{"label":"Section grouping","value":"Related items grouped visually. Dividers separate major sections (Operations, Sales, Finance, Admin)."}]},{"heading":"Keyboard Shortcuts","items":[{"label":"/ (forward slash)","value":"Focus global search input"},{"label":"? (question mark)","value":"Open keyboard shortcuts help dialog"},{"label":"Escape","value":"Close current dialog, panel, or overlay"},{"label":"Ctrl+K","value":"Open command palette (quick navigation)"}]},{"heading":"URL State Patterns","items":[{"label":"Tabs","value":"Reflected as route segments: /admin/integrations, /inventory/receiving"},{"label":"Selected entity","value":"Query param: ?detail=job:1055, ?detail=part:42"},{"label":"Filters","value":"Query params: ?status=open&priority=high"},{"label":"Pagination","value":"Query params: ?page=2&pageSize=50"},{"label":"Wizard steps","value":"Query param: ?step=2"}]},{"heading":"Theme System","items":[{"label":"Light theme","value":"Default. White backgrounds, dark text. CSS custom properties: --bg: #ffffff, --surface: #f8f8f8"},{"label":"Dark theme","value":"Dark backgrounds, light text. Toggled via data-theme='dark' on <html>. CSS custom properties auto-swap."},{"label":"Persistence","value":"Saved to localStorage key 'themeMode'. Syncs across tabs via storage event."},{"label":"Reduced motion","value":"Animations disabled when prefers-reduced-motion is set in OS settings."}]}]}"""
        });

        // ── Knowledge Check (Quiz) ───────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Navigation Knowledge Check",
            Slug = "navigation-quiz",
            Summary = "Test your knowledge of app navigation, header controls, sidebar, keyboard shortcuts, and URL state.",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/training"]""",
            Tags = """["navigation","quiz"]""",
            ContentJson = """{"passingScore":80,"questionsPerQuiz":8,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"nav1","text":"You want to search for a specific job number across the entire app. What is the fastest way?","options":[{"id":"a","text":"Navigate to Backlog and use the search filter"},{"id":"b","text":"Click the search icon in the header or press / to open global search","isCorrect":true},{"id":"c","text":"Go to Reports and run a Job Summary report"},{"id":"d","text":"Open the Kanban Board and scroll through columns"}],"explanation":"Global search in the header searches across all entity types (jobs, parts, customers, etc.) simultaneously. Press / to focus it instantly from any page."},{"id":"nav2","text":"You want to share the exact filtered view of the Backlog with a teammate. What do you do?","options":[{"id":"a","text":"Take a screenshot and send it via chat"},{"id":"b","text":"Copy the URL from your browser's address bar — filters are encoded in query params","isCorrect":true},{"id":"c","text":"Export the backlog to CSV and email it"},{"id":"d","text":"Save a report with the same filters"}],"explanation":"All filter state is reflected in the URL as query parameters. Copying the URL gives your teammate the exact same view, including filters, sorting, and pagination."},{"id":"nav3","text":"The notification bell shows a red badge with '5'. What does this mean?","options":[{"id":"a","text":"5 system errors occurred in the last hour"},{"id":"b","text":"You have 5 unread notifications","isCorrect":true},{"id":"c","text":"5 jobs are overdue"},{"id":"d","text":"5 compliance forms need attention"}],"explanation":"The badge count on the notification bell shows unread notifications. These can be job updates, assignment changes, mentions, or system alerts."},{"id":"nav4","text":"How do you switch between Light and Dark theme?","options":[{"id":"a","text":"Admin → Settings → Theme"},{"id":"b","text":"Click your avatar in the top-right and toggle the theme switch","isCorrect":true},{"id":"c","text":"Account → Profile → Appearance"},{"id":"d","text":"Press Ctrl+D to toggle"}],"explanation":"The theme toggle is in the user menu dropdown, accessed by clicking your avatar in the top-right corner. Your choice is saved and syncs across browser tabs."},{"id":"nav5","text":"You're on the Expenses page and want to quickly navigate to Parts. Where do you click?","options":[{"id":"a","text":"Browser back button until you reach Parts"},{"id":"b","text":"Click the Parts icon in the left sidebar","isCorrect":true},{"id":"c","text":"Use the header breadcrumbs to navigate up"},{"id":"d","text":"Open global search and type 'Parts'"}],"explanation":"The sidebar is your primary navigation tool. Click the Parts icon (or its label if the sidebar is expanded) to navigate directly."},{"id":"nav6","text":"What keyboard shortcut opens the keyboard shortcuts help dialog?","options":[{"id":"a","text":"Ctrl+H"},{"id":"b","text":"F1"},{"id":"c","text":"? (question mark)","isCorrect":true},{"id":"d","text":"Ctrl+/"}],"explanation":"Press ? (question mark) from any page to see all available keyboard shortcuts. This works regardless of which page you're on."},{"id":"nav7","text":"The sidebar is collapsed and only showing icons. How do you see the full labels?","options":[{"id":"a","text":"Click the chevron toggle at the bottom of the sidebar, or hover to temporarily expand","isCorrect":true},{"id":"b","text":"Press Ctrl+B to toggle the sidebar"},{"id":"c","text":"Go to Account → Preferences → Sidebar"},{"id":"d","text":"You cannot expand it — the icons are the only navigation"}],"explanation":"Hover over the sidebar to temporarily expand it with full labels. Click the chevron toggle at the bottom to permanently expand. Your preference is saved."},{"id":"nav8","text":"You bookmarked a URL that ends with ?detail=job:1055. What happens when you open it?","options":[{"id":"a","text":"It shows an error because job detail URLs are temporary"},{"id":"b","text":"It opens the page and automatically opens the detail dialog for job 1055","isCorrect":true},{"id":"c","text":"It redirects to the Kanban Board filtered to job 1055"},{"id":"d","text":"It opens the job's PDF work order"}],"explanation":"The ?detail=type:id query parameter triggers the detail dialog to open automatically when the page loads. This makes bookmarks and shared links land on the exact view."}]}"""
        });

        Log.Information("Seeded Navigation training modules");
    }
}
