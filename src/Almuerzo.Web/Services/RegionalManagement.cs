using System.Globalization;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public static class RegionManagement
{
    public static async Task<object> Save(AppDbContext db, JsonNode payload)
    {
        var id = LunchService.Str(payload, "id");
        var region = id.Length == 0 ? new Region() : await db.Regions.FindAsync(id) ?? throw new BusinessException("Regional inexistente.");
        var name = LunchService.Bounded(LunchService.Str(payload, "nombre"), 150, "Nombre");
        if (id.Length > 0 && LunchService.Str(payload, "directoryCode") != (region.DirectoryCode ?? "") && (region.DirectoryManaged || await db.Users.IgnoreQueryFilters().AnyAsync(x => x.RegionId == region.Id && x.DirectoryManaged)))
            throw new BusinessException("El código de una regional vinculada requiere reconciliación con TI.");
        var status = LunchService.Str(payload, "estado", "ACTIVO");
        LunchService.Require(status is "ACTIVO" or "INACTIVO", "Estado inválido.");
        LunchService.Require(!await db.Regions.AnyAsync(x => x.Name == name && x.Id != region.Id), "Nombre de regional duplicado.");
        if (status == "INACTIVO") LunchService.Require(!await db.Users.IgnoreQueryFilters().AnyAsync(x => x.RegionId == region.Id && x.Role == Roles.GeneralAdmin && x.Status == "ACTIVO"), "Mueve primero los administradores generales de esta regional.");
        region.Name = name; region.Status = status;
        region.DirectoryCode = string.IsNullOrWhiteSpace(LunchService.Str(payload, "directoryCode")) ? null : LunchService.Bounded(LunchService.Str(payload, "directoryCode"), 64, "Código AD");
        if (id.Length == 0) db.Regions.Add(region);
        await db.SaveChangesAsync();
        var originalRegion = db.RegionId;
        try { db.RegionId = region.Id; await SeedData.InitializeAsync(db); }
        finally { db.RegionId = originalRegion; }
        return new { ok = true, id = region.Id };
    }
}

public partial class LunchService
{
    private async Task<object> ChangeProvider(AppUser actor, JsonNode p)
    {
        Admin(actor, true);
        var name = Bounded(Str(p, "providerName"), 150, "Proveedor");
        Require(p["price"] != null && decimal.TryParse(Str(p, "price"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _), "Debes confirmar el precio del nuevo proveedor.");
        var price = decimal.Parse(Str(p, "price"), CultureInfo.InvariantCulture);
        Require(price is > 0 and <= 100000 && decimal.Round(price, 2) == price, "Precio inválido: usa un importe positivo con hasta dos decimales.");
        var cfg = await Config();
        var history = JsonNode.Parse(cfg["MEAL_PRICE_HISTORY_JSON"]) as JsonArray ?? new();
        foreach (var old in history.Where(x => Str(x, "from") == rules.Today).ToArray()) history.Remove(old);
        var period = Guid.NewGuid().ToString();
        history.Add(new JsonObject { ["from"] = rules.Today, ["price"] = price, ["provider"] = name, ["periodId"] = period });
        await Set("MEAL_PRICE_HISTORY_JSON", history.ToJsonString());
        await Set("MEAL_PRICE_CURRENT", price.ToString(CultureInfo.InvariantCulture));
        await Set("PROVIDER_NAME", name); await Set("PROVIDER_PERIOD_ID", period); await Set("PROVIDER_PERIOD_START", rules.Today);
        return new { ok = true, providerInfo = Provider(await CurrentConfig()), mealPricing = new { current = price, history } };
    }
}
