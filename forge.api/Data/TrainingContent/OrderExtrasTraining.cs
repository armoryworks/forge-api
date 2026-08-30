using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data.TrainingContent;

public class OrderExtrasTraining : TrainingContentBase
{
    public OrderExtrasTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override IReadOnlyList<string> Capabilities =>
    [
        "CAP-MD-PART-COMPLIANCE",
    ];

    public override async Task SeedAsync()
    {
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Part Compliance Fields",
            Slug = "orders-adv-part-compliance",
            Summary = "Hazmat class, shelf life, backflush policy, receiving inspection templates and tariff codes: the part fields that matter when you ship regulated or imported material",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/parts","/parts/new"]""",
            Tags = """["parts","compliance","hazmat","shelf-life","hts","purchasing","quality"]""",
            ContentJson = """{"body":"## Part Compliance Fields\n\nMost shops can describe a part with a number, a name, a unit of measure and a cost. The moment you buy from overseas, handle anything hazardous, or sell material that goes stale, the part record needs a few more answers. Forge keeps those answers in one place — the **Quality** cluster on the part — and only shows them when an admin has turned on the **Part — compliance fields** capability. Shops that never ship internationally or touch regulated material can leave it off and never see the extra boxes.\n\n### What the capability adds\n\nOpen any part and scroll to the **Quality** cluster. Without the capability you see the receiving inspection basics: whether the part needs inspection on receipt, how often, and after how many clean receipts to stop checking. With the capability on, the cluster grows:\n\n- **Receiving Inspection Template** — which checklist the receiving inspector fills in for this part. Pick from the templates your quality team has built. Leave it blank and inspection still happens; it just uses no fixed checklist.\n- **Hazmat Class** — the hazard classification for the material (for example a DOT or UN class). Anything you type here rides along to shipping paperwork and tells the warehouse to treat the part as regulated.\n- **Shelf Life (days)** — how long the material is good after it is received or made. Forge uses it to work out an expiration date on each lot so old stock is flagged before it ships.\n- **Backflush Policy** — how component inventory is consumed when this part is built: **Auto** relieves stock when the operation completes, **Manual** waits for someone to issue material, **None** never backflushes. **Default** uses the plant-wide setting.\n\n### Where the rest of it lives\n\nThe capability also unlocks a few fields in the new-part workflow that are not in the Quality cluster:\n\n- **Flags** step — **Ship as kit** (children deliver with the parent) and **Configurable** (turns on the configurator wizard for this parent). The Flags step also carries the same **Backflush Policy** so you can set it while building the part.\n- **Source Part** step — for parts that go out to a vendor for finishing, the in-house part that is sent out before plating, heat treat or paint.\n- **Inventory** step — a **Default Bin** so receipts land in the right place without asking.\n\nTariff codes are a vendor-source detail rather than a part-wide one, because the same part bought from two countries can carry two classifications. Open the part's **Vendor Sources** panel and set the **HTS Code** on each vendor part. Purchasing and customs paperwork read it from there.\n\n### Why it matters to sales and purchasing\n\n- A **hazmat class** on the part is what stops a regulated item from going out on an ordinary parcel label, and it is the flag your carrier will ask about.\n- A **shelf life** is the difference between a customer receiving fresh material and a return you have to credit. The warehouse picks the oldest good lot first only if Forge knows when lots expire.\n- An **HTS code** on the vendor source lets the purchase order carry the classification so the broker does not have to guess at the border.\n- A **receiving inspection template** means the inspector checks the things that matter for this part rather than a generic list.\n\n### What to do first\n\n1. Ask an admin to turn on **Part — compliance fields** under Admin > Capabilities.\n2. Go through your regulated parts and set **Hazmat Class** and **Shelf Life** on each.\n3. For imported parts, open **Vendor Sources** and fill in the **HTS Code** on every foreign vendor part.\n4. Have quality build one or two **Receiving Inspection Templates** and attach them to the parts that need them.\n5. Leave **Backflush Policy** on Default unless a specific part needs to behave differently from the rest of the plant.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Part Compliance — Guided Tour",
            Slug = "orders-adv-part-compliance-walkthrough",
            Summary = "Find the compliance fields on a part: inspection template, hazmat class, shelf life and backflush policy",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/parts"]""",
            Tags = """["parts","compliance","walkthrough"]""",
            ContentJson = """{"appRoute":"/parts","startButtonLabel":"Tour Part Compliance","steps":[{"element":"[data-testid='new-part-btn']","popover":{"title":"Start From a Part","description":"Compliance fields live on the part record. Open an existing part from the list, or create one here, then scroll to the Quality cluster.","side":"bottom"}},{"element":"[data-testid='part-quality-requires-inspection']","popover":{"title":"Requires Receiving Inspection","description":"The basic switch every shop has. Turn it on and receipts of this part stop for inspection before they can be put away.","side":"right"}},{"element":"[data-testid='part-quality-frequency']","popover":{"title":"Inspection Frequency","description":"Every receipt, first article only, skip-lot, or random sampling. Pair skip-lot with Skip After N Receipts.","side":"right"}},{"element":"[data-testid='part-quality-hazmat']","popover":{"title":"Hazmat Class","description":"Only visible with Part — compliance fields on. Enter the hazard class so shipping and the warehouse treat the material as regulated.","side":"right"}},{"element":"[data-testid='part-quality-shelf-life']","popover":{"title":"Shelf Life (days)","description":"How long the material stays good. Forge uses it to expire lots and to pick oldest-good-stock first.","side":"right"}},{"element":"[data-testid='part-quality-backflush']","popover":{"title":"Backflush Policy","description":"Auto, Manual or None for how components are consumed when this part is built. Default follows the plant setting.","side":"right"}},{"element":"[data-testid='part-quality-save']","popover":{"title":"Save","description":"Nothing changes until you save. The validation button warns you if a field is out of range before you commit.","side":"top"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Part Compliance Field Reference",
            Slug = "orders-adv-part-compliance-field-reference",
            Summary = "Every compliance-related field on the part Quality cluster, the new-part workflow and the vendor source",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/parts","/parts/new"]""",
            Tags = """["parts","compliance","reference","fields"]""",
            ContentJson = """{"title":"Part Compliance Field Reference","groups":[{"heading":"Quality cluster — always shown","items":[{"label":"Requires Receiving Inspection","value":"On means every receipt of this part is held for inspection before put-away."},{"label":"Inspection Frequency","value":"Every Receipt, First Article Only, Skip-Lot, or Random Sampling. Unset falls back to inspecting every receipt when inspection is required."},{"label":"Skip After N Receipts","value":"For Skip-Lot: how many consecutive passing receipts before inspection is skipped."}]},{"heading":"Quality cluster — with Part — compliance fields on","items":[{"label":"Receiving Inspection Template","value":"The checklist the inspector uses for this part. Built by quality under Receiving Inspection Templates."},{"label":"Hazmat Class","value":"Hazard classification of the material. Free text; use the class your carrier and safety data sheet reference."},{"label":"Shelf Life (days)","value":"Days the material stays usable after receipt or completion. Drives lot expiration dates. Leave blank for parts that do not expire."},{"label":"Backflush Policy","value":"Default (plant setting), Auto (consume components when the operation completes), Manual (someone issues material), or None."}]},{"heading":"New-part workflow — Flags step","items":[{"label":"Ship as kit","value":"Children deliver together with the parent instead of as a built assembly."},{"label":"Configurable","value":"Enables the configurator wizard for this parent part."},{"label":"Backflush Policy","value":"Same field as on the Quality cluster, available while the part is being created."}]},{"heading":"New-part workflow — Source Part step","items":[{"label":"Source Part","value":"For finished-by-vendor parts: the pre-finishing in-house part that is sent out for plating, heat treat or paint."}]},{"heading":"New-part workflow — Inventory step","items":[{"label":"Default Bin","value":"The bin receipts of this part land in unless the receiver picks a different one."}]},{"heading":"Vendor Sources panel","items":[{"label":"HTS Code","value":"Harmonized tariff classification for this part from this vendor. Set per vendor source because the same part from two countries can classify differently."}]}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Part Compliance — Knowledge Check",
            Slug = "orders-adv-part-compliance-quiz",
            Summary = "Five questions on hazmat, shelf life, backflush, inspection templates and tariff codes",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/parts"]""",
            Tags = """["parts","compliance","quiz"]""",
            ContentJson = """{"passingScore":80,"shuffleQuestions":false,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"q1","text":"You open a part's Quality cluster and see only the receiving inspection fields — no Hazmat Class or Shelf Life. Why?","options":[{"id":"q1a","text":"The part is a purchased item; compliance fields only apply to made parts","isCorrect":false},{"id":"q1b","text":"The Part — compliance fields capability is turned off","isCorrect":true},{"id":"q1c","text":"The part has no vendor source yet","isCorrect":false},{"id":"q1d","text":"Those fields only appear after the first receipt","isCorrect":false}],"explanation":"Hazmat Class, Shelf Life, Backflush Policy and the inspection template picker are hidden until an admin enables Part — compliance fields."},{"id":"q2","text":"Where do you record the HTS (tariff) code for an imported part?","options":[{"id":"q2a","text":"On the vendor source for that part","isCorrect":true},{"id":"q2b","text":"In the part's Hazmat Class field","isCorrect":false},{"id":"q2c","text":"On the customer record","isCorrect":false},{"id":"q2d","text":"In the Flags step of the new-part workflow","isCorrect":false}],"explanation":"The HTS code is a per-vendor-source field, because the same part bought from different countries can carry different classifications."},{"id":"q3","text":"What does Shelf Life (days) control?","options":[{"id":"q3a","text":"How long a quote for the part stays valid","isCorrect":false},{"id":"q3b","text":"The expiration date Forge assigns to each lot of the part","isCorrect":true},{"id":"q3c","text":"How many days a purchase order can stay open","isCorrect":false},{"id":"q3d","text":"The lead time shown to customers","isCorrect":false}],"explanation":"Shelf life is counted from receipt or completion to set a lot's expiration, so old material is flagged before it ships."},{"id":"q4","text":"A part's Backflush Policy is set to Manual. What happens when an operation on it completes?","options":[{"id":"q4a","text":"Component stock is relieved automatically","isCorrect":false},{"id":"q4b","text":"Nothing is consumed until someone issues the material","isCorrect":true},{"id":"q4c","text":"The plant-wide default decides","isCorrect":false},{"id":"q4d","text":"The operation cannot be completed","isCorrect":false}],"explanation":"Auto consumes on completion, Manual waits for a material issue, None never backflushes, and Default follows the plant setting."},{"id":"q5","text":"Which field decides the checklist the receiving inspector follows for a part?","options":[{"id":"q5a","text":"Inspection Frequency","isCorrect":false},{"id":"q5b","text":"Receiving Inspection Template","isCorrect":true},{"id":"q5c","text":"Skip After N Receipts","isCorrect":false},{"id":"q5d","text":"Default Bin","isCorrect":false}],"explanation":"The template is the checklist; frequency and skip-after only decide how often inspection happens."}]}"""
        });
    }
}
