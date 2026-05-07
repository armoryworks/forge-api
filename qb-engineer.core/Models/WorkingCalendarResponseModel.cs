namespace QBEngineer.Core.Models;

public record WorkingCalendarResponseModel(
    int Id,
    string Name,
    string TimeZone,
    int WorkingDaysMask,
    bool IsDefault,
    bool IsActive,
    List<HolidayResponseModel> Holidays,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record HolidayResponseModel(
    int Id,
    DateOnly Date,
    string Name,
    DateOnly? ObservedDate,
    bool IsRecurring);

public record WorkingCalendarRequestModel(
    string Name,
    string TimeZone,
    int WorkingDaysMask,
    bool IsActive);

public record HolidayRequestModel(
    DateOnly Date,
    string Name,
    DateOnly? ObservedDate,
    bool IsRecurring);
