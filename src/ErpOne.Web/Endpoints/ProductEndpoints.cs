using ErpOne.Application.Products;
using ErpOne.Web.Authorization;

namespace ErpOne.Web.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products")
            .RequireAuthorization();

        group.MapGet("/", async (IProductService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapGet("/{id:int}", async (int id, IProductService service, CancellationToken ct) =>
            await service.GetByIdAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());

        group.MapPost("/", async (CreateProductRequest request, IProductService service, CancellationToken ct) =>
        {
            var dto = await service.CreateAsync(request, ct); // ValidationException → 400 via handler
            return Results.Created($"/api/products/{dto.Id}", dto);
        }).RequireAuthorization(AppPolicies.ManageProducts);

        group.MapPut("/{id:int}", async (int id, UpdateProductRequest request, IProductService service, CancellationToken ct) =>
            (await service.UpdateAsync(id, request, ct)).Found ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AppPolicies.ManageProducts);

        group.MapDelete("/{id:int}", async (int id, IProductService service, CancellationToken ct) =>
            await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AppPolicies.ManageProducts);

        return app;
    }
}
