namespace ServiceBooking.API.DTOs.WorkingHours;

public record WorkingHoursDto(
    Guid Id,
    string MasterId,
    Guid CompanyId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsWorking,
    List<BreakDto> Breaks
);

public record BreakDto(Guid Id, TimeOnly StartTime, TimeOnly EndTime);

public record UpsertWorkingHoursDto(
    string MasterId,
    Guid CompanyId,
    DateOnly Date,
    bool IsWorking,
    TimeOnly StartTime,
    TimeOnly EndTime,
    List<UpsertBreakDto> Breaks
);

public record UpsertBreakDto(TimeOnly StartTime, TimeOnly EndTime);
