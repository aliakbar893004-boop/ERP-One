namespace ErpOne.Application.Notifications;

/// <summary>One actionable group shown in the notification popover.</summary>
public record NotificationGroupDto(string Key, string Label, string Icon, int Count, string Url, string Severity);

public record NotificationSummaryDto(int TotalCount, IReadOnlyList<NotificationGroupDto> Groups);
