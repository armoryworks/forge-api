namespace Forge.Core.Models;

public record CompanyProfileResponseModel(
    string Name,
    string Phone,
    string Email,
    string Ein,
    string Website,
    string TimeZone = "",
    int BooksAwaitingTimeZone = 0);
