namespace ErpOne.Application.CompanySettings;

public interface ICompanySettingService
{
    /// <summary>Ambil profil perusahaan; baris tunggal (Id=1) di-seed via HasData.</summary>
    Task<CompanySettingDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(UpdateCompanySettingRequest request, CancellationToken ct = default);
}
