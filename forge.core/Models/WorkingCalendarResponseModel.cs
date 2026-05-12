namespace Forge.Core.Models;

public record WorkingCalendarResponseModel(
    int Id,
    string Name,
    string TimeZone,
    int WorkingDaysMask,
    bool IsDefault,
    bool IsActive,
    List<HolidayResponseModel> Holidays,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Shifts effort — calendar-bound shifts. Drives hours-of-operation,
    // capacity for finite MRP, and (future) payroll premium application.
    List<CalendarShiftResponseModel>? Shifts = null,
    // Computed weekly capacity hours (sum across active shifts × occurrences-per-week).
    decimal WeeklyCapacityHours = 0m);

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

/// <summary>
/// Shifts effort — calendar-bound shift surface. Mirrors the persisted
/// fields plus a computed <see cref="EffectiveCapacityHours"/> so the UI
/// doesn't repeat the fallback logic. Mask uses the same 7-bit
/// convention as <see cref="WorkingCalendarResponseModel.WorkingDaysMask"/>:
/// bit 0 = Sunday … bit 6 = Saturday.
/// </summary>
public record CalendarShiftResponseModel(
    int Id,
    int WorkingCalendarId,
    string Name,
    int DaysOfWeekMask,
    TimeOnly StartTime,
    TimeOnly EndTime,
    decimal PremiumMultiplier,
    decimal CapacityHours,
    decimal EffectiveCapacityHours,
    bool IsActive);

public record CalendarShiftRequestModel(
    string Name,
    int DaysOfWeekMask,
    TimeOnly StartTime,
    TimeOnly EndTime,
    decimal PremiumMultiplier,
    decimal CapacityHours,
    bool IsActive);
