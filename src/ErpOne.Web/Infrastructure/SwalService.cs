using Microsoft.JSInterop;

namespace ErpOne.Web.Infrastructure;

/// <summary>Pembungkus SweetAlert2 (window.appSwal) untuk dialog &amp; toast dari komponen Blazor.</summary>
public sealed class SwalService(IJSRuntime js)
{
    /// <summary>Tampilkan dialog konfirmasi; true bila pengguna menekan tombol konfirmasi.</summary>
    public ValueTask<bool> ConfirmAsync(string title, string text, string confirmText = "Yes, delete") =>
        js.InvokeAsync<bool>("appSwal.confirm", title, text, confirmText);

    /// <summary>Tampilkan toast singkat (icon: success | error | warning | info).</summary>
    public ValueTask ToastAsync(string icon, string title) =>
        js.InvokeVoidAsync("appSwal.toast", icon, title);

    /// <summary>Tampilkan dialog info (satu tombol OK); menunggu sampai pengguna menutupnya.</summary>
    public ValueTask AlertAsync(string title, string text) =>
        js.InvokeVoidAsync("appSwal.alert", title, text);
}
