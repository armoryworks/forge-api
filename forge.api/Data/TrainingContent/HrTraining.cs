using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Data.TrainingContent;

public class HrTraining : TrainingContentBase
{
    public HrTraining(AppDbContext db, Dictionary<string, int> slugMap) : base(db, slugMap) { }

    public override IReadOnlyList<string> Capabilities =>
    [
        "CAP-IDEN-AUTH-SSO",
    ];

    public override async Task SeedAsync()
    {
        await GetOrCreateModule(new TrainingModule
        {
            Title = "Signing In with Single Sign-On",
            Slug = "hr-sso-overview",
            Summary = "How to sign in to Forge with your Google or Microsoft work account instead of a separate password, and what to do when it does not work",
            ContentType = TrainingContentType.Article,
            EstimatedMinutes = 4,
            IsPublished = true,
            SortOrder = 1,
            AppRoutes = """["/login"]""",
            Tags = """["sso","sign-in","login","identity","hr"]""",
            ContentJson = """{"body":"## Signing In with Single Sign-On\n\nSingle sign-on (SSO) lets you get into Forge with the same work account you already use for email — Google Workspace, Microsoft 365, or your company's own identity provider. There is no separate Forge password to remember, and when someone leaves the company, turning off their work account turns off their Forge access too.\n\nSSO is optional. When your company has set it up, the sign-in page shows one or more **Sign in with** buttons below the usual email and password boxes. If you do not see those buttons, your company has not turned it on and you sign in with your email and Forge password as before.\n\n### Signing in\n\n1. Open Forge. You land on the sign-in page.\n2. Below the password form, under **Or sign in with**, click the button for your work account — **Google**, **Microsoft**, or your company's named provider.\n3. Your browser goes to that provider. Pick your work account and, if asked, enter its password and approve any two-step verification the provider uses.\n4. You come back to Forge with a short *Completing sign-in* message, then land on your dashboard.\n\nIf you were already signed in to your work account in that browser, step 3 usually takes a single click.\n\n### The first time\n\nYou do not need to do anything special the first time. Forge matches the email address from your work account to the account your administrator already created for you and links the two. From then on, that provider button signs you straight in.\n\nThis means you must already have a Forge account before SSO can work. Signing in with a work account does **not** create one. If you see *No account found*, ask your administrator to add you — using the same email address as your work account — and then try again.\n\n### Password sign-in still works\n\nLinking your work account does not remove your Forge password. If the provider is down, or you are on a device where you cannot reach your work account, you can still type your email and Forge password in the boxes at the top of the sign-in page.\n\n### When it does not work\n\n- **No account found** — there is no active Forge account with your work email. Ask your administrator to create one, or check whether you picked the wrong account at the provider (a personal address instead of your work one).\n- **This email domain is not permitted** — your company allows SSO only for its own email domain, and the account you picked is from a different one. Go back and choose your work account.\n- **Sign-in failed** — the trip to the provider did not complete. Try again; if it keeps happening, sign in with your password and let your administrator know.\n- **Nothing happens after the provider** — check that your browser is not blocking pop-ups or redirects for the Forge address.\n\n### What administrators should know\n\nSSO is turned on per Forge installation by whoever runs your server, under the **Single sign-on** capability; it is not switched on from inside the app. Each provider can be limited to a list of allowed email domains, so only company addresses get in. A person's Forge account must exist, with a matching email, before their first SSO sign-in — create the account first, then tell them to use the provider button. Deactivating the Forge account, or the work account at the provider, stops SSO sign-ins immediately.","sections":[]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Single Sign-On Quick Reference",
            Slug = "hr-sso-reference",
            Summary = "What each part of the sign-in page does when single sign-on is on, and what the SSO messages mean",
            ContentType = TrainingContentType.QuickRef,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 2,
            AppRoutes = """["/login"]""",
            Tags = """["sso","sign-in","reference","hr"]""",
            ContentJson = """{"title":"Single Sign-On Quick Reference","groups":[{"heading":"Sign-in page","items":[{"label":"Email / Password","value":"Your Forge account and Forge password. Always available, even after you have linked a work account."},{"label":"Or sign in with","value":"Appears only when your company has turned single sign-on on. Lists each provider that is set up."},{"label":"Google","value":"Sign in with your Google Workspace account."},{"label":"Microsoft","value":"Sign in with your Microsoft 365 / Azure work account."},{"label":"Named provider (for example SSO)","value":"Your company's own identity provider, shown under whatever name your administrator gave it."},{"label":"Completing sign-in","value":"The short screen you see on the way back from the provider. It goes to your dashboard on its own."}]},{"heading":"Messages","items":[{"label":"No account found","value":"No active Forge account has the email address on the work account you picked. Ask an administrator to create your account with that email, or pick your work account instead of a personal one."},{"label":"Email domain not permitted","value":"Your company limits SSO to its own email domains and the account you chose is from another domain. Choose your work account."},{"label":"Sign-in failed","value":"The provider did not finish the sign-in. Try again, or use your email and password."}]},{"heading":"Good to know","items":[{"label":"First sign-in","value":"Forge links the work account to your existing Forge account by email. Nothing to set up on your side."},{"label":"Creating accounts","value":"SSO never creates a Forge account. Administrators add people first; the provider button only signs them in."},{"label":"Two-step verification","value":"Whatever verification your work account requires (an authenticator app, a phone prompt) applies when you sign in through it."},{"label":"Leaving the company","value":"Deactivating the Forge account, or the work account at the provider, stops SSO sign-ins right away."},{"label":"Turning SSO on","value":"Done per installation by whoever runs the Forge server, under the Single sign-on capability. It is not a setting inside the app."}]}]}"""
        });

        await GetOrCreateModule(new TrainingModule
        {
            Title = "Single Sign-On — Knowledge Check",
            Slug = "hr-sso-quiz",
            Summary = "Four questions on signing in with a work account, first-time linking and the common messages",
            ContentType = TrainingContentType.Quiz,
            EstimatedMinutes = 2,
            IsPublished = true,
            SortOrder = 3,
            AppRoutes = """["/login"]""",
            Tags = """["sso","sign-in","quiz","hr"]""",
            ContentJson = """{"passingScore":80,"shuffleQuestions":false,"shuffleOptions":true,"showExplanationsAfterSubmit":true,"questions":[{"id":"q1","text":"A new hire clicks Sign in with Google and sees No account found. What is the fix?","options":[{"id":"q1a","text":"An administrator creates their Forge account using their work email, then they try again","isCorrect":true},{"id":"q1b","text":"They click the button a second time so Forge creates the account","isCorrect":false},{"id":"q1c","text":"They reset their Google password","isCorrect":false},{"id":"q1d","text":"They sign in with a personal Gmail address instead","isCorrect":false}],"explanation":"Single sign-on only signs in accounts that already exist in Forge. It never creates one, so the administrator has to add the person first with a matching email."},{"id":"q2","text":"What happens the first time you sign in with a work account that matches your Forge email?","options":[{"id":"q2a","text":"Forge links the work account to your Forge account and signs you in","isCorrect":true},{"id":"q2b","text":"Forge emails you a code to confirm the link","isCorrect":false},{"id":"q2c","text":"Your Forge password is deleted","isCorrect":false},{"id":"q2d","text":"You are asked to create a second Forge account","isCorrect":false}],"explanation":"The link is made automatically by email address. Your Forge password stays and still works."},{"id":"q3","text":"You do not see any Sign in with buttons on the sign-in page. What does that mean?","options":[{"id":"q3a","text":"Your company has not turned single sign-on on, so you sign in with your email and Forge password","isCorrect":true},{"id":"q3b","text":"Your account has been deactivated","isCorrect":false},{"id":"q3c","text":"Your browser is blocking Forge","isCorrect":false},{"id":"q3d","text":"You need to sign in once with a password before the buttons appear","isCorrect":false}],"explanation":"The provider buttons only appear when SSO is set up for your installation. Without them, password sign-in is the normal path."},{"id":"q4","text":"You get Email domain not permitted after picking an account at the provider. What most likely went wrong?","options":[{"id":"q4a","text":"You picked an account from a different email domain than your company allows, such as a personal address","isCorrect":true},{"id":"q4b","text":"Your Forge password has expired","isCorrect":false},{"id":"q4c","text":"The provider is down","isCorrect":false},{"id":"q4d","text":"You have not completed your onboarding training","isCorrect":false}],"explanation":"Administrators can limit SSO to the company's own email domains. Go back and choose your work account."}]}"""
        });
    }
}
