using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data.TrainingContent;

public class InventoryAdvancedTraining : TrainingContentBase
{
    public InventoryAdvancedTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override IReadOnlyList<string> Capabilities =>
    [
        "CAP-INV-CYCLECOUNT", "CAP-INV-RESERVE", "CAP-INV-ADJUST", "CAP-MD-GS1",
    ];

    public override async Task SeedAsync()
    {
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Advanced Inventory: Counts, Holds and Barcodes",
            Slug = "inventory-adv-overview",
            Summary = "What sits on top of basic stock tracking: cycle counts that fix the book, reservations that hold stock for a job, the Count override for opening stock, and GS1 GTINs for parts sold at retail",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/inventory/cycleCounts","/inventory/reservations","/inventory/home/kiosk","/admin/gs1"]""",
            Tags = """["inventory","cycle-count","reservation","adjustment","gs1","gtin"]""",
            ContentJson = """{"body":"## Advanced Inventory\n\nThe basic Inventory training covers the day-to-day: stock levels, locations, receiving, transfers and units of measure. This series covers the four tools you reach for once the basics are running and the numbers start to drift or the orders start to compete.\n\n- **Cycle counts** — count one location at a time, compare against the book, and approve the difference so on-hand stays honest without shutting the shop down.\n- **Reservations** — put a hold on stock so it cannot be used for a different job while you wait to pick it.\n- **Count (on-hand override)** — the quick way to enter opening stock or fix a number without a purchase order. Managers and admins only.\n- **GS1 GTINs** — give a part a globally unique retail barcode when it ships to a store or an online marketplace.\n\nEach one is its own capability, so you may see some of these and not others. Cycle counts and the Count override are on by default. Reservations and GS1 are off until an admin turns them on.\n\n### Where they live\n\n- Cycle counts and reservations are tabs on the detailed inventory page (**Inventory > Manage**, then the **Cycle Counts** or **Reservations** tab).\n- The Count override is the **Count** button on the Inventory home Quick tab.\n- GS1 settings are under **Admin > GS1 / GTIN**; assigning a GTIN happens on the part itself.\n\n### How they fit together\n\nA reservation lowers *available* (on hand minus reserved) but not *on hand*. A cycle count or a Count override changes *on hand*. Because of that, Forge will not let a count or an override drop a bin below what is reserved — release the reservation first, then fix the number. Every count approval and every override writes a movement you can see on the **Movements** tab, so nothing changes without a trail.\n\nRead the other modules in this series in order: cycle counts, reservations, the Count override, then GS1.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Cycle Counts",
            Slug = "inventory-adv-cycle-counts",
            Summary = "Count one location at a time, enter what you actually found, and approve the variance so the book matches the shelf",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/inventory/cycleCounts"]""",
            Tags = """["inventory","cycle-count","variance"]""",
            ContentJson = """{"body":"## Cycle Counts\n\nA cycle count is a small, regular check of one location instead of a once-a-year count of everything. You pick a bin or shelf, count what is really there, and Forge shows the difference against what it expected. Approve it and on-hand is corrected on the spot; reject it and nothing changes.\n\nOpen **Inventory > Manage** and click the **Cycle Counts** tab. The table lists every count with its location, who counted, when, its status, and the total variance.\n\n### Starting a count\n\n1. Click **New Count** in the page header.\n2. Choose the **Location** to count. Only bin-type locations are offered.\n3. Add a note if it helps (why you are counting, who asked).\n4. Click **Create**.\n\nForge snapshots every item in that location with its **Expected** quantity — what the book says right now — and opens the count detail dialog. The count starts in **Pending** status.\n\n### Entering what you found\n\nIn the detail dialog each line shows the item, the expected quantity, an **Actual** box, and the variance. Type the counted quantity into **Actual** for each line. The variance updates as you go: a plus number means you found more than the book, a minus number means less.\n\nIf you get interrupted, just close the dialog. The count stays Pending and you can click the row in the table to open it again later.\n\n### Approving or rejecting\n\nWhen every line is counted, two buttons appear at the bottom of the dialog:\n\n- **Approve & Adjust Stock** sets each bin to the actual quantity you entered and writes a movement for every line that changed, with the reason *Cycle Count*. A line counted at zero closes that bin content. The count moves to **Approved** and can no longer be edited.\n- **Reject** throws the count away without touching stock. Use it when the count was done wrong or the location was still in motion (a receipt landing mid-count, for example).\n\nOnly a Manager or Admin can approve or reject. Anyone with inventory access can create a count and enter actuals, so a clerk can do the counting and a supervisor can sign off.\n\n### The reservation rule\n\nIf a bin has stock reserved for a job, Forge will not approve a count that drops that bin below the reserved amount. The approval fails with a message telling you how many units are reserved. Go to the **Reservations** tab, release the hold, and approve again — or recount, because reserved stock that is not on the shelf usually means someone already picked it.\n\n### Good habits\n\n- Count when the location is quiet. A count taken while receiving is landing will always show a variance.\n- Count a few locations every week rather than everything once a year. Small, frequent counts catch problems while they are still small.\n- Write the cause in the notes when you find a variance (mislabeled bin, scrap not recorded, wrong unit). The note stays with the count and helps the next person.\n- Review the **Movements** tab after approving. Every corrected line shows there as a Cycle Count movement.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Cycle Counts Tab — Guided Tour",
            Slug = "inventory-adv-cycle-count-walkthrough",
            Summary = "A short tour of the Cycle Counts tab: where counts are listed, how to start one, and what the status and variance columns mean",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/inventory/cycleCounts"]""",
            Tags = """["inventory","cycle-count","walkthrough"]""",
            ContentJson = """{"appRoute":"/inventory/cycleCounts","startButtonLabel":"Tour Cycle Counts","steps":[{"element":".tab-bar","popover":{"title":"Inventory Tabs","description":"The detailed inventory page is organized into tabs. Cycle Counts is the sixth one.","side":"bottom"}},{"element":".tab-bar .tab:nth-child(6)","popover":{"title":"Cycle Counts","description":"Every count you have started, one row each, with its location, counter, date, status and total variance. Click a row to open it.","side":"bottom"}},{"element":".panel-header","popover":{"title":"Count Total","description":"How many counts are on file. Pending counts are waiting for actuals or for a manager to approve.","side":"bottom"}},{"element":"app-data-table","popover":{"title":"The Count List","description":"Status is Pending until it is approved or rejected. Variance is the total difference between expected and actual across every line. Use the New Count button in the page header to start a fresh count of one location.","side":"top"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Reservations",
            Slug = "inventory-adv-reservations",
            Summary = "Hold stock for a job so nobody else uses it, see what is on hold, and release it when the job no longer needs it",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/inventory/reservations"]""",
            Tags = """["inventory","reservation","available"]""",
            ContentJson = """{"body":"## Reservations\n\nOn hand tells you what is on the shelf. It does not tell you whether it is spoken for. A reservation puts a hold on a quantity in a specific bin so it counts against **available** — on hand minus reserved — and cannot be used up by a different job or a quick stock-out from the kiosk.\n\nThis is an optional capability. If you do not see the **Reservations** tab on **Inventory > Manage**, ask an admin to turn on *Inventory reservation*.\n\n### Reserving stock\n\n1. Open the **Reservations** tab and click **Reserve Stock** in the page header.\n2. Enter the **Part ID** and the **Bin Content ID** of the exact bin you are holding stock in. You can read both from the bin detail on the Stock Levels tab.\n3. Enter the **Job ID** if the hold is for a job. Leave it blank for a general hold (a customer sample, a recall check).\n4. Enter the **Quantity** and any notes, then click **Reserve**.\n\nForge checks the bin first. You can only reserve what is still available in that bin. If the bin has 40 on hand and 30 already reserved, a request for 15 is refused with a message showing the numbers.\n\n### Reading the list\n\nThe table shows one row per active reservation: part, quantity, the job number and title it is held for, notes, and when it was created. There is no expiry — a reservation stays until someone releases it.\n\n### Releasing\n\nClick the **lock-open** button at the end of the row. The quantity goes back to available immediately. Release a reservation when:\n\n- the job was cancelled or the order changed;\n- the stock was picked and issued to the job through the normal job flow, so the hold is no longer needed;\n- you need to correct on hand with a count and the bin would drop below the reserved amount.\n\n### What a reservation protects against\n\n- **Use** from the kiosk and other manual stock-outs cannot take a bin below its reserved quantity.\n- Cycle counts and the Count override cannot set a bin below its reserved quantity.\n- Replenishment math looks at available, not on hand, so reserved stock does not hide a shortage.\n\nA reservation does not move stock. The units stay in their bin; only the available number changes.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Reservations Tab — Guided Tour",
            Slug = "inventory-adv-reservations-walkthrough",
            Summary = "A short tour of the Reservations tab: the list of holds, what each column means, and how to release one",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 5,
            AppRoutes = """["/inventory/reservations"]""",
            Tags = """["inventory","reservation","walkthrough"]""",
            ContentJson = """{"appRoute":"/inventory/reservations","startButtonLabel":"Tour Reservations","steps":[{"element":".tab-bar","popover":{"title":"Inventory Tabs","description":"Reservations is the seventh tab on the detailed inventory page. It only appears when the reservation capability is on.","side":"bottom"}},{"element":".tab-bar .tab:nth-child(7)","popover":{"title":"Reservations","description":"Every active hold on stock. A hold lowers available without moving anything.","side":"bottom"}},{"element":".panel-header","popover":{"title":"Active Holds","description":"How many reservations are open right now. Use the Reserve Stock button in the page header to add one.","side":"bottom"}},{"element":"app-data-table","popover":{"title":"The Reservation List","description":"Part, quantity, the job it is held for, notes and the date. The lock-open button at the end of each row releases the hold and returns the quantity to available.","side":"top"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Count: Setting On-Hand Directly",
            Slug = "inventory-adv-count-override",
            Summary = "The Count button on the Inventory home Quick tab sets a part's on-hand quantity without a purchase order — for opening stock, found stock and quick corrections. Managers and admins only",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 6,
            AppRoutes = """["/inventory/home/kiosk"]""",
            Tags = """["inventory","adjustment","opening-stock","count"]""",
            ContentJson = """{"body":"## Count: Setting On-Hand Directly\n\nMost stock arrives through receiving and leaves through jobs and shipments. Sometimes there is no paperwork: you are loading opening stock into a brand-new Forge install, you found a box that was never entered, or the number on the screen is simply wrong. The **Count** button on the Inventory home **Quick** tab handles those cases by setting on hand to the number you type.\n\nThis is different from a cycle count. A cycle count is a formal, reviewable process over a whole location. Count is a one-part, one-bin fix. It is also different from the **Adjust** dialog on the Stock Ops tab, which changes an existing bin by an amount; Count sets the total and will create the bin if the part has never been stocked there.\n\n### Who can use it\n\nCount is restricted to **Managers and Admins**, because it bypasses the purchasing trail. Other users see Receive and Use but not Count. If you need a correction and are not a manager, start a cycle count instead and have a manager approve it.\n\n### Using it\n\n1. Go to **Inventory** (the home page) and stay on the **Quick** tab.\n2. Click **Count**.\n3. Pick the **Part**.\n4. Enter the **Quantity** — the total that should be on hand in that bin after you save, not the difference.\n5. Choose the **Location** if your shop tracks more than one. With a single location Forge uses the default.\n6. Enter a **Reason**. This is required; if you leave it blank Forge records *Physical count*.\n7. Click **Save count**.\n\nForge sets the bin to that quantity, creating the bin if it did not exist, and writes an **Adjustment** movement for the difference with your reason and notes attached. Setting a bin to zero closes it.\n\n### Rules to know\n\n- You cannot set a bin below its reserved quantity. Release the reservation first.\n- Count never touches the general ledger. It is an operational correction only; if your accountant needs a matching entry, that is handled separately.\n- Every Count is audited with who, when and why. Pick reasons you would be comfortable explaining later: *Opening stock*, *Found in overflow rack*, *Recount after mislabel*.\n\n### When to use which\n\n- **Receive** — stock came in without a PO. Adds to the bin.\n- **Use** — stock went out without a job or shipment. Subtracts from the bin.\n- **Count** — the bin should be this number, full stop. Sets the bin.\n- **Cycle count** — check a whole location with review and sign-off.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "GS1 GTINs for Retail Barcodes",
            Slug = "inventory-adv-gs1",
            Summary = "When a part needs a globally unique barcode for a store or marketplace: enter your GS1 company prefix once, then assign a GTIN on the part",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 7,
            AppRoutes = """["/admin/gs1","/parts"]""",
            Tags = """["gs1","gtin","barcode","retail"]""",
            ContentJson = """{"body":"## GS1 GTINs for Retail Barcodes\n\nEvery part in Forge already has a free internal barcode. It is unique inside your shop and works everywhere in Forge: labels, scanners, kiosk lookups. That is all most shops ever need.\n\nA **GTIN** is different. It is the number under the barcode on retail packaging, and it must be unique in the whole world. Retailers and online marketplaces require one before they will list your product. GTINs come from **GS1**, the organization that licenses company prefixes. You pay GS1 for a prefix; Forge then builds GTINs under it for you.\n\nThis is an optional capability. Until an admin turns on *GS1 GTIN barcodes*, none of this appears and every part keeps its internal barcode.\n\n### Step 1: enter your company prefix\n\nGo to **Admin > GS1 / GTIN**. Type the **GS1 company prefix** exactly as GS1 licensed it to you — 6 to 11 digits — and click **Save**. Only a Manager or Admin can change it.\n\nOnce saved, the page shows **Remaining GTIN capacity**: how many more GTINs Forge can generate under that prefix. A short prefix leaves room for many items; a long prefix leaves room for only a few. That is set by GS1 when you buy the prefix, not by Forge.\n\nClearing the prefix and saving takes the shop back to internal-only barcodes.\n\n### Step 2: assign a GTIN on the part\n\nOpen a part. In the identity area, below the internal barcode, there is a **GTIN** section.\n\n- If the part has no GTIN it shows a hint and an **Assign GTIN** button.\n- Click it and choose one of two paths:\n  - **Auto-allocate** generates the next unused GTIN under your prefix. Use this for your own products.\n  - **Enter a GTIN you purchased separately** — paste in a GTIN you already own (8, 12, 13 or 14 digits). Use this for a product that already had a GTIN before Forge, or one bought as a single GTIN from GS1.\n\nOnce assigned, the section shows a green **GS1** chip with the number. A **Remove** button clears it and returns the part to its internal barcode; you can assign a new one later.\n\n### Which parts should get one\n\nOnly finished goods that ship to retail, to a marketplace, or into a customer's supply chain that requires GS1. Do not assign GTINs to raw material, hardware or internal subassemblies — they use capacity you paid for and gain nothing.\n\n### Common questions\n\n- **We do not sell retail.** Leave the capability off. Nothing changes.\n- **Auto-allocate is greyed out.** Check that the company prefix is set under Admin, and that remaining capacity is not zero.\n- **The manual GTIN was refused.** Forge checks the check digit. Re-type it from the GS1 certificate; a single wrong digit fails.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "GS1 Settings — Guided Tour",
            Slug = "inventory-adv-gs1-walkthrough",
            Summary = "A short tour of the Admin GS1 page: the company prefix, the hint about clearing it, and remaining GTIN capacity",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 1,
            IsPublished = true,
            SortOrder = 8,
            AppRoutes = """["/admin/gs1"]""",
            Tags = """["gs1","gtin","walkthrough","admin"]""",
            ContentJson = """{"appRoute":"/admin/gs1","startButtonLabel":"Tour GS1 Settings","steps":[{"element":".gs1__intro","popover":{"title":"What This Page Is For","description":"GTINs are the globally unique barcodes retailers and marketplaces require. Everything in Forge keeps working on the free internal barcode until you set a prefix here.","side":"bottom"}},{"element":".gs1__field","popover":{"title":"Company Prefix","description":"Type the 6 to 11 digit prefix exactly as GS1 licensed it to you, then click Save in the page actions. Only Managers and Admins can change it.","side":"bottom"}},{"element":".gs1__hint","popover":{"title":"Clearing the Prefix","description":"Saving an empty prefix reverts every part to its internal barcode. Assigned GTINs are not deleted from GS1, but Forge stops using them.","side":"top"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Advanced Inventory Field Reference",
            Slug = "inventory-adv-field-reference",
            Summary = "Every field and button on the Cycle Counts tab, the Reservations tab, the Count dialog, the GS1 admin page and the part GTIN section",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 9,
            AppRoutes = """["/inventory/cycleCounts","/inventory/reservations","/inventory/home/kiosk","/admin/gs1"]""",
            Tags = """["inventory","reference","fields","gs1"]""",
            ContentJson = """{"title":"Advanced Inventory Field Reference","groups":[{"heading":"Cycle Counts tab","items":[{"label":"New Count","value":"Starts a count of one location. Snapshots every item there with its expected quantity."},{"label":"Location","value":"The bin to count. Only bin-type locations are offered."},{"label":"Status","value":"Pending until a manager approves or rejects. Approved counts cannot be edited."},{"label":"Counted By / Date","value":"Who created the count and when."},{"label":"Variance (list)","value":"Total difference between expected and actual across all lines."}]},{"heading":"Cycle count detail","items":[{"label":"Expected","value":"What the book said when the count was created."},{"label":"Actual","value":"What you physically counted. Editable only while Pending."},{"label":"Variance (line)","value":"Actual minus expected. Plus means found more, minus means found less."},{"label":"Approve & Adjust Stock","value":"Sets each bin to the actual and writes a Cycle Count movement per changed line. Managers and Admins only."},{"label":"Reject","value":"Discards the count without touching stock."}]},{"heading":"Reservations tab","items":[{"label":"Reserve Stock","value":"Opens the reservation dialog."},{"label":"Part ID","value":"The part being held."},{"label":"Bin Content ID","value":"The exact bin the hold applies to. Find it in the bin detail on Stock Levels."},{"label":"Job ID (optional)","value":"The job the stock is held for. Blank for a general hold."},{"label":"Quantity","value":"Units to hold. Cannot exceed what is available in that bin (on hand minus already reserved)."},{"label":"Release (lock-open button)","value":"Removes the hold and returns the quantity to available."}]},{"heading":"Count dialog (Inventory home, Quick tab)","items":[{"label":"Count","value":"Sets on hand for one part in one bin. Managers and Admins only."},{"label":"Part","value":"The part to set."},{"label":"Quantity","value":"The total that should be on hand after saving, not the change."},{"label":"Location","value":"Shown only with multiple locations. Otherwise the default location is used."},{"label":"Reason","value":"Required for the audit trail. Defaults to Physical count if left blank."},{"label":"Save count","value":"Sets the bin (creating it if new) and writes an Adjustment movement for the difference."}]},{"heading":"Admin > GS1 / GTIN","items":[{"label":"GS1 company prefix","value":"6 to 11 digits exactly as licensed by GS1. Blank means internal barcodes only."},{"label":"Remaining GTIN capacity","value":"How many more GTINs can be auto-allocated under the prefix. Shown once a prefix is saved."},{"label":"Save","value":"Applies the prefix. Managers and Admins only."}]},{"heading":"Part GTIN section","items":[{"label":"Assign GTIN","value":"Opens the assign dialog. Shown when the part has no GTIN."},{"label":"Auto-allocate","value":"Generates the next unused GTIN under your company prefix."},{"label":"GTIN (manual)","value":"Paste a GTIN you purchased separately: 8, 12, 13 or 14 digits with a valid check digit."},{"label":"GS1 chip","value":"Shows the assigned GTIN."},{"label":"Remove","value":"Clears the GTIN and returns the part to its internal barcode."}]}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Advanced Inventory — Knowledge Check",
            Slug = "inventory-adv-quiz",
            Summary = "Six questions on cycle counts, reservations, the Count override and GS1 GTINs",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 10,
            AppRoutes = """["/inventory/cycleCounts"]""",
            Tags = """["inventory","quiz","gs1"]""",
            ContentJson = """{"passingScore":80,"shuffleQuestions":false,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"q1","text":"You approve a cycle count where one line was counted at 12 against an expected 15. What happens to that bin?","options":[{"id":"q1a","text":"Nothing until a purchase order is created","isCorrect":false},{"id":"q1b","text":"On hand is set to 12 and a Cycle Count movement of 3 is written","isCorrect":true},{"id":"q1c","text":"On hand stays 15 and a note is added","isCorrect":false},{"id":"q1d","text":"The bin is deleted","isCorrect":false}],"explanation":"Approve & Adjust Stock sets each changed bin to the actual quantity and records a movement for the difference."},{"id":"q2","text":"A bin shows 40 on hand and 30 reserved. How many units can you reserve for a new job from that bin?","options":[{"id":"q2a","text":"40","isCorrect":false},{"id":"q2b","text":"30","isCorrect":false},{"id":"q2c","text":"10","isCorrect":true},{"id":"q2d","text":"As many as the job needs","isCorrect":false}],"explanation":"Reservations are limited to what is available: on hand minus already reserved, so 40 minus 30 is 10."},{"id":"q3","text":"A cycle count approval fails with a message about reserved units. What is the right next step?","options":[{"id":"q3a","text":"Release the reservation on the Reservations tab, then approve again","isCorrect":true},{"id":"q3b","text":"Reject the count and delete the part","isCorrect":false},{"id":"q3c","text":"Change the expected quantity to match","isCorrect":false},{"id":"q3d","text":"Receive stock against a purchase order","isCorrect":false}],"explanation":"Forge will not let a count drop a bin below its reserved quantity. Release the hold first, then approve."},{"id":"q4","text":"Which users can use the Count button on the Inventory home Quick tab?","options":[{"id":"q4a","text":"Anyone who can see the Inventory page","isCorrect":false},{"id":"q4b","text":"Managers and Admins only","isCorrect":true},{"id":"q4c","text":"Only the person who received the stock","isCorrect":false},{"id":"q4d","text":"Only Admins","isCorrect":false}],"explanation":"Count bypasses the purchasing trail, so it is restricted to Managers and Admins. Everyone else can start a cycle count instead."},{"id":"q5","text":"On the Count dialog you enter Quantity 25 for a bin that currently shows 20. What is on hand afterward?","options":[{"id":"q5a","text":"45","isCorrect":false},{"id":"q5b","text":"25","isCorrect":true},{"id":"q5c","text":"5","isCorrect":false},{"id":"q5d","text":"20 until a manager approves","isCorrect":false}],"explanation":"Count sets the total, not the change. The bin becomes 25 and an Adjustment movement of 5 is written."},{"id":"q6","text":"Which part should get a GS1 GTIN?","options":[{"id":"q6a","text":"A bag of hardware used only inside your assemblies","isCorrect":false},{"id":"q6b","text":"A finished product listed on an online marketplace","isCorrect":true},{"id":"q6c","text":"Raw bar stock","isCorrect":false},{"id":"q6d","text":"Every part, so barcodes are consistent","isCorrect":false}],"explanation":"GTINs are for items that ship to retail, marketplaces or a customer supply chain that requires GS1. Everything else keeps the free internal barcode."}]}"""
        });
    }
}
