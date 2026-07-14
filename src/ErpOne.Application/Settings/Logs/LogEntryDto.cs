namespace ErpOne.Application.Logs;

public record LogEntryDto(
    int Id,
    DateTime TimeStamp,
    string Level,
    string Message,
    string? Exception);
