using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data.TrainingContent;

public class I18nLabelsTraining : TrainingContentBase
{
    public I18nLabelsTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override IReadOnlyList<string> Capabilities => ["CAP-ADMIN-I18N"];

    public override async Task SeedAsync()
    {
        await GetOrCreateModule(new TrainingModule
        {
            Title = "UI Labels — Renaming What Forge Calls Things",
            Slug = "i18n-labels-overview",
            Summary = "How an admin renames any label in Forge to match the shop's vocabulary, what happens in the other languages, and how to undo it.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/admin/i18n-labels"]""",
            Tags = """["admin","i18n","labels","languages"]""",
            ContentJson = """{"body":"## UI Labels\n\nEvery piece of text Forge shows — button names, column headings, menu entries, messages — comes from a catalog of labels, one copy per language. **UI Labels** lets an Admin change any of them so the screens use the words your shop already uses. If your people say *traveler* and Forge says *job card*, you fix it here once and it changes everywhere.\n\nYou find it under **Admin > Master Data > UI Labels**.\n\n### The page\n\nThe table lists every label in the catalog for the language chosen in the **Language** picker. Each row shows:\n\n- **Key** — the label's internal name, like `nav.kanban`. You never type these; they just tell you where the label is used.\n- **Base value** — the text Forge ships with.\n- **Override** — your replacement, if there is one.\n- **Status** — **Default** (unchanged), **Customized** (you changed it), **Machine** (translated automatically from a change you made in another language), or **Pending** (a machine translation that has not been produced yet).\n\nThe **Search** box matches on the key, the base text, or your override, so the fastest way to find a label is to type the words you see on screen.\n\n### Changing a label\n\nClick the pencil on a row. The dialog shows the key and the base value, and a box for your **Override value** — up to 2,000 characters. Save, and the new text is live for everyone the next time their screen loads.\n\n### The other languages\n\nForge is used in more than one language, and a label you rename in English would otherwise still say the old thing in Spanish. So the edit dialog has **Auto-translate to other languages**, on by default. With it on, saving your override also asks Forge's self-hosted AI module to translate it into each other configured language. Those rows show as **Machine** so you know a computer wrote them; you can open and correct any of them, and a corrected one becomes **Customized**.\n\nTwo rules keep this safe:\n\n- A label someone already customized by hand in another language is never overwritten by a machine translation.\n- If the AI module is unreachable when you save, your override is still saved and the translations are queued as **Pending**. Forge retries on its own; a **pending translation(s)** count and a **Retry now** button appear in the toolbar so you can push it along.\n\nTurn Auto-translate off when the word should stay the same in every language — a product name, a part number format, a brand.\n\n### Undoing a change\n\nThe **Revert** button on a customized row removes your override and restores the shipped text. It also removes any machine translations that were generated from it, so the other languages fall back to their shipped text too. Reverting a single machine-translated row only affects that row.\n\n### Labels that no longer exist\n\nWhen Forge is updated, a label you overrode may be retired from the catalog. Its row stays in the list with an empty base value so you can see it and revert it; it does nothing otherwise.\n\n### Good habits\n\nChange labels to match words your people already use, not to describe a feature differently from how it works. Keep overrides short — a label that fit as one word may wrap or truncate as three. And search before you edit: the same word often appears under several keys (a menu entry, a page title, a column heading), and consistency is the whole point.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Rename a UI Label — Guided Tour",
            Slug = "i18n-labels-walkthrough",
            Summary = "A guided tour of the UI Labels page: picking a language, finding a label, and opening it to edit.",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/admin/i18n-labels"]""",
            Tags = """["admin","i18n","walkthrough"]""",
            ContentJson = """{"appRoute":"/admin/i18n-labels","startButtonLabel":"Tour UI Labels","steps":[{"element":"[data-testid='i18n-labels-lang']","popover":{"title":"Language","description":"The table shows one language at a time. Edit in the language you write in; Auto-translate handles the others.","side":"bottom"}},{"element":"[data-testid='i18n-labels-search']","popover":{"title":"Search","description":"Type the words you see on screen. Search matches the key, the shipped text, and your override.","side":"bottom"}},{"element":"[data-testid='i18n-labels-edit-btn']","popover":{"title":"Edit","description":"The pencil opens the label. Type your Override value, decide whether to auto-translate it, and Save. A Revert button appears on the row once it is customized.","side":"left"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "UI Labels — Field Reference",
            Slug = "i18n-labels-quick-reference",
            Summary = "The table columns, the four status chips, the edit dialog fields, and the toolbar actions.",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/admin/i18n-labels"]""",
            Tags = """["admin","i18n","reference"]""",
            ContentJson = """{"title":"UI Labels Reference","groups":[{"heading":"Toolbar","items":[{"label":"Language","value":"Which language's catalog the table shows. Overrides are per language."},{"label":"Search","value":"Filters rows by key, base value, or override text."},{"label":"N pending translation(s)","value":"Appears when machine translations are waiting on the AI module."},{"label":"Retry now","value":"Asks Forge to produce the pending translations immediately instead of waiting for the automatic retry."}]},{"heading":"Table Columns","items":[{"label":"Key","value":"The label's internal name, such as nav.kanban. Read-only."},{"label":"Base Value","value":"The text Forge ships with for this language. Empty when the label has been retired from the catalog."},{"label":"Override","value":"Your replacement text, or a dash when there is none."},{"label":"Status","value":"Default, Customized, Machine, or Pending."}]},{"heading":"Status Chips","items":[{"label":"Default","value":"No override; the shipped text is shown."},{"label":"Customized","value":"An admin typed this text."},{"label":"Machine","value":"Translated automatically from an override made in another language. Editable."},{"label":"Pending","value":"A machine translation that has not been produced yet; the shipped text is shown meanwhile."}]},{"heading":"Edit Dialog","items":[{"label":"Override value (required)","value":"The replacement text, up to 2,000 characters."},{"label":"Auto-translate to other languages","value":"On by default. Sends the override to the AI module for each other configured language. Never overwrites a hand-made override."},{"label":"Save","value":"Stores the override; it is live on the next screen load."}]},{"heading":"Row Actions","items":[{"label":"Edit (pencil)","value":"Opens the dialog with the current override, or the base value if there is none."},{"label":"Revert","value":"Removes the override and any machine translations generated from it. Only shown on rows that have an override."}]}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "UI Labels — Knowledge Check",
            Slug = "i18n-labels-quiz",
            Summary = "Four questions on overrides, machine translation, pending rows, and revert.",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/admin/i18n-labels"]""",
            Tags = """["admin","i18n","quiz"]""",
            ContentJson = """{"passingScore": 75, "shuffleQuestions": false, "shuffleOptions": true, "showExplanationsAfterSubmit": true, "questions": [{"id": "q1", "text": "You changed the English label \"Job Card\" to \"Traveler\" with Auto-translate on. What happens to the Spanish label?", "options": [{"id": "q1a", "text": "Nothing; each language must be edited separately", "isCorrect": false}, {"id": "q1b", "text": "It is machine-translated and marked Machine, unless someone already customized it by hand", "isCorrect": true}, {"id": "q1c", "text": "It is set to the English word Traveler", "isCorrect": false}, {"id": "q1d", "text": "It is deleted until an admin retypes it", "isCorrect": false}], "explanation": "Auto-translate fans the change out to the other languages as editable Machine rows. A hand-made override in another language is never overwritten."}, {"id": "q2", "text": "A row shows the status Pending. What does that mean?", "options": [{"id": "q2a", "text": "An admin has to approve the change before it goes live", "isCorrect": false}, {"id": "q2b", "text": "The label has been retired from the catalog", "isCorrect": false}, {"id": "q2c", "text": "The AI module could not be reached, so the translation is queued and will be retried", "isCorrect": true}, {"id": "q2d", "text": "The override is longer than the allowed length", "isCorrect": false}], "explanation": "Pending is a machine translation Forge could not produce yet. It retries on its own, and Retry now pushes it immediately."}, {"id": "q3", "text": "When should you turn Auto-translate off?", "options": [{"id": "q3a", "text": "Whenever you edit the English catalog", "isCorrect": false}, {"id": "q3b", "text": "When the word should read the same in every language, such as a product or brand name", "isCorrect": true}, {"id": "q3c", "text": "Never; it cannot be turned off", "isCorrect": false}, {"id": "q3d", "text": "When the label is longer than one word", "isCorrect": false}], "explanation": "Names that do not translate should not be sent for translation. For ordinary labels leave it on so the other languages keep up."}, {"id": "q4", "text": "What does Revert do on a customized row?", "options": [{"id": "q4a", "text": "Restores the shipped text and removes machine translations generated from that override", "isCorrect": true}, {"id": "q4b", "text": "Deletes the label from every screen", "isCorrect": false}, {"id": "q4c", "text": "Sends the label back to the AI module for a fresh translation", "isCorrect": false}, {"id": "q4d", "text": "Restores the shipped text in this language only, keeping the machine translations", "isCorrect": false}], "explanation": "Revert removes the override and the machine translations that were made from it, so all languages fall back to the shipped text."}]}"""
        });
    }
}
