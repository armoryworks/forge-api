namespace QBEngineer.Core.Settings;

/// <summary>
/// Phase 1m option-3 — single source of truth for every integration the
/// admin can configure. Replaces the inline list previously hand-rolled
/// in GetIntegrationSettings.
///
/// Each entry references field keys defined in the per-integration
/// SettingDescriptor lists (OAuthImapSettings, TwilioSettings, etc.).
/// </summary>
public static class IntegrationDescriptorCatalog
{
    private static List<IntegrationDescriptor>? _all;

    public static IReadOnlyList<IntegrationDescriptor> All => _all ??= Build();

    public static IntegrationDescriptor? FindByProvider(string provider)
        => All.FirstOrDefault(i => string.Equals(i.Provider, provider, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Categories => All
        .Select(i => i.Category)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<IntegrationDescriptor> Build() =>
    [
        // ── Communications / Email ─────────────────────────────────
        new(
            Provider: "gmail-oauth",
            Name: "Gmail / Google Workspace (OAuth)",
            Description: "Auto-log inbound and outbound Gmail against active leads + customer contacts. OAuth — no app passwords required.",
            Icon: "mark_email_read",
            Category: "communications",
            FieldKeys:
            [
                OAuthImapSettings.KeyRedirectUri,
                OAuthImapSettings.KeyGoogleClientId,
                OAuthImapSettings.KeyGoogleClientSecret,
            ],
            IsConfiguredCheckKey: OAuthImapSettings.KeyGoogleClientId,
            LogoUrl: "https://logo.clearbit.com/gmail.com",
            SetupSteps:
            [
                "Go to console.cloud.google.com and create a new project (or select an existing one).",
                "Enable the Gmail API: APIs & Services → Library → search 'Gmail API' → Enable.",
                "Configure the OAuth consent screen: APIs & Services → OAuth consent screen. External user type unless you have a Workspace tenant.",
                "Create credentials: APIs & Services → Credentials → Create Credentials → OAuth Client ID → Web application. Add the Redirect URI shown in this card as an authorized redirect URI.",
                "Authorize the https://mail.google.com/ scope. Copy the Client ID + Client Secret into the fields below.",
            ],
            SignupUrl: "https://console.cloud.google.com/apis/credentials"),

        new(
            Provider: "microsoft-oauth",
            Name: "Outlook / Microsoft 365 (OAuth)",
            Description: "Auto-log inbound and outbound Outlook / Microsoft 365 against active leads + customer contacts. Supports MFA + work/school accounts.",
            Icon: "forward_to_inbox",
            Category: "communications",
            FieldKeys:
            [
                OAuthImapSettings.KeyRedirectUri,
                OAuthImapSettings.KeyMicrosoftClientId,
                OAuthImapSettings.KeyMicrosoftClientSecret,
            ],
            IsConfiguredCheckKey: OAuthImapSettings.KeyMicrosoftClientId,
            LogoUrl: "https://logo.clearbit.com/microsoft.com",
            SetupSteps:
            [
                "Go to portal.azure.com → Azure Active Directory → App registrations → New registration.",
                "Account types: 'Accounts in any organizational directory and personal Microsoft accounts' (most flexible).",
                "Redirect URI: Web platform, paste the Redirect URI from this card.",
                "After creation: Certificates & secrets → New client secret. Copy the value immediately (shown only once).",
                "API permissions → Add a permission → APIs my organization uses → search 'Office 365 Exchange Online' → Delegated → IMAP.AccessAsUser.All. Also add offline_access (required for refresh tokens).",
                "Grant admin consent if you're on a Workspace/Azure tenant. Personal MSA accounts auto-consent on first auth.",
                "Copy Application (client) ID + the secret value into the fields below.",
            ],
            SignupUrl: "https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps"),

        // ── Voice ─────────────────────────────────────────────────
        new(
            Provider: "twilio",
            Name: "Twilio Voice",
            Description: "Auto-log inbound + outbound calls against leads/contacts. Webhook-driven — no polling.",
            Icon: "phone_in_talk",
            Category: "communications",
            FieldKeys:
            [
                TwilioSettings.KeyMode,
                TwilioSettings.KeyAccountSid,
                TwilioSettings.KeyAuthToken,
                TwilioSettings.KeyRequireSignature,
            ],
            IsConfiguredCheckKey: TwilioSettings.KeyAuthToken,
            LogoUrl: "https://logo.clearbit.com/twilio.com",
            SetupSteps:
            [
                "Sign up at twilio.com — free trial includes a sandbox phone number.",
                "From the Twilio Console, copy your Account SID + Auth Token. These appear on the dashboard right after login.",
                "Configure the Voice Status Callback URL on your phone number(s): Phone Numbers → Manage → click number → A Call Comes In: Webhook → set to https://your-domain/api/v1/communications/webhook/twilio.",
                "Enable Require Signature once you've verified production webhooks are reaching you correctly.",
            ],
            SignupUrl: "https://www.twilio.com/try-twilio"),

        // ── Email infrastructure ──────────────────────────────────
        new(
            Provider: "smtp",
            Name: "SMTP Email",
            Description: "Outbound email notifications, invoices, employee onboarding mail.",
            Icon: "email",
            Category: "service",
            FieldKeys:
            [
                SmtpSettings.KeyMode,
                SmtpSettings.KeyHost,
                SmtpSettings.KeyPort,
                SmtpSettings.KeyUseSsl,
                SmtpSettings.KeyUsername,
                SmtpSettings.KeyPassword,
                SmtpSettings.KeyFromAddress,
                SmtpSettings.KeyFromName,
            ],
            IsConfiguredCheckKey: SmtpSettings.KeyHost,
            LogoUrl: null, // generic protocol
            SetupSteps:
            [
                "Create a free account at mailtrap.io for testing — emails are captured in a fake inbox, never reach real recipients.",
                "Email Testing → My Inbox → SMTP Settings tab. Copy Host (sandbox.smtp.mailtrap.io), Port (2525), Username, Password.",
                "Enter the credentials below + your From Address. Send a test email and watch it appear in Mailtrap.",
                "For production, swap to SendGrid / Postmark / your domain mail server using the same field shape.",
            ],
            SignupUrl: "https://mailtrap.io/register/signup"),

        // ── Storage ───────────────────────────────────────────────
        new(
            Provider: "minio",
            Name: "MinIO Storage",
            Description: "S3-compatible file storage for documents and attachments.",
            Icon: "cloud_upload",
            Category: "service",
            FieldKeys:
            [
                MinioSettings.KeyMode,
                MinioSettings.KeyEndpoint,
                MinioSettings.KeyAccessKey,
                MinioSettings.KeySecretKey,
                MinioSettings.KeyBucket,
                MinioSettings.KeyUseSsl,
            ],
            IsConfiguredCheckKey: MinioSettings.KeyEndpoint,
            LogoUrl: "https://logo.clearbit.com/min.io",
            SetupSteps:
            [
                "MinIO is already running locally via Docker — no external account or sign-up is needed.",
                "Open the MinIO console at http://localhost:9001 (default login: minioadmin / minioadmin) to browse buckets.",
                "The endpoint is pre-configured. The Docker container IS the sandbox — these defaults work for local dev.",
                "For production, swap Endpoint to your real MinIO / S3 host and rotate the access keys.",
            ]),

        // ── Address validation ────────────────────────────────────
        new(
            Provider: "usps",
            Name: "USPS Address Validation",
            Description: "USPS Addresses API v3 for address verification (free with Business account).",
            Icon: "local_post_office",
            Category: "service",
            FieldKeys: [UspsSettings.KeyMode, UspsSettings.KeyConsumerKey, UspsSettings.KeyConsumerSecret],
            IsConfiguredCheckKey: UspsSettings.KeyConsumerKey,
            LogoUrl: "https://logo.clearbit.com/usps.com",
            SetupSteps:
            [
                "Go to cop.usps.com (USPS Customer Onboarding Portal) — sign in with a USPS business account or create one for free.",
                "My Apps → register a new application. Copy the credentials shown.",
                "For sandbox testing, use the TEM base URL: apis-tem.usps.com instead of apis.usps.com. Labels there are watermarked.",
            ],
            SignupUrl: "https://cop.usps.com"),

        // ── E-signature ───────────────────────────────────────────
        new(
            Provider: "docuseal",
            Name: "DocuSeal Document Signing",
            Description: "Electronic document signing for employee forms and contracts.",
            Icon: "draw",
            Category: "service",
            FieldKeys:
            [
                DocuSealSettings.KeyMode,
                DocuSealSettings.KeyApiUrl,
                DocuSealSettings.KeyPublicBaseUrl,
                DocuSealSettings.KeyApiKey,
                DocuSealSettings.KeyWebhookSecret,
                DocuSealSettings.KeyTimeoutSeconds,
            ],
            IsConfiguredCheckKey: DocuSealSettings.KeyApiKey,
            LogoUrl: "https://logo.clearbit.com/docuseal.com",
            SetupSteps:
            [
                "The local DocuSeal container (port 3000) is your sandbox — no external sign-up required for self-hosted mode. Or create a free cloud account at docuseal.com/sign_up.",
                "Cloud: avatar (top-right) → Console → API. Toggle Test Mode ON. Copy the Test Mode API Key (free + unlimited).",
                "Self-hosted: open http://localhost:3000, create an admin, then Settings → API to generate a key.",
                "Test Mode keys cannot be used in production and vice versa.",
            ],
            SignupUrl: "https://docuseal.com/sign_up"),

        // ── AI ────────────────────────────────────────────────────
        new(
            Provider: "ai",
            Name: "AI Assistant (Ollama)",
            Description: "Self-hosted AI for smart search, drafting, and document Q&A.",
            Icon: "psychology",
            Category: "service",
            FieldKeys:
            [
                AiSettings.KeyMode,
                AiSettings.KeyBaseUrl,
                AiSettings.KeyChatModel,
                AiSettings.KeyEmbeddingModel,
                AiSettings.KeyVisionModel,
                AiSettings.KeyTimeoutSeconds,
                AiSettings.KeyVisionTimeoutSeconds,
            ],
            IsConfiguredCheckKey: AiSettings.KeyBaseUrl,
            LogoUrl: "https://logo.clearbit.com/ollama.com",
            SetupSteps:
            [
                "Ollama is already running locally via Docker — no external account is needed.",
                "Pull the chat model: docker exec qb-engineer-ai ollama pull gemma3:4b",
                "Pull the embedding model: docker exec qb-engineer-ai ollama pull nomic-embed-text",
                "Larger models (7B+) give better results but require more RAM. The defaults below work on 8GB+.",
            ],
            SignupUrl: "https://ollama.com/library"),

        // ── Shipping carriers ─────────────────────────────────────
        new(
            Provider: "ups",
            Name: "UPS",
            Description: "UPS shipping rate shopping, label creation, and tracking.",
            Icon: "local_shipping",
            Category: "shipping",
            FieldKeys: ["ups.mode", "ups.client-id", "ups.client-secret", "ups.account-number"],
            IsConfiguredCheckKey: "ups.client-id",
            LogoUrl: "https://logo.clearbit.com/ups.com",
            SetupSteps:
            [
                "developer.ups.com → sign in with your UPS.com account (or create one).",
                "Avatar → Apps → Add Apps. 'I want to integrate UPS technology into my business' → associate a UPS shipping account → accept terms.",
                "Add the Authorization (OAuth) product + APIs you need (Rating, Shipping, Tracking). Click the eye icon on the app to reveal Client ID + Client Secret.",
                "Sandbox testing is free — no charges for test shipments. OAuth tokens expire every hour (auto-refresh handled).",
            ],
            SignupUrl: "https://developer.ups.com/"),

        new(
            Provider: "fedex",
            Name: "FedEx",
            Description: "FedEx shipping rate shopping, label creation, and tracking.",
            Icon: "local_shipping",
            Category: "shipping",
            FieldKeys: ["fedex.mode", "fedex.client-id", "fedex.client-secret", "fedex.account-number"],
            IsConfiguredCheckKey: "fedex.client-id",
            LogoUrl: "https://logo.clearbit.com/fedex.com",
            SetupSteps:
            [
                "developer.fedex.com → register and create a free user ID. Log in and create an Organization (required before any project).",
                "My Projects → Create New Project → API Project. Pick the APIs (Rates, Ship, Track).",
                "Open the project → Test tab → API Key (Client ID) + Secret Key (Client Secret).",
                "Sandbox base URL: apis-sandbox.fedex.com (vs production: apis.fedex.com). Tokens expire every hour. No real shipments in sandbox.",
            ],
            SignupUrl: "https://developer.fedex.com/"),

        new(
            Provider: "dhl",
            Name: "DHL Express",
            Description: "DHL Express international shipping.",
            Icon: "flight_takeoff",
            Category: "shipping",
            FieldKeys: ["dhl.mode", "dhl.api-key", "dhl.account-number"],
            IsConfiguredCheckKey: "dhl.api-key",
            LogoUrl: "https://logo.clearbit.com/dhl.com",
            SetupSteps:
            [
                "Register at developer.dhl.com/user/register. Confirm via email.",
                "Get Access / API Catalog → request the DHL Express MyDHL API. Provide your 9-digit DHL Express account number.",
                "Approval takes ~24 hours. You'll get Test + Production access emails.",
                "Once approved: app dashboard → Show Key → API Key + API Secret. Test labels are watermarked + not billed.",
            ],
            SignupUrl: "https://developer.dhl.com/"),

        new(
            Provider: "stamps",
            Name: "Stamps.com",
            Description: "USPS postage printing + shipping labels via Stamps.com.",
            Icon: "local_post_office",
            Category: "shipping",
            FieldKeys: ["stamps.mode", "stamps.username", "stamps.password", "stamps.integration-id"],
            IsConfiguredCheckKey: "stamps.integration-id",
            LogoUrl: "https://logo.clearbit.com/stamps.com",
            SetupSteps:
            [
                "Sign up for a free developer sandbox at developer.stamps.com → Get a Free API Key.",
                "Log in to the developer portal → create a new application → receive your Integration ID (API Key).",
                "Sandbox endpoint: swsim.stamps.com. Sandbox labels are watermarked SAMPLE + not charged.",
                "Username = your Stamps.com login. SOAP-based API — WSDL at swsim.stamps.com/SwsimV111.asmx?WSDL.",
            ],
            SignupUrl: "https://developer.stamps.com/"),

        // ── Accounting providers ──────────────────────────────────
        new(
            Provider: "quickbooks",
            Name: "QuickBooks Online",
            Description: "QuickBooks Online accounting — connected via OAuth. Existing primary accounting integration.",
            Icon: "receipt_long",
            Category: "accounting",
            FieldKeys: ["quickbooks.mode", "quickbooks.client-id", "quickbooks.client-secret", "quickbooks.realm-id"],
            IsConfiguredCheckKey: "quickbooks.client-id",
            LogoUrl: "https://logo.clearbit.com/quickbooks.intuit.com",
            SetupSteps:
            [
                "developer.intuit.com → sign in with Intuit (free dev account, no card required).",
                "My Apps → Create an App → QuickBooks Online and Payments. Pick scopes (Accounting, Payments).",
                "Keys & OAuth → Development tab → copy Client ID + Client Secret.",
                "A sandbox company is auto-created — Dashboard → Sandbox Companies. Comes pre-loaded with sample data.",
                "Use Development credentials for dev. Production requires Intuit app review.",
            ],
            SignupUrl: "https://developer.intuit.com/app/developer/myapps"),

        new(
            Provider: "xero",
            Name: "Xero",
            Description: "Cloud accounting with multi-currency support.",
            Icon: "account_balance_wallet",
            Category: "accounting",
            FieldKeys: ["xero.mode", "xero.client-id", "xero.client-secret", "xero.tenant-id"],
            IsConfiguredCheckKey: "xero.client-id",
            LogoUrl: "https://logo.clearbit.com/xero.com",
            SetupSteps:
            [
                "Free Xero account at xero.com/signup. Then developer.xero.com → My Apps → New App.",
                "Enter app name + Redirect URI. Copy Client ID immediately. Click Generate a secret to create Client Secret (shown once).",
                "Test org: Xero → click your org name → My Xero → Try the Demo Company. Connect via OAuth and pick Demo Company.",
            ],
            SignupUrl: "https://developer.xero.com/app/manage"),

        new(
            Provider: "freshbooks",
            Name: "FreshBooks",
            Description: "Small-business invoicing + accounting.",
            Icon: "receipt_long",
            Category: "accounting",
            FieldKeys: ["freshbooks.mode", "freshbooks.client-id", "freshbooks.client-secret", "freshbooks.account-id"],
            IsConfiguredCheckKey: "freshbooks.client-id",
            LogoUrl: "https://logo.clearbit.com/freshbooks.com",
            SetupSteps:
            [
                "Free FreshBooks account at freshbooks.com/signup → my.freshbooks.com/#/developer.",
                "Create App → enter name, description, website URL, Redirect URI (HTTPS required; self-signed certs work for localhost).",
                "Copy Client ID + Client Secret. Pre-built Authorization URL is shown lower in the page.",
                "Your own FreshBooks account doubles as the sandbox — no separate environment.",
            ],
            SignupUrl: "https://my.freshbooks.com/#/developer"),

        new(
            Provider: "sage",
            Name: "Sage Business Cloud",
            Description: "Sage Business Cloud Accounting.",
            Icon: "business",
            Category: "accounting",
            FieldKeys: ["sage.mode", "sage.client-id", "sage.client-secret"],
            IsConfiguredCheckKey: "sage.client-id",
            LogoUrl: "https://logo.clearbit.com/sage.com",
            SetupSteps:
            [
                "Free dev account at developer.sage.com → My Applications → register.",
                "Enter name, description, Redirect URI. Request Development credentials (sandbox-scoped).",
                "Provisioning can take up to 72 hours. Once approved, copy Client ID + Client Secret.",
                "Sandbox includes sample companies, contacts, invoices.",
            ],
            SignupUrl: "https://developer.sage.com/accounting/"),

        new(
            Provider: "netsuite",
            Name: "NetSuite",
            Description: "NetSuite ERP (Token-Based Authentication).",
            Icon: "corporate_fare",
            Category: "accounting",
            FieldKeys:
            [
                "netsuite.mode",
                "netsuite.account-id",
                "netsuite.consumer-key",
                "netsuite.consumer-secret",
                "netsuite.token-id",
                "netsuite.token-secret",
            ],
            IsConfiguredCheckKey: "netsuite.account-id",
            LogoUrl: "https://logo.clearbit.com/netsuite.com",
            SetupSteps:
            [
                "Free Trial at netsuite.com/portal/free-trial.shtml for a usable dev account. Existing customers request a sandbox via Support.",
                "Setup → Company → Enable Features → SuiteCloud tab → check Token-Based Authentication + REST Web Services.",
                "Setup → Integration → Manage Integrations → New. Generates Consumer Key + Consumer Secret (copy immediately).",
                "Setup → Users/Roles → Access Tokens → New. Pick the integration record + a user. Copy Token ID + Token Secret (shown once).",
                "Account ID is in the URL when logged in (e.g. TSTDRV123456 for sandbox).",
            ],
            SignupUrl: "https://www.netsuite.com/portal/free-trial.shtml"),

        new(
            Provider: "wave",
            Name: "Wave",
            Description: "Free small-business accounting.",
            Icon: "waves",
            Category: "accounting",
            FieldKeys: ["wave.mode", "wave.client-id", "wave.client-secret"],
            IsConfiguredCheckKey: "wave.client-id",
            LogoUrl: "https://logo.clearbit.com/waveapps.com",
            SetupSteps:
            [
                "Free Wave account at waveapps.com → developer.waveapps.com → Manage Applications → Create Application.",
                "Enter name, description, Redirect URI (HTTPS required). Copy Client ID + Client Secret.",
                "Wave uses GraphQL — single endpoint gql.waveapps.com/graphql/public.",
                "End-users connecting via API need a Wave Pro Plan (as of May 2025). Devs can use Full Access Tokens for personal testing.",
            ],
            SignupUrl: "https://developer.waveapps.com"),

        new(
            Provider: "zoho",
            Name: "Zoho Books",
            Description: "Zoho Books accounting and invoicing.",
            Icon: "menu_book",
            Category: "accounting",
            FieldKeys: ["zoho.mode", "zoho.client-id", "zoho.client-secret", "zoho.organization-id"],
            IsConfiguredCheckKey: "zoho.client-id",
            LogoUrl: "https://logo.clearbit.com/zoho.com",
            SetupSteps:
            [
                "Free Zoho account at zoho.com/signup → accounts.zoho.com/developerconsole → Add Client ID → Server-Based Applications.",
                "Enter name, website URL, Redirect URI. Copy Client ID + Client Secret.",
                "Sandbox: Zoho Books → Settings → Developer Space → Sandbox to create an isolated org.",
                "Organization ID is shown in Manage Organizations or via GET /organizations.",
            ],
            SignupUrl: "https://api-console.zoho.com/"),
    ];
}
