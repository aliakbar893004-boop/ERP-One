namespace ErpOne.Application.PriceLists;

public record PriceListDto(int Id, string Code, string Name, string? Description, bool IsActive,
    int LineCount, DateTime CreatedAt, string? CreatedBy);

public record PriceListLineDto(int Id, int ProductVariantId, string VariantSku, string ProductName,
    int MinQty, decimal UnitPrice);

public record PriceListDetailDto(int Id, string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<PriceListLineDto> Lines);

public record PriceListVariantOptionDto(int VariantId, string Sku, string ProductName, decimal Price);

public record PriceListLineRequest(int ProductVariantId, int MinQty, decimal UnitPrice);

public record CreatePriceListRequest(string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<PriceListLineRequest> Lines);

public record UpdatePriceListRequest(string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<PriceListLineRequest> Lines);
