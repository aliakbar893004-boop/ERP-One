namespace ErpOne.Application.Accounting;

public record PostingConfigurationDto(
    int? ArAccountId, int? ApAccountId, int? InventoryAccountId, int? GrIrAccountId,
    int? SalesAccountId, int? CogsAccountId, int? InputTaxAccountId, int? OutputTaxAccountId, int? PosCashAccountId);

public record UpdatePostingConfigurationRequest(
    int? ArAccountId, int? ApAccountId, int? InventoryAccountId, int? GrIrAccountId,
    int? SalesAccountId, int? CogsAccountId, int? InputTaxAccountId, int? OutputTaxAccountId, int? PosCashAccountId);
