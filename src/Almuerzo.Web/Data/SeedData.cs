using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        var defaults = new Dictionary<string, string> {
            ["HORA_ENVIO"] = "15:00", ["MINUTOS_PREV_CIERRE"] = "30", ["HORA_RECORDATORIO"] = "13:00",
            ["ADMIN_EMAILS"] = "", ["RESPONSIBLES_EMAILS_JSON"] = "[]", ["MAIL_SENDER_NAME"] = "Comedor Institucional",
            ["APP_TITLE"] = "Solicitud de Almuerzo", ["APP_URL"] = "", ["TEST_EMAIL_MODE"] = "TRUE", ["TEST_EMAIL_DEST"] = "",
            ["FOOTER_TEXT"] = "Dirección de Innovación © 2025", ["FAVICON_ASSET_ID"] = "", ["FOOTER_STAMP_ASSET_ID"] = "",
            ["MEAL_PRICE_CURRENT"] = "57", ["MEAL_PRICE_HISTORY_JSON"] = "[{\"from\":\"1900-01-01\",\"price\":57}]",
            ["ANNOUNCEMENT_ENABLED"] = "FALSE", ["ANNOUNCEMENT_ID"] = "", ["ANNOUNCEMENT_EXPIRES_ON"] = "",
            ["ANNOUNCEMENT_MAX_DISMISS"] = "3", ["ANNOUNCEMENT_PAYLOAD_JSON"] = "{\"slides\":[]}",
            ["PROVIDER_NAME"] = "Proveedor de Alimentos", ["PROVIDER_PERIOD_ID"] = "PROV_INITIAL", ["PROVIDER_PERIOD_START"] = ""
        };
        var existing = await db.Settings.Select(x => x.Key).ToListAsync();
        db.Settings.AddRange(defaults.Where(x => !existing.Contains(x.Key)).Select(x => new Setting { Key = x.Key, Value = x.Value }));
        if (!await db.Categories.AnyAsync())
        {
            var ids = new[] { "Arroces", "Granos", "Carnes", "Ensaladas", "Viveres", "Vegetariana", "Caldo", "Opcion_Rapida", "Frituritas" };
            var allowed = new[] { "Granos, Carnes, Ensaladas, Frituritas", "Arroces, Carnes, Ensaladas, Frituritas", "Arroces, Granos, Ensaladas, Viveres, Frituritas", "Arroces, Granos, Carnes, Viveres, Frituritas", "Carnes, Ensaladas, Frituritas", "", "", "", "Arroces, Granos, Carnes, Ensaladas, Viveres" };
            var aliases = new[] { "", "Legumbres, Habichuelas", "Proteinas, Carnes y Pescados", "Guarnición, Guarnición / Ensalada", "Víveres, Tuberculos", "Vegetariano, Veggie, Menú Vegetariano", "Caldos, Sopas, Sopa", "Opción Rápida, Rapida, Sandwich, Dieta", "Frituras, Fritos, Snack" };
            db.Categories.AddRange(ids.Select((id, i) => new MenuCategory { Id = id, Name = id.Replace('_', ' '), SortOrder = (i + 1) * 10, SelectionType = id == "Caldo" ? "MULTIPLE" : "UNICA", Combinable = i is not (5 or 6 or 7), CombinableWith = allowed[i], Aliases = aliases[i] }));
        }
        await db.SaveChangesAsync();
    }
}
