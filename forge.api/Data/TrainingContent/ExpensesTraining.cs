using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

using Serilog;

namespace Forge.Api.Data.TrainingContent;

public class ExpensesTraining : TrainingContentBase
{
    public ExpensesTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override async Task SeedAsync()
    {
        // ── Overview (Article) ───────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Expenses Overview",
            Slug = "expenses-overview",
            Summary = "What the Expenses module does: submission flow, approval workflow, status tracking, and manager review.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/expenses"]""",
            Tags = """["expenses","approval"]""",
            ContentJson = """
{
  "body": "## Expenses Overview\n\nThe Expenses module lets you submit, track, and manage company expenses in one searchable table. Managers can approve or reject pending expenses right from the table.\n\n### Submitting an Expense\n\nTo submit an expense:\n1. Go to the Expenses page.\n2. Click **Add Expense** in the page header.\n3. Enter the Amount (required, at least $0.01), Date (required), and Category (required — categories are set up by your admin).\n4. Add a Description if you like.\n5. Click **Submit**. The expense becomes **Pending**.\n\nOnce submitted, the expense shows up in the main table for everyone to see.\n\n### Approving Expenses\n\nManagers and admins can approve or reject pending expenses right in the table. Each pending row shows two quick buttons:\n- **Approve** (checkmark) — marks the expense Approved (green label).\n- **Reject** (X) — marks the expense Rejected (red label).\n\nThese buttons only appear on expenses that are still Pending.\n\n### Filtering and Search\n\nThe page header gives you:\n- A **Search** box that filters by description or category.\n- A **Status** menu to show All, Pending, Approved, Rejected, Self-approved, or Needs revision.\n- A **Total Amount** that adds up the expenses you can currently see.\n\n### Expense Statuses\n\n- **Pending** (yellow label) — Waiting for a manager to review it.\n- **Approved** (green label) — Reviewed and approved.\n- **Rejected** (red label) — Reviewed and turned down.\n- **Self-approved** (green label) — Submitted by a manager who can approve their own expenses.\n\n### Your Work Is Saved\n\nIf you close the expense window by accident before saving, your entries are kept and filled back in the next time you open it.",
  "sections": []
}
"""
        });

        // ── Walkthrough ──────────────────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Expenses — Guided Tour",
            Slug = "expenses-walkthrough",
            Summary = "A guided tour of the Expenses page: filters, table columns, inline approval actions, and creating a new expense.",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 7,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/expenses"]""",
            Tags = """["expenses","walkthrough"]""",
            ContentJson = """
{
  "appRoute": "/expenses",
  "startButtonLabel": "Tour Expenses",
  "steps": [
    {
      "element": ".filters-bar app-input",
      "popover": {
        "title": "Search Expenses",
        "description": "Search your expenses by description or category. Type a word and press Enter to filter the list.",
        "side": "bottom"
      }
    },
    {
      "element": "[data-testid='status-filter']",
      "popover": {
        "title": "Status Filter",
        "description": "Show only expenses in a certain state, such as Pending, Approved, Rejected, or Needs revision. Each state has its own colored label in the table.",
        "side": "bottom"
      }
    },
    {
      "element": "app-data-table",
      "popover": {
        "title": "Expenses Table",
        "description": "Every submitted expense shows up here, with details such as its date, category, description, linked job, who submitted it, amount, and status. Click a column header to sort. Pending rows show quick approve and reject buttons.",
        "side": "top"
      }
    },
    {
      "element": "[data-testid='expense-save-btn'], [data-testid='new-expense-btn'], .action-btn--primary",
      "popover": {
        "title": "Add Expense",
        "description": "Click here to submit a new expense. You'll enter the amount, date, and category (all required), plus an optional description. The new expense starts out Pending.",
        "side": "bottom"
      }
    }
  ]
}
"""
        });

        // ── Field Reference (QuickRef) ───────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Expenses Field Reference",
            Slug = "expenses-field-reference",
            Summary = "Complete reference for every field, button, status, validation rule, and table column in the Expenses module.",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/expenses"]""",
            Tags = """["expenses","reference"]""",
            ContentJson = """
{
  "title": "Expenses Field Reference",
  "groups": [
    {
      "heading": "Create Expense Dialog Fields",
      "items": [
        {"label": "Amount (required)", "value": "The dollar amount of the expense. Must be at least $0.01."},
        {"label": "Date (required)", "value": "The date the expense happened. Shown as MM/dd/yyyy."},
        {"label": "Category (required)", "value": "Pick a category from the menu. Categories are set up by your admin."},
        {"label": "Description (optional)", "value": "A short note about what the expense was for."},
        {"label": "Submit button", "value": "Stays grayed out until the form is complete. If it won't turn on, click the warning triangle next to it to see what's still missing."}
      ]
    },
    {
      "heading": "Expense Statuses",
      "items": [
        {"label": "Pending (yellow label)", "value": "Submitted and waiting for a manager to review it. This is where new expenses start."},
        {"label": "Approved (green label)", "value": "Reviewed and approved by a manager."},
        {"label": "Rejected (red label)", "value": "Reviewed and turned down by a manager."},
        {"label": "Self-approved (green label)", "value": "Submitted by a manager who can approve their own expenses. Shows the same green label as Approved."}
      ]
    },
    {
      "heading": "Table Columns",
      "items": [
        {"label": "Date", "value": "The date of the expense, shown as MM/dd/yyyy. Click to sort."},
        {"label": "Category", "value": "The expense category, shown as a colored label. Click to sort."},
        {"label": "Description", "value": "The note describing the expense. Click to sort."},
        {"label": "Job", "value": "The linked job number, or a dash if it isn't tied to a job. Click to sort."},
        {"label": "Submitted By", "value": "Who submitted the expense. Click to sort."},
        {"label": "Amount", "value": "The dollar amount. Click to sort."},
        {"label": "Status", "value": "A colored label showing the state: Pending (yellow), Approved (green), Rejected (red), Self-approved (green). Sort or filter by it."},
        {"label": "Actions", "value": "Quick approve (checkmark) and reject (X) buttons. Only shown on Pending expenses."}
      ]
    },
    {
      "heading": "Page Header Controls",
      "items": [
        {"label": "Search", "value": "Filters expenses by description or category. Press Enter to apply."},
        {"label": "Status Filter", "value": "A menu to show only one state: All, Pending, Approved, Rejected, Self-approved, or Needs revision."},
        {"label": "Total Amount", "value": "Adds up the expenses you can currently see (after any search or filter)."},
        {"label": "Add Expense button", "value": "Opens the window for creating a new expense."}
      ]
    },
    {
      "heading": "Inline Approval Actions",
      "items": [
        {"label": "Approve (checkmark)", "value": "A green button that approves the expense right away. Only shown on Pending expenses."},
        {"label": "Reject (X)", "value": "A red button that turns down the expense right away. Only shown on Pending expenses."}
      ]
    },
    {
      "heading": "Validation Rules",
      "items": [
        {"label": "Amount", "value": "Required. Must be at least $0.01."},
        {"label": "Date", "value": "Required. Must be a valid date."},
        {"label": "Category", "value": "Required. Pick one of the categories your admin set up."},
        {"label": "Description", "value": "Optional. Any length is fine."},
        {"label": "Why Submit is grayed out", "value": "When something's missing, click the warning triangle next to the Submit button to see which fields still need attention."},
        {"label": "Your work is saved", "value": "As you type, your entries are saved automatically and filled back in if you reopen the window before submitting."}
      ]
    }
  ]
}
"""
        });

        // ── Knowledge Check (Quiz) ───────────────────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Expenses Knowledge Check",
            Slug = "expenses-quiz",
            Summary = "Test your knowledge of the Expenses module: submission, approval workflow, statuses, and filtering.",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/expenses"]""",
            Tags = """["expenses","quiz","approval"]""",
            ContentJson = """
{
  "passingScore": 80,
  "questionsPerQuiz": 8,
  "shuffleOptions": true,
  "showExplanationsAfterSubmit": true,
  "questions": [
    {
      "id": "ex1",
      "text": "You need to submit a $150 expense for office supplies. What fields are required in the Create Expense dialog?",
      "options": [
        {"id": "a", "text": "Amount, Date, Category, and Description are all required"},
        {"id": "b", "text": "Amount, Date, and Category are required; Description is optional", "isCorrect": true},
        {"id": "c", "text": "Only Amount is required — all other fields are optional"},
        {"id": "d", "text": "Amount, Date, Category, Description, and Job are all required"}
      ],
      "explanation": "The Create Expense window requires three fields: Amount (at least $0.01), Date, and Category (from the categories your admin set up). Description is optional."
    },
    {
      "id": "ex2",
      "text": "What status does a newly submitted expense start with?",
      "options": [
        {"id": "a", "text": "Draft — it needs to be finalized before submission"},
        {"id": "b", "text": "Approved — expenses are auto-approved by default"},
        {"id": "c", "text": "Pending — it awaits manager approval", "isCorrect": true},
        {"id": "d", "text": "Self-approved — the submitter's own expenses are auto-approved"}
      ],
      "explanation": "All newly submitted expenses start out Pending (yellow label) and wait for a manager to review them."
    },
    {
      "id": "ex3",
      "text": "A manager wants to approve a pending expense. How do they do it?",
      "options": [
        {"id": "a", "text": "Click the checkmark icon on the expense row to approve it immediately", "isCorrect": true},
        {"id": "b", "text": "Select the expense and click a bulk Approve All button"},
        {"id": "c", "text": "Open the expense detail and change the status menu to Approved"},
        {"id": "d", "text": "Right-click the row and select Approve from the context menu"}
      ],
      "explanation": "Each pending expense row has two quick buttons: a checkmark (approve) and an X (reject). Clicking the checkmark approves that expense right away."
    },
    {
      "id": "ex4",
      "text": "Where do expense categories come from?",
      "options": [
        {"id": "a", "text": "They are hardcoded in the application and cannot be changed"},
        {"id": "b", "text": "Each user defines their own categories in their profile settings"},
        {"id": "c", "text": "They are set up by an admin in the Admin section", "isCorrect": true},
        {"id": "d", "text": "They are imported from QuickBooks and cannot be modified locally"}
      ],
      "explanation": "Expense categories are set up in the Admin section. An administrator can add, edit, or remove the categories that appear in the Category menu."
    },
    {
      "id": "ex5",
      "text": "What does the Total Amount in the page header show?",
      "options": [
        {"id": "a", "text": "The total of all expenses ever submitted in the system"},
        {"id": "b", "text": "The sum of currently visible (filtered) expenses", "isCorrect": true},
        {"id": "c", "text": "The total of only Approved expenses"},
        {"id": "d", "text": "The monthly budget remaining for the current user"}
      ],
      "explanation": "The Total Amount in the page header adds up all the expenses you can currently see, after any search or status filter is applied."
    },
    {
      "id": "ex6",
      "text": "You accidentally close the expense window before saving. What happens to what you entered?",
      "options": [
        {"id": "a", "text": "It is lost — you must re-enter everything from scratch"},
        {"id": "b", "text": "A pop-up asks if you want to save a draft before closing"},
        {"id": "c", "text": "Your entries are saved automatically and filled back in when you reopen the window", "isCorrect": true},
        {"id": "d", "text": "The data is saved to the server as a Draft expense"}
      ],
      "explanation": "The expense window saves your work as you type, so if you close it by accident, your entries are filled back in the next time you open it."
    },
    {
      "id": "ex7",
      "text": "How can you filter the expenses table to see only rejected expenses?",
      "options": [
        {"id": "a", "text": "Type 'rejected' in the search box"},
        {"id": "b", "text": "Use the Status filter menu and pick 'Rejected'", "isCorrect": true},
        {"id": "c", "text": "Click the Rejected column header to sort by rejections"},
        {"id": "d", "text": "Go to a separate Rejected Expenses page"}
      ],
      "explanation": "The Status filter in the page header lets you pick one state: All, Pending, Approved, Rejected, Self-approved, or Needs revision. Picking 'Rejected' shows only rejected expenses."
    },
    {
      "id": "ex8",
      "text": "In the Expense Approval Queue, a manager opens an expense and wants to reject it (or send it back for changes). What does the review window require?",
      "options": [
        {"id": "a", "text": "Nothing extra — Reject is a single click with no note"},
        {"id": "b", "text": "A review note of at least 10 characters before Reject or Request Revision is allowed", "isCorrect": true},
        {"id": "c", "text": "A receipt must be attached before the expense can be rejected"},
        {"id": "d", "text": "The expense must be deleted and the submitter re-creates it from scratch"}
      ],
      "explanation": "Opening an expense in the Approval Queue shows a review window with three choices: Approve, Request Revision, and Reject. Both Reject and Request Revision need a review note of at least 10 characters; only Approve can go through without one. Rejected shows a red label, while Request Revision sets the expense to Needs Revision so the submitter can fix and resubmit it."
    },
    {
      "id": "ex9",
      "text": "The search input in the page header filters by which fields?",
      "options": [
        {"id": "a", "text": "Only the Description field"},
        {"id": "b", "text": "Description and Amount"},
        {"id": "c", "text": "Description and Category", "isCorrect": true},
        {"id": "d", "text": "All visible table columns including Date and Status"}
      ],
      "explanation": "The search box filters by description and category only. It doesn't search by submitter name, date, amount, or status — use the Status menu to filter by status."
    },
    {
      "id": "ex10",
      "text": "What does the 'Self-approved' status mean on an expense?",
      "options": [
        {"id": "a", "text": "The expense was approved by the system automatically based on rules"},
        {"id": "b", "text": "The expense was submitted by a manager who can approve their own expenses", "isCorrect": true},
        {"id": "c", "text": "The submitter clicked an Approve button on their own expense"},
        {"id": "d", "text": "The expense was under a threshold amount and was auto-approved"}
      ],
      "explanation": "Self-approved means the expense was submitted by a manager who's allowed to approve their own expenses. It shows a green label, the same as Approved."
    },
    {
      "id": "ex11",
      "text": "What is the minimum amount you can enter when creating an expense?",
      "options": [
        {"id": "a", "text": "$0.00 — zero-dollar expenses are allowed for tracking purposes"},
        {"id": "b", "text": "$0.01 — the minimum is one cent", "isCorrect": true},
        {"id": "c", "text": "$1.00 — expenses under a dollar are not supported"},
        {"id": "d", "text": "There is no minimum — any positive number works"}
      ],
      "explanation": "The Amount must be at least $0.01. You can't submit an expense for $0.00 or a negative amount."
    },
    {
      "id": "ex12",
      "text": "Which of the following is NOT a column in the Expenses table?",
      "options": [
        {"id": "a", "text": "Category"},
        {"id": "b", "text": "Submitted By"},
        {"id": "c", "text": "Vendor", "isCorrect": true},
        {"id": "d", "text": "Job"}
      ],
      "explanation": "The Expenses table columns are: Date, Category, Description, Job, Submitted By, Amount, Status, and Actions. There is no Vendor column. (Vendor is a field inside the expense window, not a table column.)"
    },
    {
      "id": "ex13",
      "text": "When are the quick approve/reject buttons shown on an expense row?",
      "options": [
        {"id": "a", "text": "Always — on every expense regardless of status"},
        {"id": "b", "text": "Only for expenses with Pending status", "isCorrect": true},
        {"id": "c", "text": "Only for expenses submitted by other users"},
        {"id": "d", "text": "Only for expenses over $100"}
      ],
      "explanation": "The quick approve (checkmark) and reject (X) buttons only appear on expenses that are still Pending. Once approved or rejected, the buttons are hidden."
    },
    {
      "id": "ex14",
      "text": "What color label does each expense status show?",
      "options": [
        {"id": "a", "text": "Pending: blue, Approved: green, Rejected: red, Self-approved: gray"},
        {"id": "b", "text": "Pending: yellow, Approved: green, Rejected: red, Self-approved: green", "isCorrect": true},
        {"id": "c", "text": "Pending: gray, Approved: blue, Rejected: yellow, Self-approved: green"},
        {"id": "d", "text": "All statuses use the same gray label"}
      ],
      "explanation": "Pending shows a yellow label, Approved and Self-approved both show green labels, and Rejected shows a red label."
    },
    {
      "id": "ex15",
      "text": "The Submit button in the Create Expense window is grayed out. How do you find out which fields still need attention?",
      "options": [
        {"id": "a", "text": "Nothing happens — the button is just grayed out"},
        {"id": "b", "text": "A tooltip says 'Please fill in all fields'"},
        {"id": "c", "text": "Click the warning triangle next to the Submit button to see which specific fields still need attention", "isCorrect": true},
        {"id": "d", "text": "The empty fields flash red to draw your attention"}
      ],
      "explanation": "When something's missing, click the warning triangle next to the grayed-out Submit button to see each field that still needs attention — for example, 'Amount is required' or 'Category is required'."
    }
  ]
}
"""
        });

        // ── Receipts & Vendor-Settled (Article) ──────────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Expense Receipts & Vendor-Settled Expenses",
            Slug = "expenses-receipts-vendor-settled",
            Summary = "Attach a receipt (and when it's mandatory), plus how naming a vendor routes the expense to Accounts Payable and becomes a vendor bill on approval.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 5,
            AppRoutes = """["/expenses"]""",
            Tags = """["expenses","receipts","vendor","payables"]""",
            ContentJson = """
{
  "body": "## Receipts and Vendor-Settled Expenses\n\nThe expense window has two fields that change *how* an expense is handled later on: the **receipt** and the **Vendor** field. This module explains both.\n\n### Attaching a Receipt\n\nWhen you create or resubmit an expense, the **Receipt** row lets you attach proof of purchase:\n\n1. Click **Upload Receipt**.\n2. Pick an image or a PDF. The file uploads right away, and the row shows the file name with a paperclip.\n3. To swap it, click the red **X** next to the file name to remove it, then upload a different file.\n\nThe receipt stays attached to the expense, so reviewers can open it while deciding.\n\n### When a Receipt Is Required\n\nYour company can require a receipt on every expense. When that's turned on:\n\n- The Receipt label shows a red asterisk (**\\***), the same mark used on other required fields.\n- The **Submit** button stays grayed out until you attach a receipt — even if everything else is filled in.\n- Hovering over the grayed-out Submit button shows a note explaining that a receipt is required.\n\nSo if Submit won't turn on and every field looks complete, check whether a receipt is still missing. When your company doesn't require one, the receipt is optional and Submit won't wait on it.\n\n### Out-of-Pocket vs Vendor-Settled\n\nEvery expense is one of two kinds, set by the **Vendor** field:\n\n- **Out-of-pocket (cash)** — leave Vendor set to *No vendor*. This is the default. It means an employee paid out of their own pocket and expects to be paid back. It stays entirely inside Expenses.\n- **Vendor-settled** — pick a vendor from the **Vendor** menu. This means the money is owed to that vendor and the company hasn't paid yet. A note appears under the field explaining what will happen.\n\n### What a Vendor-Settled Expense Does on Approval\n\nNaming a vendor sends the expense to **Accounts Payable**. When the expense is **approved**, it turns into a *vendor bill* that gets paid through Payables — from there it follows the normal path of vendor bills and vendor payments.\n\nAfter that happens, the expense row shows a small **label with the vendor-bill number** next to its status. Clicking that label opens the linked bill. That's how you trace an approved vendor-settled expense to the bill it became.\n\nGood to know:\n- The **Vendor** field lives in the create/resubmit window — it is *not* a column in the main expenses table, so don't look for a Vendor column there. Instead, the link shows up as the bill-number label beside the status.\n- The bill is created at **approval**, not when you submit. A pending vendor-settled expense doesn't have a bill yet.\n- Out-of-pocket expenses never create a vendor bill — they're reimbursements, not bills to pay.\n\n### Quick Decision Guide\n\n| You spent money… | Vendor field | What happens on approval |\n|---|---|---|\n| Personally, expect to be paid back | *No vendor* | Stays an expense (reimbursement) |\n| On the company's behalf, vendor not yet paid | Select the vendor | Becomes a vendor bill in Payables |",
  "sections": []
}
"""
        });

        // ── Approval Queue & Revision (Walkthrough) ──────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Expense Approval Queue & Requesting Revision",
            Slug = "expenses-approval-queue",
            Summary = "Work the dedicated Expense Approval Queue: open an expense to review it, and use Approve, Request Revision, or Reject with the required review note.",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 7,
            IsPublished = true,
            SortOrder = 6,
            AppRoutes = """["/expenses/approval"]""",
            Tags = """["expenses","approval","walkthrough","manager"]""",
            ContentJson = """
{
  "appRoute": "/expenses/approval",
  "startButtonLabel": "Tour the Approval Queue",
  "steps": [
    {
      "element": "app-page-layout",
      "popover": {
        "title": "Expense Approval Queue",
        "description": "This queue lists every expense that's still Pending, from everyone, so managers can review them in one place instead of hunting through the main table. It's separate from the quick approve and reject buttons on the main Expenses list.",
        "side": "bottom"
      }
    },
    {
      "element": "app-input",
      "popover": {
        "title": "Search Pending Expenses",
        "description": "Narrow the queue by typing a category or description and pressing Enter. Only Pending expenses ever show up here.",
        "side": "bottom"
      }
    },
    {
      "element": ".approval-queue__summary",
      "popover": {
        "title": "Pending Count & Total",
        "description": "This summary shows how many expenses are waiting on your decision and their combined dollar total — a quick read on how much you still have to review.",
        "side": "left"
      }
    },
    {
      "element": "app-data-table",
      "popover": {
        "title": "Open an Expense to Review",
        "description": "Click any row (or its checkmark/X buttons) to open the review window. It shows the submitter, date, category, amount, description, and linked job, plus a Review Note field and three buttons: Approve, Request Revision, and Reject.",
        "side": "top"
      }
    }
  ]
}
"""
        });

        // ── Approval Queue & Revision detail (Article) ───────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Reviewing Expenses: Approve, Request Revision, or Reject",
            Slug = "expenses-review-decisions",
            Summary = "The three outcomes in the expense review dialog and the 10-character note rule that governs Reject and Request Revision.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 7,
            AppRoutes = """["/expenses/approval"]""",
            Tags = """["expenses","approval","manager","revision"]""",
            ContentJson = """
{
  "body": "## Reviewing an Expense\n\nClicking a row in the Expense Approval Queue opens the **review window**. It shows the submission's details — submitter, date, category, amount, description, and linked job (when there is one) — above a **Review Note** field and three buttons.\n\n### The Three Choices\n\n- **Approve** — accepts the expense. It moves to Approved (green label). If the expense named a vendor, approving it also turns it into a vendor bill (see *Expense Receipts & Vendor-Settled Expenses*). Approve does **not** need a note; you can add one for context, but it's optional.\n- **Request Revision** — sends the expense back to the submitter without turning it down outright. The status becomes **Needs Revision**, and your note becomes the feedback the submitter sees. Use this when the expense is fixable — wrong category, missing receipt, amount needs explaining — and you'd rather the submitter fix it and resubmit than start over.\n- **Reject** — turns the expense down for good (red label). Use this when the expense shouldn't be paid at all.\n\n### The Review-Note Rule\n\nThe **Review Note** field controls the two negative choices:\n\n- **Reject and Request Revision both need a note of at least 10 characters.** Until the note is long enough, both buttons stay grayed out, and a hint under the field counts your progress.\n- **Approve needs no note.** It's available no matter what's in the note field.\n\nThis is on purpose: telling a submitter *why* their expense was sent back is required, so they can fix it (Request Revision) or understand the decision (Reject). You can't turn one down silently.\n\n### After You Decide\n\nThe window closes, the queue refreshes (the expense you just handled drops off the Pending list), and a short message confirms what happened. A Needs-Revision expense comes back for the submitter to edit and resubmit; an Approved or Rejected one is final as far as the queue is concerned.\n\n### Who Sees This Queue\n\nThe Approval Queue is for managers. Regular submitters use the main Expenses list to create and track their own expenses; the queue is where reviewers clear out the pending backlog."
}
"""
        });

        // ── Editing, Resubmitting & Deleting (Walkthrough) ───────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Editing, Resubmitting & Deleting Your Expenses",
            Slug = "expenses-edit-resubmit-delete",
            Summary = "Fix and resubmit an expense that came back as Needs Revision, and manage your own expense submissions.",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 8,
            AppRoutes = """["/expenses"]""",
            Tags = """["expenses","walkthrough","revision","resubmit"]""",
            ContentJson = """
{
  "appRoute": "/expenses",
  "startButtonLabel": "Tour Editing & Resubmitting",
  "steps": [
    {
      "element": "[data-testid='status-filter']",
      "popover": {
        "title": "Find Returned Expenses",
        "description": "When a reviewer sends an expense back, it gets the Needs Revision status (a yellow label). Open this Status filter and pick 'Needs Revision' to see just the ones waiting on you to fix.",
        "side": "bottom"
      }
    },
    {
      "element": "app-data-table",
      "popover": {
        "title": "The Resubmit Action",
        "description": "A Needs-Revision row shows a pencil (edit) button at the right end. Click it to reopen the expense in a 'Resubmit Expense' window, already filled in with everything you entered before.",
        "side": "top"
      }
    },
    {
      "element": "[data-testid='new-expense-btn']",
      "popover": {
        "title": "Or Create a Fresh Expense",
        "description": "This same window is used to create new expenses. When resubmitting, it also shows the reviewer's feedback note at the top so you know exactly what to change.",
        "side": "bottom"
      }
    }
  ]
}
"""
        });

        // ── Recurring & Upcoming Expenses (Article) ──────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Recurring & Upcoming Expenses",
            Slug = "expenses-recurring-upcoming",
            Summary = "Set up recurring expense templates that auto-generate on a schedule, and read the 90-day Upcoming forecast.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 9,
            AppRoutes = """["/expenses/upcoming"]""",
            Tags = """["expenses","recurring","forecast","upcoming"]""",
            ContentJson = """
{
  "body": "## Recurring & Upcoming Expenses\n\nThe Recurring & Upcoming page handles predictable, repeating costs — subscriptions, leases, insurance, utilities — so you don't have to remember to file them by hand each cycle. It has two tabs: **Upcoming** and **Recurring**.\n\n### Recurring Tab — the Templates\n\nA **recurring expense** is a template that creates real expenses automatically on a schedule. Click **New Recurring** to set one up:\n\n- **Amount** (required) and **Frequency** (required) — Weekly, Bi-weekly, Monthly, Quarterly, or Annually.\n- **Category** (required) and **Classification** (required). Classification is the kind of repeating cost (Subscription, Lease, Insurance, Utility, Maintenance Contract, License, Membership, or Other) and sets the colored label.\n- **Description** (required) and **Vendor** (optional).\n- **Start Date** (required) and an optional **End Date** — leave End Date blank if it runs indefinitely.\n- **Auto-approve** switch — when on, the expenses this template creates skip the pending queue.\n\nThe Recurring tab lists every template with its **Next Due** date and an **Active** label. Two buttons on each row let you manage a template:\n\n- The **pause / play** button switches the template between Active and Paused. Pausing stops it from creating new expenses without deleting its history.\n- The **delete** (trash) button removes the template after a confirmation. Note that templates can only be paused or deleted — there's no editing in place. To change the terms, pause the old one, or delete it and set up a new one.\n\n### How New Expenses Get Created\n\nActive templates create their expense on each occurrence — you don't have to do anything. A template with Auto-approve on produces already-approved expenses; without it, each new expense lands in the normal Pending queue for review, just like one you'd enter by hand.\n\n### Upcoming Tab — the 90-Day Forecast\n\nThe **Upcoming** tab looks ahead: it shows every expense your active templates will create over the next **90 days**. It doesn't create anything — it's just a forecast so you can see what's coming.\n\n- The **90-Day Total** adds up everything expected in that window.\n- A **monthly breakdown** shows the total and count for each month, so you can see how the load spreads out.\n- The **Highlight Classification** picker lets you spotlight one kind of cost (say, all Subscriptions) — matching rows are highlighted rather than hidden, so you keep the full picture while emphasizing one type.\n- Each row shows the expected due date, classification, category, description, vendor, amount, and frequency.\n\nUse Upcoming to plan for cash needs; use Recurring to manage the templates behind it. Pausing a template on the Recurring tab immediately drops its future occurrences from the Upcoming forecast."
}
"""
        });

        // ── Recurring & Upcoming Quick Reference (QuickRef) ──────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Recurring & Upcoming Expenses — Quick Reference",
            Slug = "expenses-recurring-quickref",
            Summary = "Fast reference for recurring-expense fields, frequencies, classifications, row actions, and the Upcoming forecast.",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 10,
            AppRoutes = """["/expenses/upcoming"]""",
            Tags = """["expenses","recurring","reference"]""",
            ContentJson = """
{
  "title": "Recurring & Upcoming Expenses Quick Reference",
  "groups": [
    {
      "heading": "New Recurring Expense Fields",
      "items": [
        {"label": "Amount (required)", "value": "The amount each created expense will be for. Must be at least $0.01."},
        {"label": "Frequency (required)", "value": "How often the template creates an expense: Weekly, Bi-weekly, Monthly, Quarterly, or Annually."},
        {"label": "Category (required)", "value": "The expense category to use for the created expenses."},
        {"label": "Classification (required)", "value": "The kind of repeating cost: Subscription, Lease, Insurance, Utility, Maintenance Contract, License, Membership, or Other."},
        {"label": "Description (required)", "value": "What the repeating cost is for."},
        {"label": "Vendor (optional)", "value": "The vendor name, typed in."},
        {"label": "Start Date (required)", "value": "The date of the first expense."},
        {"label": "End Date (optional)", "value": "Leave blank if it runs indefinitely."},
        {"label": "Auto-approve", "value": "An on/off switch. When on, the created expenses skip the Pending queue."}
      ]
    },
    {
      "heading": "Recurring Tab Columns & Actions",
      "items": [
        {"label": "Next Due", "value": "The next date this template will create an expense."},
        {"label": "Active label", "value": "Green 'Active' or gray 'Paused' — whether the template is currently creating expenses."},
        {"label": "Pause / Play button", "value": "Switches the template between Active and Paused. Pausing keeps its history but stops new expenses."},
        {"label": "Delete button", "value": "Removes the template after a confirmation. Templates can't be edited in place — only paused or deleted."}
      ]
    },
    {
      "heading": "Upcoming (Forecast) Tab",
      "items": [
        {"label": "Window", "value": "Shows the next 90 days of expenses from active templates. It's a forecast only — it creates nothing."},
        {"label": "90-Day Total", "value": "Adds up everything expected in the 90-day window."},
        {"label": "Monthly breakdown", "value": "The total and item count for each month."},
        {"label": "Highlight Classification", "value": "Spotlights one kind of cost by highlighting matching rows (it doesn't hide the rest)."},
        {"label": "Pausing effect", "value": "Pausing a template on the Recurring tab immediately drops its future expenses from this forecast."}
      ]
    }
  ]
}
"""
        });

        // ── Advanced Reference (side documentation) ──────────────────────
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Expenses — Advanced Reference",
            Slug = "expenses-reference",
            Summary = "Edge-case and power-user reference for Expenses: policy settings & validators, vendor-bill promotion invariants, recurrence math, direct-URL sub-routes, and capability/role gating.",
            ContentType = TrainingContentType.Reference,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 90,
            AppRoutes = """["/expenses"]""",
            Tags = """["expenses","reference","advanced"]""",
            ContentJson = """
{"body":"Advanced, ancillary notes for the Expenses area — the policy machinery, accounting-promotion invariants, recurrence math, hidden routes, and gating that the core modules don't cover. Reference material, not a tutorial.\n\n## Expense Policy Settings & Dynamic Validators\n\nAn install can constrain expense submissions through per-install policy, stored as `expense_*` rows in `system_settings` and surfaced by `GET /api/v1/expenses/settings` (returns `ExpenseSettingsResponse`). Five knobs:\n\n- **`expense_max_amount`** (`MaxAmount`, decimal, optional) — caps a single expense; over-cap submissions fail with *\"Amount exceeds the policy maximum…\"*.\n- **`expense_min_description_length`** (`MinDescriptionLength`, int, default 0) — forces a minimum description length; 0 = off.\n- **`expense_require_receipt`** (`RequireReceipt`, bool) — blocks save until a receipt file is attached (see the *Receipts & Vendor-Settled* module).\n- **`expense_auto_approve_threshold`** (`AutoApproveThreshold`, decimal, optional) — at/under this amount a self-approval-eligible submitter's expense is auto-approved (drives the **SelfApproved** status).\n- **`expense_self_approval`** (`AllowSelfApproval`, bool) — whether self-approval is permitted at all.\n\nThese are *dynamic validators*: enforcement lives in `ExpensePolicyEnforcement.Enforce(amount, description, receiptFileId, settings)`, which runs on create/update and throws a `ValidationException` aggregating every violation at once. Because the rules are data-driven, the same dialog enforces different constraints per install with no code change. Settings are read by **Admin, Manager** (`GET /settings`) and written by **Admin only** (`PUT /settings`); a validator also guards that `AutoApproveThreshold` never exceeds `MaxAmount`.\n\n## Vendor-Bill Promotion & the One-Live-Bill Invariant\n\nApproving a **vendor-settled** expense (one with a named Vendor) promotes it into a real `VendorBill` via `IExpenseBillPromotionService.PromoteApprovedExpenseAsync` — an open AP item that ages and is paid through vendor payments. The mechanics that matter at the edges:\n\n- **One live bill per expense.** At most one non-void bill may exist per expense, backstopped by the DB unique index **`ux_vendor_bills_expense_live`** (`VendorBill.ExpenseId` where `Status != Void`). A re-approve is idempotent: it returns the existing bill rather than minting a second.\n- **Promotion can decline.** When Payables is off, the expense is cash-settled, there's no vendor, or the amount is non-positive, promotion returns null and the handler falls back to the legacy `IExpenseApPostingService` (Dr Expense / Cr Cash) posting.\n- **Legacy-key guard.** If the expense's payable was already booked under the older Expense-keyed origination (`HasPostedLegacyOriginationAsync`), the service won't also post a bill-keyed entry — that would double-book the AP credit.\n- **Demotion voids.** Leaving approved status (Reject / Request Revision / re-open) calls `DemoteExpenseBillAsync`, which sets the bill to **Void** and reverses its posting in the same transaction. A bill with vendor payments applied **blocks the transition** — void the payment(s) first, exactly like `VoidVendorBill`.\n\nThe whole status-change + posting runs in one DB transaction, so a posting failure leaves the expense status unchanged (no orphaned approval).\n\n## Classification Taxonomy & Recurrence Math\n\nRecurring templates (`RecurringExpense`) carry a **Classification** string (Subscription, Lease, Insurance, Utility, Maintenance Contract, License, Membership, Other) used for the colored chip and the *Highlight Classification* spotlight — it is a label, not an accounting dimension.\n\nThe **Upcoming** forecast (`GET /api/v1/expenses/upcoming?daysAhead=90`) is pure projection — it generates nothing. Starting from each active template's `NextOccurrenceDate`, it steps forward by the `RecurrenceFrequency` until it passes the horizon (`now + daysAhead`, default **90** days), emitting one projected row per step:\n\n- **Weekly** → +7 days\n- **Biweekly** → +14 days\n- **Monthly** → +1 month\n- **Quarterly** → +3 months\n- **Annually** → +1 year\n\nMonthly/Quarterly/Annually use calendar `AddMonths`/`AddYears` (date arithmetic, so month-end and leap years follow .NET's rules), while Weekly/Biweekly are fixed day counts. A template whose `NextOccurrenceDate` is already past the horizon contributes nothing; pausing a template drops all its future projections immediately.\n\n## Direct-URL-Only Sub-Routes\n\nThe Expenses feature ships two child routes that are reachable by URL but not always linked from the main page chrome:\n\n- **`/expenses/approval`** — the manager Approval Queue (lazy-loaded `ExpenseApprovalQueueComponent`).\n- **`/expenses/upcoming`** — the Recurring & Upcoming ledger (lazy-loaded `UpcomingExpensesComponent`).\n\nThe base `/expenses` route renders the main list. If a queue or forecast view seems missing, navigate to the explicit sub-route (or follow a deep link); they exist independent of any nav entry.\n\n## CAP-ACCT-EXPENSES & Role Gating\n\nThe entire controller (`/api/v1/expenses`) sits behind **`CAP-ACCT-EXPENSES`** — an install with that capability off returns 403 and the area disappears. On top of the capability, individual endpoints split by role:\n\n- **Create / Update / Delete / Read list / Upload receipt** — any authenticated user with the capability (you manage your own expenses).\n- **Update status (approve / reject / request revision)** — `Admin, Manager, OfficeManager` (the approver roles; tag `F-EXP-01`).\n- **Read settings** (`GET /settings`) — `Admin, Manager`.\n- **Write settings** (`PUT /settings`) — `Admin` only.\n\nSo a worker can submit and edit but never approve or change policy; a Manager can approve and read policy but not rewrite it; only an Admin tunes the `expense_*` knobs.","sections":[]}
"""
        });

        Log.Information("Seeded Expenses training modules");
    }
}
