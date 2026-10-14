namespace SutariyaTimeClock;

internal sealed record Employee(int Id, string Name);

internal sealed record PunchHistory(
    DateTimeOffset? ClockIn,
    DateTimeOffset? ClockOut,
    double? RawHours,
    double? RoundedHours);

internal sealed record EmployeeStatus(
    bool ClockedIn,
    int PendingCount,
    IReadOnlyList<PunchHistory> History);

internal sealed record PunchResult(string Message);
