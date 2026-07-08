# Task 2 Report: Warehouse Master

**Status:** DONE

**Date:** 2026-06-18

---

## Files Created

| File | Description |
|------|-------------|
| `src/MyApp.Domain/Entities/Warehouse.cs` | Entity with Code, Name, Address?, IsActive, IsDefault, plus SetAsDefault()/ClearDefault() methods |
| `src/MyApp.Application/Warehouses/IWarehouseService.cs` | Interface including GetDefaultAsync() |
| `src/MyApp.Application/Warehouses/WarehouseDtos.cs` | WarehouseDto, CreateWarehouseRequest, UpdateWarehouseRequest records |
| `src/MyApp.Application/Warehouses/CreateWarehouseValidator.cs` | CreateWarehouseValidator + UpdateWarehouseValidator (Code/Name/Address) |
| `src/MyApp.Infrastructure/Services/WarehouseService.cs` | Full CRUD + GetDefaultAsync + ClearOtherDefaultsAsync (single-default enforcement) |
| `src/MyApp.Web/Components/Pages/Master/Warehouses/WarehouseIndex.razor` | Index page with Default badge + Active badge columns |
| `src/MyApp.Web/Components/Pages/Master/Warehouses/WarehouseIndex.razor.css` | Styles (identical to BrandIndex) |
| `src/MyApp.Web/Components/Pages/Master/Warehouses/WarehouseForm.razor` | Form with Address textarea, IsActive checkbox (default true), IsDefault checkbox |
| `src/MyApp.Web/Components/Pages/Master/Warehouses/WarehouseForm.razor.css` | Styles (identical to BrandForm) |
| `tests/MyApp.IntegrationTests/WarehouseServiceTests.cs` | Integration test: SettingSecondDefault_UnsetsFirst |

## Files Modified

| File | Change |
|------|--------|
| `src/MyApp.Infrastructure/Persistence/AppDbContext.cs` | Added DbSet<Warehouse> + entity config (Code/Name/Address + unique Code index) |
| `src/MyApp.Infrastructure/DependencyInjection.cs` | Added using + AddScoped<IWarehouseService, WarehouseService>() |
| `src/MyApp.Web/Authorization/AppMenus.cs` | Added master.warehouses resource with CRUD actions |
| `src/MyApp.Web/Components/Layout/NavMenu.razor` | Added AuthorizeView for master.warehouses.index → href master/warehouses |
| `src/MyApp.Web/Components/_Imports.razor` | Added @using MyApp.Application.Warehouses |

---

## Implementation Notes

### Single-Default Enforcement
- `ClearOtherDefaultsAsync(int? exceptId, CancellationToken ct)` helper loads all IsDefault=true rows (excluding the current entity by ID) and calls `ClearDefault()` on each.
- In `CreateAsync`: called after `db.Warehouses.Add(entity)` and before `SaveChangesAsync` when `request.IsDefault == true`. The new entity (state=Added) does NOT appear in the EF DB query, so it won't accidentally clear itself.
- In `UpdateAsync`: called after `entity.Update(...)` and before `SaveChangesAsync`, with `exceptId=id` so the current entity keeps its default flag.

### Validator
- Code: NotEmpty, MaximumLength(20), Matches letters/numbers/dashes (allows WH-MAIN format)
- Name: NotEmpty, MaximumLength(100)
- Address: MaximumLength(300) (optional, no NotEmpty)

### UI
- Index: extra columns `Default` (shows blue "Default" badge if IsDefault) and `Active` (green "Active" / grey "Inactive" badge)
- Form: Address as textarea (3 rows, maxlength=300, char counter), IsActive checkbox (default `true`), IsDefault checkbox (default `false`)

---

## Concerns

None. All spec requirements from Task 2 are fulfilled verbatim. No seed data added (that is Task 6).
