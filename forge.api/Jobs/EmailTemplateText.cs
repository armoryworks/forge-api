namespace Forge.Api.Jobs;

public record EmailTemplateText(
    string Greeting,
    string OpenCompany,
    string ViewRecord,
    string SentBy,
    string ManagePreferences)
{
    public static readonly EmailTemplateText English = new(
        "Hi {0},", "Open {0}", "View in {0}", "Sent by {0}", "Manage preferences");

    public static readonly EmailTemplateText Spanish = new(
        "Hola, {0}:", "Abrir {0}", "Ver en {0}", "Enviado por {0}", "Administrar preferencias");

    public static EmailTemplateText For(string? language) =>
        string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ? Spanish : English;
}
