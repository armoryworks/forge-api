using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data.TrainingContent;

public class MobileTraining : TrainingContentBase
{
    public MobileTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override IReadOnlyList<string> Capabilities =>
    [
        "CAP-MOBILE-CORE", "CAP-MOBILE-SCAN", "CAP-MOBILE-CLOCK", "CAP-MOBILE-JOBS", "CAP-MOBILE-STOCK", "CAP-MOBILE-LOOKUP",
    ];

    public override async Task SeedAsync()
    {
        await GetOrCreateModule(new TrainingModule
        {
            Title = "The Forge Phone App",
            Slug = "mobile-overview",
            Summary = "What the phone app does on the floor: scan a label, clock in and out, move stock, check a job — and undo any of it.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 5,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/app/scan"]""",
            Tags = """["mobile","shop-floor","scan"]""",
            ContentJson = """{"body":"## The Forge Phone App\n\nThe phone app is for people on the floor. It does five things, each on its own tab, and every one of them starts with the camera or a single big button. There is almost nothing to type.\n\n### Getting the app on your phone\n\nYour admin gives you a QR code (or types one out). Open the app, point the camera at the code, and you are in — no account name, no password. The first time, the app shows the server's certificate fingerprint and asks you to accept it. After that you choose a six-digit PIN (and fingerprint or face unlock if your phone has it). The app locks itself after a while; the PIN opens it again.\n\nOn a **shared phone** (one the whole shift uses) there is no PIN. Instead, every action asks you to scan your badge first, so the work is recorded under your name and not the last person's.\n\n### Scan\n\nThis is the home tab. Point the camera at any Forge label — a job traveler, a part tag, a bin, a badge — and the app tells you what it is and offers the two or three things you can do with it. Point at a job and you get **Move to next step**, **Start timer**, **Details**. Point at a part and you get **Move stock**. Unknown labels buzz twice.\n\nIf the camera cannot read a label (torn, dirty, glare), there is a small **Type it in** link in the corner. It takes the printed id and does exactly what a scan would.\n\n### Clock\n\nOne button. It says **Clock in** when you are out and **Clock out** when you are in, with a **Start break** button underneath while you are clocked in. Your status shows in big letters so you can check it from across the bench.\n\n### Jobs\n\nWhere a job is, what is next, who is on it. From here you can move it one column forward, start or stop the timer, add a note (pick from the presets, type, or dictate), or attach a photo.\n\n### Move\n\nScan the part, scan the bin it is in, scan the bin it is going to, check the quantity (it starts at what is on hand), tap **Done**. If the part is lot-tracked you pick the lot first.\n\n### Lookup\n\nThe one place you type. Search a job number, part, customer or bin — or tap the microphone and say it — and you get the same action sheet a scan gives.\n\n### Undo\n\nEvery action shows a blue bar at the bottom with **Undo** for thirty seconds. Moved the wrong job? Clocked in by mistake? Tap Undo. There are no \"Are you sure?\" pop-ups anywhere in the app because Undo is faster and safer.\n\n### When the signal drops\n\nKeep working. The app saves each action on the phone, shows a small cloud icon with a count in the header, and sends everything in order as soon as it is back online. Undo still works on saved actions — it just removes them before they send. If the server later refuses one (say, a job someone else already moved), the header shows a warning and the item is listed under **Needs attention** for someone at a desk to sort out.\n\n### The gear\n\nTop right. Switch between Forge servers if you work at more than one site, change your PIN, turn fingerprint unlock on or off, change the language, and **Report a problem** — which sends what you typed straight to your admins.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Managing Phones — Admin Guide",
            Slug = "mobile-admin-devices",
            Summary = "Turning the phone app on for your Forge, enrolling personal and shared phones, revoking a lost one, and the certificate fingerprint rule.",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 6,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/admin/users","/admin/capabilities"]""",
            Tags = """["mobile","admin","devices","security"]""",
            ContentJson = """{"body":"## Managing Phones\n\n### Turning it on\n\nThe phone app is off until you enable it. Under **Admin > Capabilities**, turn on **Mobile app device enrollment** (`CAP-MOBILE-CORE`), then one flag per screen you want people to have: Scan, Clock, Job Status, Move Stock, Lookup. A screen that is off simply does not appear on the phone, and the server refuses its requests even if someone finds the URL. You cannot turn the core off while any screen is on — switch the screens off first.\n\n### Enrolling someone's phone\n\nOn **Admin > Users**, the phone icon next to a person opens **Add a device**. It shows a QR code that is good for ten minutes and for one phone. The person scans it in the app and is enrolled — no password on the phone, ever. The dialog also lists the phones already enrolled for that person, when each was last seen, and a **Sign out** button per phone.\n\nIf they cannot scan (no camera in front of them), they can type the server address into the app and sign in with their normal password and second factor instead.\n\n### Shared phones\n\nFor a phone the whole shift passes around, use **Shared device** at the top of the Users page. A shared phone is enrolled to your Forge, not to a person: it has no PIN, and every action asks for a badge scan first so the work lands on the right person. It cannot be used to see anything beyond the five screens.\n\n### When a phone is lost\n\nSign it out from the Add a device dialog. The phone is cut off immediately — its saved sign-in stops working, and the next time it reaches the server it erases everything Forge kept on it. Nothing the person does on that phone afterward is recorded. The person's other phones are not affected.\n\nIf a phone's sign-in is ever seen twice (someone restored a backup of it, or copied it), the server treats that as tampering: it cuts off every copy and marks the device **flagged** in the list. Re-enroll the phone you trust and sign out the rest.\n\n### The certificate fingerprint\n\nPhones pin your server's TLS certificate the first time they connect and check it on every reconnect. That is what stops a fake Wi-Fi network from impersonating your Forge. It also means: **before your certificate is renewed, put the new fingerprint into the server settings** (`MOBILE_CERT_SHA256` on the deploy box). Do it after, and every phone refuses to connect until it is re-enrolled. Your deploy documentation has the one-line command to compute it.\n\n### Problem reports\n\nWhen someone taps **Report a problem** on their phone, every admin gets a notification with what they wrote, which screen they were on, and the app version. It also lands in the server log. Nothing goes anywhere outside your Forge.\n\n### Stale phones\n\nA phone that has not checked in for thirty days shows as **stale** in the device list. That is usually a phone that was replaced without being signed out — sign it out.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Add a Phone — Guided Tour",
            Slug = "mobile-admin-walkthrough",
            Summary = "A guided tour of the Users page controls that enroll a personal or shared phone.",
            ContentType = TrainingContentType.Walkthrough,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/admin/users"]""",
            Tags = """["mobile","admin","walkthrough"]""",
            ContentJson = """{"appRoute":"/admin/users","startButtonLabel":"Tour Add a Phone","steps":[{"element":"[data-testid='admin-user-add-device']","popover":{"title":"Add a Device","description":"The phone icon on a person's row opens Add a device: a ten-minute QR code for their phone, plus the list of phones they already have and a Sign out button for each.","side":"left"}},{"element":"[data-testid='admin-shared-device']","popover":{"title":"Shared Device","description":"For a phone the whole shift uses. It is enrolled to your Forge, not a person; every action on it starts with a badge scan.","side":"bottom"}}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Phone App — Knowledge Check",
            Slug = "mobile-quiz",
            Summary = "Five questions on enrolling, undo, offline work, and lost phones.",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 3,
            IsPublished = true,
            SortOrder = 4,
            AppRoutes = """["/app/scan"]""",
            Tags = """["mobile","quiz"]""",
            ContentJson = """{"passingScore":80,"questions":[{"question":"How does a person get the app connected to your Forge?","options":["They type a username and password into the app","They scan a QR code an admin issued from Admin > Users","They download it from the company website and it finds the server","They email their phone number to IT"],"correctIndex":1,"explanation":"Enrollment is a one-phone, ten-minute QR code from Add a device. No password is ever typed on the phone."},{"question":"You moved the wrong job forward on your phone. What do you do?","options":["Tell a supervisor to fix it on the desktop","Tap Undo on the blue bar within thirty seconds","Scan the job again to move it back","Restart the app"],"correctIndex":1,"explanation":"Every action offers Undo for thirty seconds; it moves the job straight back to the column it came from."},{"question":"The Wi-Fi drops while you are moving stock. What happens to what you did?","options":["It is lost — do it again when you are back online","The app refuses to do anything until the signal returns","It is saved on the phone and sent in order when the signal returns","It is emailed to your admin"],"correctIndex":2,"explanation":"Actions queue on the phone with a count in the header and replay in order on reconnect; Undo still works on queued actions."},{"question":"A phone was lost on Friday. What does an admin do?","options":["Change the company Wi-Fi password","Sign the phone out from the person's Add a device dialog","Delete the person's user account","Nothing — the phone locks itself after a day"],"correctIndex":1,"explanation":"Sign out cuts the phone off immediately and it erases its Forge data the next time it reaches the server."},{"question":"Your server's TLS certificate renews next week. What must happen first?","options":["Nothing — phones pick up the new certificate automatically","Every phone must be re-enrolled after the renewal","The new fingerprint goes into the server settings before the renewal","Turn CAP-MOBILE-CORE off during the renewal"],"correctIndex":2,"explanation":"Phones pin the fingerprint. Set MOBILE_CERT_SHA256 to the new value before the certificate changes; otherwise every phone refuses to connect until re-enrolled."}]}"""
        });
    }
}
