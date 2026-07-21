namespace ErpOne.Application.Accounting;

public interface IPostingConfigurationService
{
    Task<PostingConfigurationDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(UpdatePostingConfigurationRequest request, CancellationToken ct = default);
}
