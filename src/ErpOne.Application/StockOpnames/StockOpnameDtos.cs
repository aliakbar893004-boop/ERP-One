using ErpOne.Application.Approvals;

namespace ErpOne.Application.StockOpnames;

public record CreateStockOpnameRequest(DateTime OpnameDate, int WarehouseId, string? Notes);

public record StockOpnameCountInput(int LineId, int PhysicalQty);

public record UpdateStockOpnameRequest(DateTime OpnameDate, string? Notes, IReadOnlyList<StockOpnameCountInput> Counts);

public record StockOpnameLineDto(int Id, int ProductVariantId, string Sku, string ProductName,
    int SystemQty, int PhysicalQty, int Variance, int OnHandNow);

public record StockOpnameDto(int Id, string OpnameNumber, DateTime OpnameDate,
    int WarehouseId, string WarehouseName, string? Notes, string Status, string? RejectionNote,
    string? CreatedBy, IReadOnlyList<StockOpnameLineDto> Lines, IReadOnlyList<ApprovalStepDto> ApprovalSteps);

public record StockOpnameListItemDto(int Id, string OpnameNumber, DateTime OpnameDate,
    string WarehouseName, int LineCount, int TotalVariance, string Status);
