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
  "body": "## Expenses Overview\n\nThe Expenses module lets you submit, track, and manage company expenses in a single searchable table. Managers can approve or reject pending expenses directly from the table.\n\n### Submission Flow\n\nTo submit an expense:\n1. Navigate to /expenses.\n2. Click the **Add Expense** button in the page header.\n3. Fill in Amount (required, min $0.01), Date (required), and Category (required — loaded from reference data configured in Admin).\n4. Optionally add a Description.\n5. Click **Submit**. The expense enters **Pending** status.\n\nOnce submitted, the expense appears in the main table for everyone to see.\n\n### Approval Workflow\n\nManagers and Admins can approve or reject pending expenses directly in the table. Each pending expense row shows two inline action buttons:\n- **Approve** (checkmark icon) — immediately marks the expense as Approved (green chip).\n- **Reject** (X icon) — immediately marks the expense as Rejected (red chip).\n\nThese action buttons only appear for expenses with Pending status.\n\n### Filtering and Search\n\nThe page header includes:\n- A **Search** input that filters by description, category, or submitter name.\n- A **Status** filter dropdown with options: All, Pending, Approved, Rejected, SelfApproved.\n- A **Total Amount** display showing the sum of currently visible expenses.\n\n### Expense Statuses\n\n- **Pending** (warning/yellow chip) — Awaiting manager approval.\n- **Approved** (success/green chip) — Reviewed and approved.\n- **Rejected** (error/red chip) — Reviewed and rejected.\n- **SelfApproved** (success/green chip) — Submitted by a manager with auto-approve privileges.\n\n### Draft Auto-Save\n\nThe expense dialog supports draft auto-save. If you accidentally close the dialog before saving, your form data is preserved in IndexedDB and restored when you reopen the dialog.",
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
        "description": "Search across all expenses by description, category, or submitter name. Results filter in real time as you type.",
        "side": "bottom"
      }
    },
    {
      "element": "[data-testid='status-filter']",
      "popover": {
        "title": "Status Filter",
        "description": "Filter expenses by status: All, Pending (awaiting approval), Approved, Rejected, or SelfApproved. Each status has a color-coded chip in the table.",
        "side": "bottom"
      }
    },
    {
      "element": "app-data-table",
      "popover": {
        "title": "Expenses Table",
        "description": "All submitted expenses appear here. Columns include Date (MM/dd/yyyy), Category (colored chip), Description, Job (if linked), Submitted By, Amount ($X.XX), and Status (color chip). Click column headers to sort. Click a row for details.",
        "side": "top"
      }
    },
    {
      "element": "[data-testid='expense-save-btn'], [data-testid='new-expense-btn'], .action-btn--primary",
      "popover": {
        "title": "Add Expense",
        "description": "Click here to submit a new expense. You'll fill in Amount (required), Date (required), Category (required from reference data), and an optional Description. The expense starts in Pending status.",
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
        {"label": "Amount (required)", "value": "Currency input, min $0.01, prefix '$'. The expense dollar amount. data-testid: expense-amount"},
        {"label": "Date (required)", "value": "Datepicker. The date the expense was incurred. Displayed as MM/dd/yyyy. data-testid: expense-date"},
        {"label": "Category (required)", "value": "Select dropdown. Options loaded from reference data (expense categories configured in Admin). data-testid: expense-category"},
        {"label": "Description (optional)", "value": "Textarea. Free-form description of the expense. data-testid: expense-description"},
        {"label": "Submit button", "value": "Disabled when form is invalid or save is in progress. Hover shows validation popover listing violations. data-testid: expense-save-btn"}
      ]
    },
    {
      "heading": "Expense Statuses",
      "items": [
        {"label": "Pending (warning/yellow chip)", "value": "Expense has been submitted and is awaiting manager approval. Default status for new expenses."},
        {"label": "Approved (success/green chip)", "value": "Expense has been reviewed and approved by a manager."},
        {"label": "Rejected (error/red chip)", "value": "Expense has been reviewed and rejected by a manager."},
        {"label": "SelfApproved (success/green chip)", "value": "Expense was submitted by a manager who auto-approved it. Same green chip as Approved."}
      ]
    },
    {
      "heading": "Table Columns",
      "items": [
        {"label": "Date", "value": "Sortable. Expense date formatted as MM/dd/yyyy."},
        {"label": "Category", "value": "Sortable. Displayed as a colored chip."},
        {"label": "Description", "value": "Sortable. Free-form text description."},
        {"label": "Job", "value": "Sortable. Linked job number or '—' if not linked to a job."},
        {"label": "Submitted By", "value": "Sortable. Name of the user who submitted the expense."},
        {"label": "Amount", "value": "Sortable. Formatted as $X.XX, right-aligned."},
        {"label": "Status", "value": "Sortable, filterable (enum). Color-coded chip: Pending (yellow), Approved (green), Rejected (red), SelfApproved (green)."},
        {"label": "Actions", "value": "Inline approve (checkmark) and reject (X) buttons. Only visible for Pending expenses."}
      ]
    },
    {
      "heading": "Page Header Controls",
      "items": [
        {"label": "Search", "value": "Text input. Filters expenses by description, category, or submitter name. Press Enter to apply."},
        {"label": "Status Filter", "value": "Select dropdown: All, Pending, Approved, Rejected, SelfApproved. data-testid: status-filter"},
        {"label": "Total Amount", "value": "Read-only display showing the sum of all currently visible (filtered) expenses."},
        {"label": "Add Expense button", "value": "Opens the Create Expense dialog. data-testid: new-expense-btn"}
      ]
    },
    {
      "heading": "Inline Approval Actions",
      "items": [
        {"label": "Approve (checkmark icon)", "value": "Green icon button (icon-btn--success). Immediately approves the expense. Only visible for Pending expenses. Stops click propagation from opening detail."},
        {"label": "Reject (X icon)", "value": "Red icon button (icon-btn--danger). Immediately rejects the expense. Only visible for Pending expenses."}
      ]
    },
    {
      "heading": "Validation Rules",
      "items": [
        {"label": "Amount", "value": "Required. Must be a number >= $0.01."},
        {"label": "Date", "value": "Required. Must be a valid date."},
        {"label": "Category", "value": "Required. Must select from reference data options (configured in Admin → Reference Data)."},
        {"label": "Description", "value": "Optional. No length restriction."},
        {"label": "Submit button popover", "value": "When form is invalid, hovering over Save shows a validation popover listing which fields need attention."},
        {"label": "Draft Auto-Save", "value": "Form data saved to IndexedDB every 2.5 seconds while editing. Recovered on next dialog open if unsaved."}
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
      "explanation": "The Create Expense dialog requires three fields: Amount (min $0.01), Date (datepicker), and Category (from reference data). Description is optional."
    },
    {
      "id": "ex2",
      "text": "What status does a newly submitted expense start with?",
      "options": [
        {"id": "a", "text": "Draft — it needs to be finalized before submission"},
        {"id": "b", "text": "Approved — expenses are auto-approved by default"},
        {"id": "c", "text": "Pending — it awaits manager approval", "isCorrect": true},
        {"id": "d", "text": "SelfApproved — the submitter's own expenses are auto-approved"}
      ],
      "explanation": "All newly submitted expenses start in Pending status (yellow chip) and await manager review."
    },
    {
      "id": "ex3",
      "text": "A manager wants to approve a pending expense. How do they do it?",
      "options": [
        {"id": "a", "text": "Click the checkmark icon on the expense row to approve it immediately", "isCorrect": true},
        {"id": "b", "text": "Select the expense and click a bulk Approve All button"},
        {"id": "c", "text": "Open the expense detail and change the status dropdown to Approved"},
        {"id": "d", "text": "Right-click the row and select Approve from the context menu"}
      ],
      "explanation": "Each pending expense row has two inline action icons: a checkmark (approve) and an X (reject). Clicking the checkmark immediately approves that expense."
    },
    {
      "id": "ex4",
      "text": "Where do expense categories come from?",
      "options": [
        {"id": "a", "text": "They are hardcoded in the application and cannot be changed"},
        {"id": "b", "text": "Each user defines their own categories in their profile settings"},
        {"id": "c", "text": "They are loaded from reference data configured by an Admin", "isCorrect": true},
        {"id": "d", "text": "They are imported from QuickBooks and cannot be modified locally"}
      ],
      "explanation": "Expense categories are managed via reference data in the Admin section. An administrator can add, edit, or remove categories that appear in the Category dropdown."
    },
    {
      "id": "ex5",
      "text": "What does the Total Amount display in the page header show?",
      "options": [
        {"id": "a", "text": "The total of all expenses ever submitted in the system"},
        {"id": "b", "text": "The sum of currently visible (filtered) expenses", "isCorrect": true},
        {"id": "c", "text": "The total of only Approved expenses"},
        {"id": "d", "text": "The monthly budget remaining for the current user"}
      ],
      "explanation": "The Total Amount display in the page header dynamically shows the sum of all expenses currently visible after applying search and status filters."
    },
    {
      "id": "ex6",
      "text": "You accidentally close the expense dialog before saving. What happens to your form data?",
      "options": [
        {"id": "a", "text": "It is lost — you must re-enter everything from scratch"},
        {"id": "b", "text": "A confirmation dialog asks if you want to save a draft before closing"},
        {"id": "c", "text": "Draft auto-save preserves the data in IndexedDB and restores it when you reopen the dialog", "isCorrect": true},
        {"id": "d", "text": "The data is saved to the server as a Draft-status expense"}
      ],
      "explanation": "The expense dialog uses draft auto-save (debounced every 2.5 seconds). Form data is stored in IndexedDB and automatically recovered when you reopen the dialog."
    },
    {
      "id": "ex7",
      "text": "How can you filter the expenses table to see only rejected expenses?",
      "options": [
        {"id": "a", "text": "Type 'rejected' in the search input"},
        {"id": "b", "text": "Use the Status filter dropdown and select 'Rejected'", "isCorrect": true},
        {"id": "c", "text": "Click the Rejected column header to sort by rejections"},
        {"id": "d", "text": "Navigate to a separate Rejected Expenses page"}
      ],
      "explanation": "The Status filter dropdown in the page header lets you select a specific status: All, Pending, Approved, Rejected, or SelfApproved. Selecting 'Rejected' shows only rejected expenses."
    },
    {
      "id": "ex8",
      "text": "In the Expense Approval Queue, a manager opens an expense and wants to reject it (or send it back for changes). What does the review dialog require?",
      "options": [
        {"id": "a", "text": "Nothing extra — Reject is a single click with no note"},
        {"id": "b", "text": "A review note of at least 10 characters before Reject or Request Revision is allowed", "isCorrect": true},
        {"id": "c", "text": "A receipt must be attached before the expense can be rejected"},
        {"id": "d", "text": "The expense must be deleted and the submitter re-creates it from scratch"}
      ],
      "explanation": "Opening an expense in the Approval Queue shows a review dialog with three outcomes: Approve, Request Revision, and Reject. Both Reject and Request Revision require a review note of at least 10 characters; only Approve can proceed without one. Rejected shows a red chip, while Request Revision sets the expense to Needs Revision so the submitter can revise and resubmit it."
    },
    {
      "id": "ex9",
      "text": "The search input in the page header filters by which fields?",
      "options": [
        {"id": "a", "text": "Only the Description field"},
        {"id": "b", "text": "Description and Amount"},
        {"id": "c", "text": "Description, Category, and Submitted By name", "isCorrect": true},
        {"id": "d", "text": "All visible table columns including Date and Status"}
      ],
      "explanation": "The search input filters across description, category, and submitter name. It does not search by date, amount, or status — use the Status dropdown for status filtering."
    },
    {
      "id": "ex10",
      "text": "What does the 'SelfApproved' status mean on an expense?",
      "options": [
        {"id": "a", "text": "The expense was approved by the system automatically based on rules"},
        {"id": "b", "text": "The expense was submitted by a manager who has auto-approve privileges", "isCorrect": true},
        {"id": "c", "text": "The submitter clicked an Approve button on their own expense"},
        {"id": "d", "text": "The expense was under a threshold amount and was auto-approved"}
      ],
      "explanation": "SelfApproved indicates the expense was submitted by a manager with auto-approve capability. It displays with a green chip, same as Approved."
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
      "explanation": "The Amount field has a minimum validator of 0.01. You cannot submit an expense for $0.00 or a negative amount."
    },
    {
      "id": "ex12",
      "text": "Which of the following is NOT a column in the Expenses data table?",
      "options": [
        {"id": "a", "text": "Category"},
        {"id": "b", "text": "Submitted By"},
        {"id": "c", "text": "Vendor", "isCorrect": true},
        {"id": "d", "text": "Job"}
      ],
      "explanation": "The Expenses table columns are: Date, Category, Description, Job, Submitted By, Amount, Status, and Actions. There is no Vendor column."
    },
    {
      "id": "ex13",
      "text": "When are the inline approve/reject action buttons visible on an expense row?",
      "options": [
        {"id": "a", "text": "Always — on every expense regardless of status"},
        {"id": "b", "text": "Only for expenses with Pending status", "isCorrect": true},
        {"id": "c", "text": "Only for expenses submitted by other users"},
        {"id": "d", "text": "Only for expenses over $100"}
      ],
      "explanation": "The approve (checkmark) and reject (X) inline action buttons only appear for expenses that are in Pending status. Once approved or rejected, the buttons are hidden."
    },
    {
      "id": "ex14",
      "text": "What color chip does each expense status display?",
      "options": [
        {"id": "a", "text": "Pending: blue, Approved: green, Rejected: red, SelfApproved: gray"},
        {"id": "b", "text": "Pending: yellow, Approved: green, Rejected: red, SelfApproved: green", "isCorrect": true},
        {"id": "c", "text": "Pending: gray, Approved: blue, Rejected: yellow, SelfApproved: green"},
        {"id": "d", "text": "All statuses use the same neutral gray chip"}
      ],
      "explanation": "Pending uses a warning/yellow chip, Approved and SelfApproved both use success/green chips, and Rejected uses an error/red chip."
    },
    {
      "id": "ex15",
      "text": "What happens when you hover over a disabled Submit button in the Create Expense dialog?",
      "options": [
        {"id": "a", "text": "Nothing happens — the button is just grayed out"},
        {"id": "b", "text": "A tooltip says 'Please fill in all fields'"},
        {"id": "c", "text": "A validation popover lists which specific fields need attention", "isCorrect": true},
        {"id": "d", "text": "The invalid fields flash red to draw your attention"}
      ],
      "explanation": "When the form is invalid, hovering over the Save/Submit button displays a validation popover that lists each field violation — for example, 'Amount is required' or 'Category is required'."
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
  "body": "## Receipts and Vendor-Settled Expenses\n\nThe expense dialog has two fields that change *how* an expense is handled downstream: the **receipt attachment** and the **Vendor** selector. This module explains both.\n\n### Attaching a Receipt\n\nIn the Create/Resubmit Expense dialog, the **Receipt** row lets you attach proof of purchase:\n\n1. Click **Upload Receipt**.\n2. Pick an image (`image/*`) or a PDF. The file uploads immediately and the row switches to show the file name with a paperclip icon.\n3. To swap it, click the red **X** next to the file name to remove it, then upload a different file.\n\nA receipt is stored as a file attachment on the expense; reviewers can open it from the approval queue when deciding.\n\n### When a Receipt Is Required\n\nYour company can turn on a **require-receipt policy** in expense settings. When it's on:\n\n- The Receipt label shows a red asterisk (**\\***), the same required marker used on other mandatory fields.\n- The **Submit** button stays disabled until a receipt is attached — even if every other field is valid.\n- Hovering the disabled Submit button shows a hint explaining that a receipt is required.\n\nSo if Submit won't enable and all your fields look filled in, check whether a receipt is still missing. The block is policy-driven: when the policy is off, the receipt is optional and Submit doesn't wait on it.\n\n### Out-of-Pocket vs Vendor-Settled\n\nEvery expense is one of two kinds, decided by the **Vendor** field:\n\n- **Out-of-pocket (cash)** — leave Vendor set to *No vendor*. This is the default. The expense represents money an employee spent personally and expects to be reimbursed for. It stays entirely inside the Expenses module.\n- **Vendor-settled** — pick a vendor from the **Vendor** dropdown. This tells the system the money is owed to that vendor (the company hasn't paid yet). A hint appears under the field explaining the consequence.\n\n### What a Vendor-Settled Expense Does on Approval\n\nNaming a vendor routes the expense to **Accounts Payable**. When the expense is **approved**, it is *promoted into a vendor bill* that is paid through Payables — the normal AP pipeline (vendor bills → vendor payments) takes over from there.\n\nOnce promotion happens, the expense row in the Expenses table shows a small **info chip with the vendor-bill number** next to its status. That chip is a live link: clicking it opens the linked vendor bill. This is how you trace an approved vendor-settled expense to the bill it became.\n\nKey points:\n- The **Vendor** field is part of the create/resubmit dialog — it is *not* a column in the main expenses table, so don't look for a Vendor column there. The linkage surfaces as the bill-number chip beside the status instead.\n- Promotion happens at **approval**, not at submission. A pending vendor-settled expense has no bill yet.\n- Out-of-pocket expenses never create a vendor bill — they're reimbursements, not payables.\n\n### Quick Decision Guide\n\n| You spent money… | Vendor field | What happens on approval |\n|---|---|---|\n| Personally, expect reimbursement | *No vendor* | Stays an expense (reimbursement) |\n| On the company's behalf, vendor not yet paid | Select the vendor | Becomes a vendor bill in Payables |",
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
        "description": "This dedicated queue at /expenses/approval lists every expense that is still Pending — across all submitters — so managers can review them in one place instead of hunting through the main table. It is separate from the inline approve/reject buttons on the main /expenses list.",
        "side": "bottom"
      }
    },
    {
      "element": "app-input",
      "popover": {
        "title": "Search Pending Expenses",
        "description": "Narrow the queue by typing a submitter, category, or description and pressing Enter. Only Pending expenses are ever shown here.",
        "side": "bottom"
      }
    },
    {
      "element": ".approval-queue__summary",
      "popover": {
        "title": "Pending Count & Total",
        "description": "The summary shows how many expenses are awaiting your decision and their combined dollar total — a quick read on your outstanding approval workload.",
        "side": "left"
      }
    },
    {
      "element": "app-data-table",
      "popover": {
        "title": "Open an Expense to Review",
        "description": "Click any row (or its check/X buttons) to open the review dialog. The dialog shows the submitter, date, category, amount, description, and linked job, plus a Review Note field and three decision buttons: Approve, Request Revision, and Reject.",
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
  "body": "## Reviewing an Expense\n\nClicking a row in the Expense Approval Queue (`/expenses/approval`) opens the **review dialog**. It shows the read-only details of the submission — submitter, date, category, amount, description, and linked job (when present) — above a **Review Note** field and three decision buttons.\n\n### The Three Outcomes\n\n- **Approve** — accepts the expense. It moves to Approved (green chip). If the expense named a vendor, approval also promotes it into a vendor bill (see *Expense Receipts & Vendor-Settled Expenses*). Approve does **not** require a note; you can add one for context, but it's optional.\n- **Request Revision** — sends the expense back to the submitter without rejecting it outright. The status becomes **Needs Revision**, and your note becomes the reviewer feedback the submitter sees. Use this when the expense is fixable — wrong category, missing receipt, amount needs explaining — and you'd rather the submitter correct and resubmit than start over.\n- **Reject** — declines the expense outright (red chip). Use this when the expense should not be reimbursed at all.\n\n### The Review-Note Rule\n\nThe **Review Note** field governs the two negative outcomes:\n\n- **Reject and Request Revision both require a note of at least 10 characters.** Until the note reaches 10 characters, both buttons stay disabled, and a hint under the field counts your progress toward the minimum.\n- **Approve has no note requirement.** It's enabled regardless of the note field.\n\nThis is intentional: telling a submitter *why* their expense was bounced is mandatory, so they can fix it (Request Revision) or understand the decision (Reject). A silent rejection isn't possible.\n\n### After You Decide\n\nThe dialog closes, the queue reloads (the expense you just handled drops off the Pending list), and a confirmation snackbar reports the outcome. A Needs-Revision expense reappears for the submitter to edit and resubmit; an Approved or Rejected one is final from the queue's perspective.\n\n### Who Sees This Queue\n\nThe Approval Queue is a manager-facing surface. Regular submitters use the main `/expenses` list to create and track their own expenses; the queue is where reviewers clear the pending backlog."
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
        "description": "When a reviewer sends an expense back, it gets the Needs Revision status (a yellow chip). Use this Status filter and pick 'Needs Revision' to see just the ones waiting on you to fix.",
        "side": "bottom"
      }
    },
    {
      "element": "app-data-table",
      "popover": {
        "title": "The Resubmit Action",
        "description": "A Needs-Revision row shows a pencil (edit) action at the right end of the row. Clicking it reopens the expense in a dialog titled 'Resubmit Expense' — pre-filled with everything you originally entered.",
        "side": "top"
      }
    },
    {
      "element": "[data-testid='new-expense-btn']",
      "popover": {
        "title": "Or Create a Fresh Expense",
        "description": "This same dialog is used to create new expenses. The difference when resubmitting is that the dialog shows the reviewer's feedback note at the top so you know exactly what to change.",
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
  "body": "## Recurring & Upcoming Expenses\n\nThe expense ledger at `/expenses/upcoming` handles predictable, repeating costs — subscriptions, leases, insurance, utilities — so you don't have to remember to file them by hand each cycle. It has two tabs: **Upcoming** and **Recurring**.\n\n### Recurring Tab — the Templates\n\nA **recurring expense** is a template that generates real expenses automatically on a schedule. Click **New Recurring** to create one:\n\n- **Amount** (required) and **Frequency** (required) — Weekly, Bi-weekly, Monthly, Quarterly, or Annually.\n- **Category** (required) and **Classification** (required). Classification is the recurring-cost taxonomy (Subscription, Lease, Insurance, Utility, Maintenance Contract, License, Membership, Other) and drives the colored chip.\n- **Description** (required) and **Vendor** (optional).\n- **Start Date** (required) and an optional **End Date** — leave End Date blank for an open-ended commitment.\n- **Auto-approve** toggle — when on, the expenses this template generates skip the pending queue.\n\nThe Recurring tab lists every template with its **Next Due** date and an **Active** chip. Two row actions let you manage a template:\n\n- The **pause / play** button toggles the template Active or Paused. Pausing stops it from generating new expenses without deleting its history.\n- The **delete** (trash) button removes the template after a confirmation. Note recurring templates are **delete-only / pause-only** — there is no in-place edit; to change terms, delete and recreate, or pause the old one.\n\n### How Generation Works\n\nActive templates auto-generate their expense on each occurrence — you don't click anything. A template with Auto-approve on produces already-approved expenses; without it, each generated expense lands in the normal Pending queue for review like any hand-entered one.\n\n### Upcoming Tab — the 90-Day Forecast\n\nThe **Upcoming** tab is a forward-looking view: it projects every occurrence your active recurring templates will generate over the next **90 days**. It does not create anything — it's a forecast so you can see what's coming.\n\n- The **90-Day Total** sums every projected occurrence in the window.\n- A **monthly breakdown** strip shows per-month totals and item counts, so you can see how the load distributes.\n- The **Highlight Classification** selector lets you spotlight one classification (e.g., all Subscriptions) — matching rows are highlighted in the table rather than filtered away, so you keep the full picture while emphasizing one cost type.\n- Each row shows the projected Due Date, Classification, Category, Description, Vendor, Amount, and Frequency.\n\nUse Upcoming to anticipate cash needs; use Recurring to control the templates that drive it. Pausing a template on the Recurring tab immediately removes its future occurrences from the Upcoming forecast."
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
        {"label": "Amount (required)", "value": "Currency, min $0.01. The amount each generated occurrence will be for. data-testid: recurring-amount"},
        {"label": "Frequency (required)", "value": "Weekly, Bi-weekly, Monthly, Quarterly, or Annually. Drives how often the template generates. data-testid: recurring-frequency"},
        {"label": "Category (required)", "value": "Expense category for the generated expenses. data-testid: recurring-category"},
        {"label": "Classification (required)", "value": "Recurring-cost type: Subscription, Lease, Insurance, Utility, Maintenance Contract, License, Membership, Other. data-testid: recurring-classification"},
        {"label": "Description (required)", "value": "What the recurring cost is for. data-testid: recurring-description"},
        {"label": "Vendor (optional)", "value": "Free-text vendor name. data-testid: recurring-vendor"},
        {"label": "Start Date (required)", "value": "First occurrence date. data-testid: recurring-start"},
        {"label": "End Date (optional)", "value": "Leave blank for open-ended. data-testid: recurring-end"},
        {"label": "Auto-approve", "value": "Toggle. When on, generated expenses skip the Pending queue. data-testid: recurring-auto-approve"}
      ]
    },
    {
      "heading": "Recurring Tab Columns & Actions",
      "items": [
        {"label": "Next Due", "value": "The next date this template will generate an expense."},
        {"label": "Active chip", "value": "Green 'Active' or muted 'Paused' — whether the template is currently generating."},
        {"label": "Pause / Play action", "value": "Toggles the template between Active and Paused. Pausing keeps history but stops new generation."},
        {"label": "Delete action", "value": "Removes the template after a confirmation dialog. Templates are delete-only — no in-place edit."}
      ]
    },
    {
      "heading": "Upcoming (Forecast) Tab",
      "items": [
        {"label": "Window", "value": "Projects the next 90 days of occurrences from active templates. Read-only forecast — generates nothing."},
        {"label": "90-Day Total", "value": "Sum of every projected occurrence in the window."},
        {"label": "Monthly breakdown", "value": "Per-month total + item count chips."},
        {"label": "Highlight Classification", "value": "Spotlights one classification by highlighting matching rows (does not hide the rest)."},
        {"label": "Pausing effect", "value": "Pausing a template on the Recurring tab immediately drops its future occurrences from this forecast."}
      ]
    }
  ]
}
"""
        });

        Log.Information("Seeded Expenses training modules");
    }
}
