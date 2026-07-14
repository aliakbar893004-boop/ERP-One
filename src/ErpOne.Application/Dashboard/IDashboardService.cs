namespace ErpOne.Application.Dashboard;

public interface IDashboardService
{
    Task<OperationalDashboardDto> GetAsync(DateTime asOf, CancellationToken ct = default);
}
