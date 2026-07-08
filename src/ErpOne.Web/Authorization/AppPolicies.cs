namespace ErpOne.Web.Authorization;

/// <summary>Nama policy otorisasi aplikasi (dipakai di endpoint &amp; komponen Blazor).</summary>
public static class AppPolicies
{
    /// <summary>Hanya anggota grup AD pengelola produk (Authorization:ManagerGroup).</summary>
    public const string ManageProducts = "ManageProducts";
}
