namespace ErpOne.Domain.Entities;

/// <summary>Metode penilaian HPP. Tahap 1 hanya MovingAverage yang didukung.</summary>
public enum CostingMethod
{
    MovingAverage,
    StandardCost,
    AveragePerWarehouse,
    Fifo
}
