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

        Log.Information("Seeded Expenses training modules");
    }
}
