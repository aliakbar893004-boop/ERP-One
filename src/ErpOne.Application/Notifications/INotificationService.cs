namespace ErpOne.Application.Notifications;

public interface INotificationService
{
    /// <summary>Compute actionable notifications for a user. roles = the user's role names;
    /// hasPermission(permKey) gates non-approval groups; asOf drives the due-soon window.</summary>
    Task<NotificationSummaryDto> GetForUserAsync(string userName, IReadOnlyCollection<string> roles,
        Func<string, bool> hasPermission, DateTime asOf, CancellationToken ct = default);
}
