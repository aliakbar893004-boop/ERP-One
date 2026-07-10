using ErpOne.Domain.Common;

namespace ErpOne.Domain.Entities;

/// <summary>Profil perusahaan (baris tunggal, Id = 1). Dipakai struk POS &amp; cetakan.</summary>
public class CompanySetting : AuditableEntity
{
    public int Id { get; private set; }
    public string? CompanyName { get; private set; }
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? TaxId { get; private set; }         // NPWP
    public string? LogoUrl { get; private set; }
    public string? ReceiptHeader { get; private set; }
    public string? ReceiptFooter { get; private set; }

    private CompanySetting() { } // EF Core

    public void Update(string? companyName, string? address, string? phone, string? email,
        string? taxId, string? logoUrl, string? receiptHeader, string? receiptFooter)
    {
        CompanyName   = Trim(companyName);
        Address       = Trim(address);
        Phone         = Trim(phone);
        Email         = Trim(email);
        TaxId         = Trim(taxId);
        LogoUrl       = Trim(logoUrl);
        ReceiptHeader = Trim(receiptHeader);
        ReceiptFooter = Trim(receiptFooter);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
