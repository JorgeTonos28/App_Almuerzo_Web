using System.Security.Claims;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Controllers;

[Authorize(Policy = "Enabled"), EnableRateLimiting("rpc"), Route("api/rpc")]
public class RpcController(LunchService lunch, AppDbContext db) : Controller
{
    [HttpPost, RequestSizeLimit(256 * 1024)] public async Task<IActionResult> Invoke([FromBody] JsonObject request)
    {
        if (!ModelState.IsValid) return BadRequest(new { ok = false, msg = "Solicitud inválida." });
        await using var tx = await db.Database.BeginTransactionAsync();
        try {
            var method = LunchService.Str(request, "method"); var args = request["args"] as JsonArray ?? new();
            var actor = await lunch.Actor(User.FindFirstValue(ClaimTypes.Email)!);
            var result = await lunch.Execute(method, args, actor);
            if (!method.StartsWith("apiGet") && method is not ("apiHeartbeat" or "apiCheckUserStatus")) {
                var target = args.FirstOrDefault(); var id = target is JsonObject ? LunchService.Str(target, "email", LunchService.Str(target, "id", LunchService.Str(target, "date"))) : target?.ToString() ?? "";
                db.Audit.Add(new AuditEvent { Actor = actor.Email, Action = method, EntityId = id[..Math.Min(254, id.Length)] });
            }
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Json(result);
        } catch (BusinessException ex) { return Json(new { ok = false, msg = ex.Message }); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException or ArgumentException) { return BadRequest(new { ok = false, msg = "Formato de solicitud inválido." }); }
        catch (DbUpdateException) { return Conflict(new { ok = false, msg = "Los datos cambiaron o entran en conflicto. Recarga la vista e intenta nuevamente." }); }
    }
}
