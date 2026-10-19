using System.Text.Json.Nodes;
using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public partial class LunchService
{
    private static void OnlyFields(JsonNode p, params string[] fields) => Require(p is JsonObject obj && obj.All(x => fields.Contains(x.Key)), "La operación contiene campos no permitidos.");

    private async Task<object> SetUserStatus(AppUser actor, JsonNode p)
    {
        Admin(actor, true); OnlyFields(p, "email", "estado");
        var user = await db.Users.Include(x => x.Department).SingleOrDefaultAsync(x => x.Email == Email(Str(p, "email")));
        Require(user != null, "Usuario fuera de la regional o inexistente.");
        var state = Str(p, "estado"); Require(state is "ACTIVO" or "INACTIVO" or "PENDIENTE", "Estado inválido.");
        if (state == "ACTIVO") Require(user!.Department is { Status: "ACTIVO" }, "Se requiere un departamento activo.");
        await ProtectLastAdmin(user!, user!.Role, state);
        var previous = user.Status; user.Status = state; await SyncDepartmentAdmins();
        if (state == "ACTIVO" && previous != state && !user.RegionalBlocked) Queue(user.Email, "Acceso aprobado", "Tu cuenta de Almuerzo fue habilitada. Ya puedes iniciar sesión con tu cuenta institucional.");
        return Ok();
    }

    private async Task<object> SetRegionalBlock(AppUser actor, JsonNode p)
    {
        ManageRegion(actor); OnlyFields(p, "email", "blocked");
        Require(p["blocked"] is JsonValue value && value.TryGetValue<bool>(out _), "Indica un bloqueo válido.");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == Email(Str(p, "email")));
        Require(user != null && user.Email != actor.Email && user.Role is Roles.User or Roles.DepartmentAdmin, "Solo puedes gestionar bloqueos de colaboradores y responsables de tu regional.");
        var blocked = p["blocked"]!.GetValue<bool>();
        if (user!.RegionalBlocked == blocked) return Ok();
        user.RegionalBlocked = blocked;
        db.Audit.Add(new AuditEvent { Actor = actor.Email, Action = blocked ? "RegionalAccessBlocked" : "RegionalAccessRestored", EntityId = user.Email });
        await SyncDepartmentAdmins();
        return new { ok = true, regionalBlocked = blocked };
    }
}
