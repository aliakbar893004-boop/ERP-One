using ErpOne.Application.Approvals;

namespace ErpOne.Application.StockTransfers;

public record StockTransferLineInput(int ProductVariantId, int Quantity);

public record CreateStockTransferRequest(DateTime TransferDate, int SourceWarehouseId, int DestinationWarehouseId,
    string? Notes, IReadOnlyList<StockTransferLineInput> Lines);

public record StockTransferLineDto(int Id, int ProductVariantId, string Sku, string ProductName, int Quantity, int OnHandSource);

public record StockTransferDto(int Id, string TransferNumber, DateTime TransferDate,
    int SourceWarehouseId, string SourceWarehouseName, int DestinationWarehouseId, string DestinationWarehouseName,
    string? Notes, string Status, string? RejectionNote, string? CreatedBy, IReadOnlyList<StockTransferLineDto> Lines,
    IReadOnlyList<ApprovalStepDto> ApprovalSteps);

public record StockTransferListItemDto(int Id, string TransferNumber, DateTime TransferDate,
    string SourceWarehouseName, string DestinationWarehouseName, int TotalQuantity, string Status);
