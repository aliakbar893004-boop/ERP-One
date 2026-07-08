using ClosedXML.Excel;

namespace ErpOne.Web.Endpoints;

public static class ProductImportEndpoints
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapProductImportEndpoints(this IEndpointRouteBuilder app)
    {
        // Template Excel untuk impor produk (di-generate on the fly).
        app.MapGet("/master/products/import-template", () =>
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Products");

            string[] headers =
                ["CategoryCode", "Name", "Description", "Price", "DiscountPrice", "Stock", "Weight", "Dimensions", "Status"];
            for (var i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var head = ws.Row(1);
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");

            // Contoh baris
            object[] r2 = ["KAT-01", "Contoh Produk A", "Deskripsi produk A", 150000, 120000, 25, 500, "20 x 15 x 5 cm", "Aktif"];
            object[] r3 = ["KAT-01", "Contoh Produk B", "", 99000, "", 10, 250, "15 x 10 x 3 cm", "Aktif"];
            for (var i = 0; i < r2.Length; i++) ws.Cell(2, i + 1).Value = XLCellValue.FromObject(r2[i]);
            for (var i = 0; i < r3.Length; i++) ws.Cell(3, i + 1).Value = XLCellValue.FromObject(r3[i]);

            // Lebar kolom tetap (hindari AdjustToContents yang butuh font sistem).
            int[] widths = [14, 26, 32, 12, 14, 8, 10, 18, 10];
            for (var i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return Results.File(ms.ToArray(), XlsxContentType, "product-import-template.xlsx");
        }).RequireAuthorization("master.products.create");

        return app;
    }
}
