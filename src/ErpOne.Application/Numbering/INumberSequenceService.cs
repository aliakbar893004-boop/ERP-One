namespace ErpOne.Application.Numbering;

public interface INumberSequenceService
{
    Task<IReadOnlyList<NumberSequenceDto>> GetAllAsync(CancellationToken ct = default);
    Task<NumberSequenceDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, UpdateNumberSequenceRequest request, CancellationToken ct = default);
}
