using ErpOne.Domain.Entities;

namespace ErpOne.Application.Costing;

/// <summary>Metode HPP aktif + apakah terkunci (sudah ada StockMovement).</summary>
public record CostingSettingDto(CostingMethod Method, bool Locked);
