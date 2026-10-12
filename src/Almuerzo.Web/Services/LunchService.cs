using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

// The legacy view contract is retained; all identity, scope and integrity checks live here.
public class LunchService(AppDbContext db, BusinessRules rules, ReportService reports)
{
    public static JsonObject Obj(string? raw) => JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw) as JsonObject ?? new();
    public static string Str(JsonNode? node, string key, string fallback = "") => node?[key]?.ToString() ?? fallback;
    public static int Num(JsonNode? node, string key, int fallback = 0) => int.TryParse(Str(node, key), out var v) ? v : fallback;
    public static string[] List(JsonNode? node) => node is JsonArray a ? a.Select(x => x?.ToString() ?? "").ToArray() : Split(node?.ToString() ?? "");
    public static string[] Split(string value) => value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    public static object Ok() => new { ok = true };
    public static void Require(bool condition, string message) { if (!condition) throw new BusinessException(message); }
    public static string Bounded(string value, int max, string label, bool required = true)
    {
        value = value.Trim(); Require((!required || value.Length > 0) && value.Length <= max, $"{label}: valor obligatorio o longitud inválida (máximo {max})."); return value;
    }
    public static string Email(string value)
    {
        value = value.Trim().ToLowerInvariant();
        Require(Regex.IsMatch(value, @"^[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@infotep\.gob\.do$"), "Se requiere un correo institucional válido."); return value;
    }
    public static void Admin(AppUser actor, bool general = false) => Require(general ? actor.Role == Roles.GeneralAdmin : actor.Role is Roles.GeneralAdmin or Roles.DepartmentAdmin, "Permiso denegado.");
    public static bool Enabled(AppUser? u) => u is { Status: "ACTIVO", Department: { Status: "ACTIVO" } } && Roles.All.Contains(u.Role);
    public async Task<AppUser> Actor(string email)
    {
        var user = await db.Users.Include(x => x.Department).SingleOrDefaultAsync(x => x.Email == email);
        Require(Enabled(user), "Acceso denegado. Tu cuenta requiere habilitación, rol y departamento activos."); return user!;
    }
    public async Task<AppUser> Target(AppUser actor, string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Equals(actor.Email, StringComparison.OrdinalIgnoreCase)) return actor;
        Admin(actor);
        var u = await db.Users.Include(x => x.Department).SingleOrDefaultAsync(x => x.Email == email.ToLowerInvariant());
        Require(Enabled(u) && (actor.Role == Roles.GeneralAdmin || u!.DepartmentId == actor.DepartmentId), "Usuario fuera de tu ámbito o inactivo."); return u!;
    }
    public async Task<Dictionary<string, string>> Config() => await db.Settings.ToDictionaryAsync(x => x.Key, x => x.Value);
    public async Task<HashSet<string>> Holidays() => (await db.Holidays.Select(x => x.Date).ToListAsync()).ToHashSet();
    private async Task Set(string key, string value)
    {
        var s = await db.Settings.FindAsync(key); Require(s != null, "Clave de configuración desconocida."); s!.Value = value;
    }
    private static int CloseMinutes(Dictionary<string, string> config)
    {
        var raw = config.GetValueOrDefault("MINUTOS_PREV_CIERRE", "30");
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return Math.Clamp(integer, 0, 300);
        // Excel/Sheets imports can represent an integer setting as a decimal (for example, "30.0").
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue) && decimalValue is >= 0 and <= 300 && decimal.Truncate(decimalValue) == decimalValue)
            return Math.Clamp((int)decimalValue, 0, 300);
        return 30;
    }
    private bool Open(string date, HashSet<string> holidays, Dictionary<string, string> config) => rules.OrderingOpen(date, holidays, config.GetValueOrDefault("HORA_ENVIO", "15:00"), CloseMinutes(config));
    public static object UserDto(AppUser u) => new { email = u.Email, nombre = u.Name, departamentoId = u.DepartmentId, departamento = u.Department?.Name ?? "", rol = u.Role, estado = u.Status, codigo = u.EmployeeCode ?? "", directoryAccount = u.DirectoryAccount ?? "" };
    public static object CategoryDto(MenuCategory c) => new { id = c.Id, nombre = c.Name, orden = c.SortOrder, estado = c.Status, aliases = Split(c.Aliases), es_combinable = c.Combinable, combinable_con = Split(c.CombinableWith), tipo_seleccion = c.SelectionType };
    public static object OrderDto(Order o) => new { id = o.Id, resumen = BusinessRules.Text(o.Summary), detalle = Obj(o.DetailJson), estado = o.Status };
    public static object RatingDto(Rating r) => new { id = r.Id, orderId = r.OrderId, date = r.Scope, periodId = r.Scope, providerName = r.ProviderName, puntuacion = r.Score, comentario = r.Comment, platos = r.Dishes, version = r.Version, updatedAt = r.UpdatedAt };
    public static object Provider(Dictionary<string, string> c) => new { name = c.GetValueOrDefault("PROVIDER_NAME", "Proveedor de Alimentos"), periodId = c.GetValueOrDefault("PROVIDER_PERIOD_ID", "PROV_INITIAL"), periodStart = c.GetValueOrDefault("PROVIDER_PERIOD_START", "") };
    public static object Announcement(Dictionary<string, string> c) => new {
        enabled = c.GetValueOrDefault("ANNOUNCEMENT_ENABLED") == "TRUE", id = c.GetValueOrDefault("ANNOUNCEMENT_ID"), expiresOn = c.GetValueOrDefault("ANNOUNCEMENT_EXPIRES_ON"),
        maxDismiss = int.TryParse(c.GetValueOrDefault("ANNOUNCEMENT_MAX_DISMISS"), out var n) ? n : 3,
        slides = Obj(c.GetValueOrDefault("ANNOUNCEMENT_PAYLOAD_JSON")) ["slides"] ?? new JsonArray()
    };
    public JsonObject Game(AppUser u)
    {
        var saved = Obj(u.GameJson); var month = rules.Today[..7]; var sameMonth = Str(saved, "monthKey") == month; var sameDay = Str(saved, "dayKey") == rules.Today;
        var used = sameDay ? Math.Clamp(Num(saved, "usedSeconds"), 0, 900) : 0;
        return new JsonObject {
            ["monthKey"] = month, ["monthLabel"] = rules.Now.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-DO")), ["dayKey"] = rules.Today,
            ["score"] = sameMonth ? Num(saved, "score") : 0, ["hits"] = sameMonth ? Num(saved, "hits") : 0, ["misses"] = sameMonth ? Num(saved, "misses") : 0,
            ["usedSeconds"] = used, ["remainingSeconds"] = 900 - used, ["dailyLimitSeconds"] = 900, ["streak"] = sameDay ? Num(saved, "streak") : 0,
            ["bestStreak"] = sameMonth ? Num(saved, "bestStreak") : 0, ["penaltySeconds"] = sameDay ? Num(saved, "penaltySeconds") : 0
        };
    }
    public async Task<object> Init(AppUser actor, string? requested = null, string? targetEmail = null)
    {
        var target = await Target(actor, targetEmail); var cfg = await Config(); var holidays = await Holidays();
        var future = await db.Menu.AsNoTracking().Where(x => string.Compare(x.Date, rules.Today) > 0 && x.Enabled).OrderBy(x => x.Date).ToListAsync();
        var openDates = future.Select(x => x.Date).Distinct().Where(d => Open(d, holidays, cfg)).ToArray();
        var date = openDates.Contains(requested) ? requested : openDates.FirstOrDefault();
        object MenuOf(string d) => future.Where(x => x.Date == d).GroupBy(x => x.CategoryId).ToDictionary(g => g.Key, g => g.Select(x => new { id = x.Id, plato = BusinessRules.Text(x.Dish), desc = BusinessRules.Text(x.Description) }).ToArray());
        var orders = await db.Orders.AsNoTracking().Where(x => x.Email == target.Email && x.Status == "ACTIVO").ToListAsync();
        var mealRatings = await db.Ratings.AsNoTracking().Where(x => x.Email == target.Email && x.Kind == "MEAL").ToListAsync();
        var period = cfg["PROVIDER_PERIOD_ID"];
        var providerRating = await db.Ratings.AsNoTracking().SingleOrDefaultAsync(x => x.Email == target.Email && x.Kind == "PROVIDER" && x.Scope == period);
        var deptUsers = actor.Role == Roles.DepartmentAdmin ? await db.Users.AsNoTracking().Where(x => x.DepartmentId == actor.DepartmentId && x.Status == "ACTIVO" && x.Email != actor.Email).Select(x => new { email = x.Email, nombre = x.Name }).ToArrayAsync() : [];
        return new {
            ok = true, appVersion = "0.1.3", nextBusinessDay = rules.NextBusinessDay(holidays).ToString("yyyy-MM-dd"), user = UserDto(target), activeUser = UserDto(actor), deptUsers,
            userPrefs = Obj(target.PreferencesJson), currentDate = date, dates = openDates.Select(d => new { value = d, label = BusinessRules.Date(d).ToString("ddd dd MMM", CultureInfo.GetCultureInfo("es-DO")) }),
            menu = date != null ? MenuOf(date) : new Dictionary<string, object>(), allMenus = openDates.ToDictionary(d => d, MenuOf), allOrders = orders.ToDictionary(x => x.Date, OrderDto),
            myOrder = orders.Where(x => x.Date == date).Select(OrderDto).FirstOrDefault(), adminData = actor.Role == Roles.User ? null : await Stats(actor, date),
            mealPricing = new { current = decimal.Parse(cfg["MEAL_PRICE_CURRENT"], CultureInfo.InvariantCulture), history = JsonNode.Parse(cfg["MEAL_PRICE_HISTORY_JSON"]) },
            announcementConfig = Announcement(cfg), providerInfo = Provider(cfg), userMealRatings = mealRatings.ToDictionary(x => x.Scope, RatingDto), userProviderRating = providerRating == null ? null : RatingDto(providerRating),
            serverHour = rules.Now.Hour, serverTime = rules.Now.ToString("O"), todayYmd = rules.Today, chefGame = Game(target), deptMap = await db.Departments.ToDictionaryAsync(x => x.Id, x => x.Name),
            menuCategories = (await db.Categories.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync()).Select(CategoryDto)
        };
    }
    private async Task<object> Stats(AppUser actor, string? date)
    {
        var rows = await db.Orders.AsNoTracking().Where(x => x.Date == date && x.Status == "ACTIVO" && (actor.Role == Roles.GeneralAdmin || x.DepartmentId == actor.DepartmentId)).ToListAsync();
        var depts = await db.Departments.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        return new { total = rows.Count, byUser = rows.Select(x => new { nombre = x.Name, pedido = BusinessRules.Text(x.Summary), depto = depts.GetValueOrDefault(x.DepartmentId, x.DepartmentId) }) };
    }
    private async Task<object> AdminData(AppUser actor)
    {
        Admin(actor); var cfg = actor.Role == Roles.GeneralAdmin ? (await Config()).Where(x => !BrandingService.Keys.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value) : new(); var cutoff = BusinessRules.Date(rules.Today).AddDays(-60).ToString("yyyy-MM-dd");
        var users = await db.Users.AsNoTracking().Include(x => x.Department).Where(x => actor.Role == Roles.GeneralAdmin || x.DepartmentId == actor.DepartmentId).ToListAsync();
        var departments = await db.Departments.AsNoTracking().ToListAsync(); var map = departments.ToDictionary(x => x.Id, x => x.Name);
        var orders = await db.Orders.AsNoTracking().Where(x => x.Status == "ACTIVO" && string.Compare(x.Date, cutoff) >= 0 && (actor.Role == Roles.GeneralAdmin || x.DepartmentId == actor.DepartmentId)).ToListAsync();
        return new { ok = true, rol = actor.Role, dept = actor.DepartmentId, users = users.Select(UserDto),
            orders = orders.Select(x => new { id = x.Id, date = x.Date, email = x.Email, nombre = x.Name, deptId = x.DepartmentId, dept = map.GetValueOrDefault(x.DepartmentId, x.DepartmentId), resumen = x.Summary, estado = x.Status, creado_por = x.CreatedBy }),
            departments = departments.Where(x => actor.Role == Roles.GeneralAdmin || x.Id == actor.DepartmentId).Select(x => new { id = x.Id, nombre = x.Name, admins = x.Admins, estado = x.Status }),
            config = cfg, configKeys = cfg.Keys, configList = (await db.Settings.AsNoTracking().ToListAsync()).Where(x => cfg.ContainsKey(x.Key)).Select(x => new { key = x.Key, value = x.Value, desc = x.Description }),
            holidays = actor.Role == Roles.GeneralAdmin ? (await db.Holidays.ToListAsync()).Select(x => new { date = x.Date, desc = x.Reason }) : [],
            menuCategories = actor.Role == Roles.GeneralAdmin ? (await db.Categories.OrderBy(x => x.SortOrder).ToListAsync()).Select(CategoryDto) : []
        };
    }
    public async Task<object> Execute(string method, JsonArray args, AppUser actor)
    {
        JsonNode? P(int n = 0) => n < args.Count ? args[n] : null;
        string S(int n = 0) => P(n)?.ToString() ?? "";
        var first = S();
        switch (method)
        {
            case "apiGetInitData": case "apiGetDateViewData": return await Init(actor, S(), S(1));
            case "apiCheckUserStatus": return actor.Status;
            case "apiGetAdminData": return await AdminData(actor);
            case "apiSubmitOrder": return await SubmitOrder(actor, P()!);
            case "apiCancelOrder": case "apiAdminCancelOrder":
            {
                var order = await db.Orders.FindAsync(S()); Require(order != null, "Pedido no encontrado.");
                if (method == "apiAdminCancelOrder") { Admin(actor); Require(actor.Role == Roles.GeneralAdmin || actor.DepartmentId == order!.DepartmentId, "Pedido fuera de tu departamento."); }
                else { await Target(actor, order!.Email); Require(Open(order.Date, await Holidays(), await Config()), "Ya pasó la hora de cierre."); }
                order!.Status = "CANCELADO"; order.ModifiedAt = rules.Now.ToString("O"); await db.SaveChangesAsync(); return new { ok = true, date = order.Date, adminData = actor.Role == Roles.User ? null : await Stats(actor, order.Date) };
            }
            case "apiSetUserPreference":
            {
                var u = await Target(actor, S(2)); Require(Regex.IsMatch(S(), @"^[A-Za-z0-9_]{1,80}$"), "Preferencia inválida.");
                Require((P(1)?.ToJsonString().Length ?? 0) < 12000, "Preferencia demasiado extensa."); var prefs = Obj(u.PreferencesJson); prefs[S()] = P(1)?.DeepClone(); u.PreferencesJson = prefs.ToJsonString(); return Ok();
            }
            case "apiDismissAnnouncement":
            {
                var cfg = await Config(); Require(S() == cfg["ANNOUNCEMENT_ID"], "Aviso no vigente."); var prefs = Obj(actor.PreferencesJson); var counts = prefs["announcements"] as JsonObject ?? new();
                counts[S()] = Math.Min(100, Num(counts, S()) + 1); prefs["announcements"] = counts.DeepClone(); actor.PreferencesJson = prefs.ToJsonString(); return Ok();
            }
            case "apiSaveAnnouncementConfig":
            {
                Admin(actor, true); var p = P()!; Require(p["slides"] is JsonArray slides && slides.Count <= 12, "Máximo 12 pantallas.");
                foreach (var slide in (JsonArray)p["slides"]!) { Bounded(Str(slide, "title"), 150, "Título"); Bounded(Str(slide, "description"), 2000, "Descripción", false); Require(Regex.IsMatch(Str(slide, "icon", "fa-star"), @"^fa-[a-z0-9-]+$"), "Icono inválido."); }
                var id = Str(p, "forceNewId") == "true" || Str(p, "id") == "" ? Guid.NewGuid().ToString() : Str(p, "id"); if (Str(p, "expiresOn").Length > 0) BusinessRules.Date(Str(p, "expiresOn"));
                Require(Num(p, "maxDismiss", 3) is >= 1 and <= 100, "Límite de cierres inválido.");
                await Set("ANNOUNCEMENT_ENABLED", Str(p, "enabled") == "true" ? "TRUE" : "FALSE"); await Set("ANNOUNCEMENT_ID", id); await Set("ANNOUNCEMENT_EXPIRES_ON", Str(p, "expiresOn"));
                await Set("ANNOUNCEMENT_MAX_DISMISS", Num(p, "maxDismiss", 3).ToString()); await Set("ANNOUNCEMENT_PAYLOAD_JSON", new JsonObject { ["slides"] = p["slides"]!.DeepClone() }.ToJsonString());
                return new { ok = true, announcementConfig = Announcement(await CurrentConfig()) };
            }
            case "apiSaveConfig": return await SaveConfig(actor, P()!);
            case "apiResetDirectoryLink":
            {
                Admin(actor, true); var linked = await db.Users.FindAsync(Email(first)); Require(linked != null, "Usuario inexistente.");
                linked!.DirectoryAccount = null; return Ok();
            }
            case "apiSaveDepartment": return await SaveDepartment(actor, P()!);
            case "apiDeleteDepartment":
            {
                Admin(actor, true); Require(!await db.Users.AnyAsync(x => x.DepartmentId == first) && !await db.Orders.AnyAsync(x => x.DepartmentId == first), "El departamento tiene usuarios o históricos. Desactívalo para conservarlos.");
                var d = await db.Departments.FindAsync(S()); Require(d != null, "Departamento no encontrado."); db.Departments.Remove(d!); return Ok();
            }
            case "apiAdminSaveUser": return await SaveUser(actor, P()!);
            case "apiAdminDeleteUser":
            {
                Admin(actor); var u = await db.Users.FindAsync(S().ToLowerInvariant()); Require(u != null, "Usuario no encontrado."); Require(u!.Email != actor.Email && (actor.Role == Roles.GeneralAdmin || u.DepartmentId == actor.DepartmentId && u.Role == Roles.User), "No puedes desactivar esta cuenta."); await ProtectLastAdmin(u, u.Role, "INACTIVO"); u.Status = "INACTIVO"; return Ok();
            }
            case "apiGetMenuDay":
            {
                Admin(actor, true); BusinessRules.Date(S()); var rows = await db.Menu.Where(x => x.Date == first).ToListAsync(); return new { ok = true, items = rows.Select(x => new { id = x.Id, cat = x.CategoryId, plato = BusinessRules.Text(x.Dish), desc = BusinessRules.Text(x.Description), hab = x.Enabled ? "SI" : "NO" }) };
            }
            case "apiSaveMenuCategory": return await SaveCategory(actor, P()!);
            case "apiSaveMenuItem": return await SaveMenu(actor, S(), S(1), P(2)!);
            case "apiDeleteMenuItem":
            {
                Admin(actor, true); var m = await db.Menu.FindAsync(S()); Require(m != null, "Plato no encontrado."); rules.MenuDate(m!.Date, await Holidays()); var n = await CancelAffected(m.Date, [m], []); db.Menu.Remove(m); return new { ok = true, cancellations = n, cancellationEmails = n };
            }
            case "apiSaveWeeklyMenu": return await SaveWeekly(actor, P() as JsonObject ?? throw new BusinessException("Formato de menú inválido."));
            case "apiGetHolidays": return new { ok = true, holidays = (await db.Holidays.ToListAsync()).Select(x => new { date = x.Date, desc = x.Reason }) };
            case "apiSaveHoliday":
            {
                Admin(actor, true); Require(BusinessRules.Date(S()) >= BusinessRules.Date(rules.Today), "No puedes suspender fechas pasadas."); var h = await db.Holidays.FindAsync(S()); if (h == null) { h = new Holiday { Date = S() }; db.Holidays.Add(h); } h.Reason = Bounded(S(1), 500, "Motivo");
                var orders = await db.Orders.Where(x => x.Date == first && x.Status == "ACTIVO").ToListAsync(); foreach (var o in orders) { o.Status = "CANCELADO"; o.ModifiedAt = rules.Now.ToString("O"); Queue(o.Email, "Almuerzo suspendido", $"Se suspendió el almuerzo de {o.Date}. Motivo: {h.Reason}"); }
                return new { ok = true, cancellations = orders.Count, cancellationEmails = orders.Count };
            }
            case "apiDeleteHoliday": { Admin(actor, true); BusinessRules.Date(S()); var h = await db.Holidays.FindAsync(S()); if (h != null) db.Holidays.Remove(h); return Ok(); }
            case "apiSubmitMealRating": case "apiSubmitProviderRating": return await SubmitRating(actor, P()!, method == "apiSubmitMealRating");
            case "apiResetProviderPeriod":
            {
                Admin(actor, true); await Set("PROVIDER_NAME", Bounded(Str(P(), "providerName"), 150, "Proveedor")); await Set("PROVIDER_PERIOD_ID", Guid.NewGuid().ToString()); await Set("PROVIDER_PERIOD_START", rules.Today); return new { ok = true, providerInfo = Provider(await CurrentConfig()) };
            }
            case "apiGetRatingsSummary": return await RatingSummary(actor);
            case "apiGetChefGameRanking": return await Ranking(actor, S());
            case "apiSyncChefGameState": return await SyncGame(actor, P()!);
            case "apiHeartbeat":
            {
                Presence[actor.Email] = rules.Now; return new { ok = true, count = actor.Role == Roles.GeneralAdmin ? Presence.Values.Count(x => (rules.Now - x).TotalMinutes < 5) : 0 };
            }
            case "apiSendDailyCloseEmailsTest":
            {
                Admin(actor, true); var cfg = await Config(); Require(cfg["TEST_EMAIL_MODE"] == "TRUE" && !string.IsNullOrWhiteSpace(cfg["TEST_EMAIL_DEST"]), "Configura el destino de prueba antes de enviar.");
                var result = await reports.EnqueueCloseAsync(rules.NextBusinessDay(await Holidays()).ToString("yyyy-MM-dd"), cfg["TEST_EMAIL_DEST"]); return new { ok = true, msg = "Reporte de prueba encolado.", sent = result };
            }
            default: throw new BusinessException("Operación no reconocida.");
        }
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> Presence = new();
    public void Queue(string recipient, string subject, string body) => db.Outbox.Add(new MailMessageRecord { Recipient = recipient, Subject = subject, Body = body });
    private async Task<Dictionary<string, string>> CurrentConfig()
    {
        var c = await Config(); foreach (var s in db.ChangeTracker.Entries<Setting>().Select(e => e.Entity)) c[s.Key] = s.Value; return c;
    }
    private async Task<object> SubmitOrder(AppUser actor, JsonNode p)
    {
        var target = await Target(actor, Str(p, "impersonateEmail")); var date = Str(p, "date"); Require(Open(date, await Holidays(), await Config()), "Esta fecha está cerrada para pedidos.");
        var cats = List(p["categorias"]); var items = List(p["items"]).Select(BusinessRules.Text).ToArray(); Require(items.Length is >= 1 and <= 40 && cats.Length == items.Length, "Selecciona al menos un plato con categoría válida.");
        var menu = await db.Menu.Include(x => x.Category).Where(x => x.Date == date && x.Enabled).ToListAsync(); var selected = new List<MenuItem>();
        for (var i = 0; i < items.Length; i++) { var m = menu.FirstOrDefault(x => x.CategoryId == cats[i] && BusinessRules.Text(x.Dish) == items[i]); Require(m?.Category?.Status == "ACTIVO", "El plato ya no está disponible."); selected.Add(m!); }
        Require(selected.Select(x => x.Id).Distinct().Count() == selected.Count, "Selección duplicada.");
        foreach (var group in selected.GroupBy(x => x.CategoryId))
        {
            var c = group.First().Category!; Require(c.SelectionType == "MULTIPLE" || group.Count() == 1, "Solo un plato por categoría."); var allowed = Split(c.CombinableWith);
            Require(cats.Distinct().All(other => other == c.Id || (c.Combinable && allowed.Length == 0) || allowed.Contains(other)), $"La categoría {c.Name} no es compatible con la selección.");
        }
        Require(!cats.Contains("Granos") || items.Any(x => x.Contains("arroz blanco", StringComparison.OrdinalIgnoreCase)), "Los granos requieren Arroz Blanco.");
        Require(!(cats.Contains("Arroces") && cats.Contains("Viveres")), "No puedes combinar Arroz y Víveres.");
        var comments = Bounded(Str(p, "comentarios"), 500, "Comentarios", false); var submittedId = Str(p, "orderId");
        var order = submittedId.Length > 0 ? await db.Orders.FindAsync(submittedId) : await db.Orders.SingleOrDefaultAsync(x => x.Email == target.Email && x.Date == date && x.Status == "ACTIVO");
        if (submittedId.Length > 0) Require(order != null && order.Email == target.Email && order.Date == date, "Pedido no autorizado o inexistente.");
        if (order == null) { order = new Order { Id = Guid.NewGuid().ToString(), Email = target.Email, Date = date, RequestedAt = rules.Now.ToString("O"), CreatedBy = actor.Email }; db.Orders.Add(order); }
        order.Name = target.Name; order.DepartmentId = target.DepartmentId!; order.Summary = string.Join(", ", items); order.Status = "ACTIVO"; order.ModifiedAt = rules.Now.ToString("O");
        order.DetailJson = new JsonObject { ["categorias"] = new JsonArray(cats.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["items"] = new JsonArray(items.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["comentarios"] = comments }.ToJsonString();
        return new { ok = true, order = OrderDto(order) };
    }
    private async Task ProtectLastAdmin(AppUser user, string nextRole, string nextStatus)
    {
        if (user.Role == Roles.GeneralAdmin && user.Status == "ACTIVO" && (nextRole != Roles.GeneralAdmin || nextStatus != "ACTIVO"))
            Require(await db.Users.AnyAsync(x => x.Email != user.Email && x.Role == Roles.GeneralAdmin && x.Status == "ACTIVO" && x.Department!.Status == "ACTIVO"), "Debe quedar un administrador general activo.");
    }
    private async Task<object> SaveUser(AppUser actor, JsonNode p)
    {
        Admin(actor); var email = Email(Str(p, "email")); var u = await db.Users.FindAsync(email); var role = Str(p, "rol", Roles.User); if (role == "USUARIO") role = Roles.User;
        Require(Roles.All.Contains(role), "Rol inválido."); var state = Str(p, "estado", "ACTIVO"); Require(new[] { "ACTIVO", "INACTIVO", "PENDIENTE" }.Contains(state), "Estado inválido.");
        var dept = Str(p, "departamento", Str(p, "departamentoId"));
        if (actor.Role == Roles.DepartmentAdmin) { Require(role == Roles.User && (u == null || u.DepartmentId == actor.DepartmentId && u.Role == Roles.User), "Solo puedes administrar colaboradores de tu departamento y no puedes asignar roles administrativos."); dept = actor.DepartmentId!; }
        var department = await db.Departments.FindAsync(dept); Require(department is { Status: "ACTIVO" }, "Selecciona un departamento activo.");
        var code = Str(p, "codigo"); Require(Regex.IsMatch(code, @"^\d{4}$"), "Código de empleado: cuatro dígitos."); Require(!await db.Users.AnyAsync(x => x.EmployeeCode == code && x.Email != email), "Código ya asignado.");
        if (!string.IsNullOrEmpty(u?.DirectoryAccount)) Require(code == u.EmployeeCode, "El código pertenece al perfil AD. Un administrador general debe revisar la vinculación antes de cambiarlo.");
        if (u != null) await ProtectLastAdmin(u, role, state); var previous = u?.Status;
        if (u == null) { u = new AppUser { Email = email }; db.Users.Add(u); }
        if (string.IsNullOrEmpty(u.DirectoryAccount)) u.Name = Bounded(Str(p, "nombre"), 150, "Nombre"); u.EmployeeCode = code; u.DepartmentId = dept; u.Role = role; u.Status = state;
        if (state == "ACTIVO" && previous != "ACTIVO") Queue(email, "Acceso aprobado", "Tu cuenta de Almuerzo fue habilitada. Ya puedes iniciar sesión con tu cuenta institucional.");
        await SyncDepartmentAdmins(); return Ok();
    }
    private async Task SyncDepartmentAdmins()
    {
        var users = await db.Users.ToListAsync(); users.AddRange(db.Users.Local.Where(x => !users.Contains(x)));
        foreach (var dept in await db.Departments.ToListAsync()) dept.Admins = string.Join(", ", users.Where(x => x.DepartmentId == dept.Id && x.Role == Roles.DepartmentAdmin && x.Status == "ACTIVO").Select(x => x.Email));
    }
    private async Task<object> SaveDepartment(AppUser actor, JsonNode p)
    {
        Admin(actor, true); var d = await db.Departments.FindAsync(Str(p, "id")); var name = Bounded(Str(p, "nombre"), 150, "Nombre");
        Require(!await db.Departments.AnyAsync(x => x.Name.ToLower() == name.ToLower() && x.Id != Str(p, "id")), "Nombre de departamento duplicado.");
        var state = Str(p, "estado", "ACTIVO"); Require(state is "ACTIVO" or "INACTIVO", "Estado inválido.");
        if (d == null) { d = new Department(); db.Departments.Add(d); }
        if (state == "INACTIVO") Require(!await db.Users.AnyAsync(x => x.DepartmentId == d.Id && x.Status == "ACTIVO" && x.Role == Roles.GeneralAdmin), "Mueve primero los administradores generales a un departamento activo.");
        var adminEmails = Split(Str(p, "admins")).Select(Email).Distinct().ToArray(); var users = await db.Users.Where(x => adminEmails.Contains(x.Email) || x.DepartmentId == d.Id && x.Role == Roles.DepartmentAdmin).ToListAsync();
        Require(adminEmails.All(e => users.Any(x => x.Email == e && x.Status == "ACTIVO")), "Los administradores deben ser usuarios activos existentes."); d.Name = name; d.Status = state;
        foreach (var u in users) { if (u.Role == Roles.GeneralAdmin) continue; if (adminEmails.Contains(u.Email)) { u.Role = Roles.DepartmentAdmin; u.DepartmentId = d.Id; } else u.Role = Roles.User; }
        await SyncDepartmentAdmins(); d.Admins = string.Join(", ", adminEmails.Where(e => users.Any(x => x.Email == e && x.Role == Roles.DepartmentAdmin))); return Ok();
    }
    private async Task<object> SaveConfig(AppUser actor, JsonNode p)
    {
        Admin(actor, true); var cfg = await Config(); var changes = p as JsonObject ?? throw new BusinessException("Configuración inválida.");
        foreach (var pair in changes)
        {
            Require(cfg.ContainsKey(pair.Key), "Clave no permitida."); if (pair.Key == "MEAL_PRICE_HISTORY_JSON") continue;
            var value = pair.Value?.ToString() ?? ""; Require(value.Length <= 20000, "Configuración demasiado extensa.");
            if (BrandingService.Keys.Contains(pair.Key)) { Require(value == cfg[pair.Key], "Usa el panel de identidad visual para cambiar estos valores."); continue; }
            if (pair.Key is "HORA_ENVIO" or "HORA_RECORDATORIO") Require(TimeOnly.TryParseExact(value, "HH:mm", out _), "Hora inválida.");
            if (pair.Key == "MINUTOS_PREV_CIERRE") Require(int.TryParse(value, out var v) && v is >= 0 and <= 300, "Minutos inválidos.");
            if (pair.Key is "TEST_EMAIL_MODE" or "ANNOUNCEMENT_ENABLED") Require(value is "TRUE" or "FALSE", "Se requiere TRUE o FALSE.");
            if (pair.Key == "ANNOUNCEMENT_MAX_DISMISS") Require(int.TryParse(value, out var limit) && limit is >= 1 and <= 100, "Límite de cierres inválido.");
            if (pair.Key is "ANNOUNCEMENT_EXPIRES_ON" or "PROVIDER_PERIOD_START" && value.Length > 0) BusinessRules.Date(value);
            if (pair.Key == "ANNOUNCEMENT_PAYLOAD_JSON") { Require(value == cfg[pair.Key], "Usa el editor de avisos para validar su contenido."); continue; }
            if (pair.Key == "APP_URL") Require(value == "" || Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https", "La URL pública debe usar HTTPS.");
            if (pair.Key == "MEAL_PRICE_CURRENT")
            {
                Require(decimal.TryParse(value, CultureInfo.InvariantCulture, out var price) && price is >= 0 and <= 100000, "Precio inválido.");
                if (value != cfg[pair.Key]) { var history = JsonNode.Parse(cfg["MEAL_PRICE_HISTORY_JSON"]) as JsonArray ?? new(); var old = history.FirstOrDefault(x => Str(x, "from") == rules.Today); if (old != null) history.Remove(old); history.Add(new JsonObject { ["from"] = rules.Today, ["price"] = price }); await Set("MEAL_PRICE_HISTORY_JSON", history.ToJsonString()); }
            }
            if (pair.Key == "RESPONSIBLES_EMAILS_JSON")
            {
                var recipients = JsonNode.Parse(value) as JsonArray; Require(recipients != null && recipients.Count <= 30, "Lista de destinatarios inválida.");
                foreach (var recipient in recipients!) { var address = recipient is JsonObject ? Str(recipient, "email") : recipient?.ToString() ?? ""; Require(System.Net.Mail.MailAddress.TryCreate(address, out _), "Correo inválido."); }
            }
            if (pair.Key is "ADMIN_EMAILS" or "TEST_EMAIL_DEST") foreach (var e in Split(value)) Require(System.Net.Mail.MailAddress.TryCreate(e, out _), "Correo inválido.");
            if (pair.Key == "TEST_EMAIL_DEST" && value.Length > 0) Require(System.Net.Mail.MailAddress.TryCreate(value.Trim(), out var destination) && destination.Address == value.Trim(), "Define un único correo de prueba.");
            if (pair.Key.EndsWith("_JSON")) JsonNode.Parse(value);
            await Set(pair.Key, value);
        }
        return Ok();
    }
    private async Task<object> SaveCategory(AppUser actor, JsonNode p)
    {
        Admin(actor, true); var c = await db.Categories.FindAsync(Str(p, "id")); var name = BusinessRules.Text(Bounded(Str(p, "nombre"), 80, "Nombre"));
        var aliases = List(p["aliasesText"] ?? p["aliases"]); Require(aliases.Length <= 10 && aliases.All(x => x.Length <= 80), "Alias inválidos."); var keys = aliases.Append(name).Select(x => x.ToLowerInvariant()).ToArray();
        var others = await db.Categories.Where(x => x.Id != Str(p, "id")).ToListAsync(); Require(!others.Any(x => Split(x.Aliases).Append(x.Name).Append(x.Id).Any(k => keys.Contains(k.ToLowerInvariant()))), "Nombre o alias duplicado.");
        Require(Num(p, "orden") is >= 0 and <= 9999, "Orden inválido."); Require(Str(p, "estado", "ACTIVO") is "ACTIVO" or "INACTIVO", "Estado inválido."); Require(Str(p, "tipo_seleccion", "UNICA") is "UNICA" or "MULTIPLE", "Selección inválida.");
        var allowed = List(p["combinable_con"]); Require(allowed.All(x => others.Any(o => o.Id == x) || x == c?.Id), "Categoría compatible inexistente.");
        if (c == null) { c = new MenuCategory(); db.Categories.Add(c); } c.Name = name; c.SortOrder = Num(p, "orden"); c.Status = Str(p, "estado", "ACTIVO"); c.Aliases = string.Join(", ", aliases); c.Combinable = Str(p, "es_combinable") is not ("false" or "NO"); c.CombinableWith = string.Join(", ", allowed); c.SelectionType = Str(p, "tipo_seleccion", "UNICA");
        var all = await db.Categories.OrderBy(x => x.SortOrder).ToListAsync(); if (!all.Contains(c)) all.Add(c); return new { ok = true, category = CategoryDto(c), categories = all.OrderBy(x => x.SortOrder).Select(CategoryDto) };
    }
    private async Task<MenuItem> NewMenu(string date, string cat, JsonNode p)
    {
        var category = await db.Categories.FindAsync(cat); Require(category is { Status: "ACTIVO" }, "Categoría inexistente o inactiva.");
        return new MenuItem { Date = date, CategoryId = cat, Dish = BusinessRules.Text(Bounded(Str(p, "plato"), 300, "Plato")), Description = BusinessRules.Text(Bounded(Str(p, "desc"), 1000, "Descripción", false)) };
    }
    private async Task<int> CancelAffected(string date, IEnumerable<MenuItem> old, IEnumerable<MenuItem> next)
    {
        var removed = old.Where(x => x.Enabled && !next.Any(n => n.Enabled && n.CategoryId == x.CategoryId && BusinessRules.Text(n.Dish) == BusinessRules.Text(x.Dish) && n.Description == x.Description)).ToArray();
        if (removed.Length == 0) return 0; var orders = await db.Orders.Where(x => x.Date == date && x.Status == "ACTIVO").ToListAsync(); var count = 0;
        foreach (var o in orders)
        {
            var detail = Obj(o.DetailJson); var cats = List(detail["categorias"]); var items = List(detail["items"]);
            if (!removed.Any(m => items.Select((s, i) => (s, i)).Any(t => BusinessRules.Text(t.s) == BusinessRules.Text(m.Dish) && (t.i >= cats.Length || cats[t.i] == m.CategoryId)))) continue;
            o.Status = "CANCELADO"; o.ModifiedAt = rules.Now.ToString("O"); Queue(o.Email, "Tu pedido fue cancelado por cambio de menú", $"Cambió un plato de tu pedido del {date}. Selecciona nuevamente tu almuerzo."); count++;
        }
        return count;
    }
    private async Task<int> NotifyAvailable(string[] dates)
    {
        if (dates.Length == 0) return 0; var users = await db.Users.Where(x => x.Status == "ACTIVO").ToListAsync(); var count = 0;
        foreach (var u in users) { if (Str(Obj(u.PreferencesJson), "reminders", "true") == "false") continue; Queue(u.Email, "Nuevo menú disponible", $"Ya puedes seleccionar tu almuerzo para: {string.Join(", ", dates)}."); count++; } return count;
    }
    private async Task<object> SaveMenu(AppUser actor, string date, string cat, JsonNode p)
    {
        Admin(actor, true); rules.MenuDate(date, await Holidays()); var newItem = await NewMenu(date, cat, p); var old = Str(p, "id") == "" ? null : await db.Menu.FindAsync(Str(p, "id"));
        Require(Str(p, "id") == "" || old != null && old.Date == date, "Plato no encontrado para esta fecha."); var had = await db.Menu.AnyAsync(x => x.Date == date && x.Enabled); var cancelled = old == null ? 0 : await CancelAffected(date, [old], [newItem]);
        if (old != null) { old.CategoryId = cat; old.Dish = newItem.Dish; old.Description = newItem.Description; old.Enabled = true; } else db.Menu.Add(newItem);
        var notified = !had && Open(date, await Holidays(), await Config()) ? await NotifyAvailable([date]) : 0; return new { ok = true, cancellations = cancelled, cancellationEmails = cancelled, menuNotificationEmails = notified };
    }
    private async Task<object> SaveWeekly(AppUser actor, JsonObject payload)
    {
        Admin(actor, true); Require(payload.Count is >= 1 and <= 14, "Importa entre 1 y 14 días."); var holidays = await Holidays(); var cfg = await Config(); var plans = new List<(string date, List<MenuItem> old, List<MenuItem> next)>();
        foreach (var day in payload)
        {
            rules.MenuDate(day.Key, holidays); Require(day.Value is JsonArray a && a.Count <= 200, "Menú diario inválido."); var next = new List<MenuItem>();
            foreach (var item in (JsonArray)day.Value!) next.Add(await NewMenu(day.Key, Str(item, "cat"), item!));
            plans.Add((day.Key, await db.Menu.Where(x => x.Date == day.Key).ToListAsync(), next));
        }
        var cancelled = 0; var available = new List<string>();
        foreach (var plan in plans) { cancelled += await CancelAffected(plan.date, plan.old, plan.next); if (!plan.old.Any(x => x.Enabled) && plan.next.Count > 0 && Open(plan.date, holidays, cfg)) available.Add(plan.date); db.Menu.RemoveRange(plan.old); db.Menu.AddRange(plan.next); }
        var notified = await NotifyAvailable(available.ToArray()); return new { ok = true, cancellations = cancelled, cancellationEmails = cancelled, menuNotificationEmails = notified };
    }
    private async Task<object> SubmitRating(AppUser actor, JsonNode p, bool meal)
    {
        Require(Num(p, "rating") is >= 1 and <= 5, "Puntuación de 1 a 5 estrellas."); var cfg = await Config(); var scope = meal ? Str(p, "date") : cfg["PROVIDER_PERIOD_ID"]; Order? order = null;
        if (meal) { Require(rules.RatingAllowed(scope), "Puedes valorar desde las 12:00 del día de consumo."); order = await db.Orders.SingleOrDefaultAsync(x => x.Email == actor.Email && x.Date == scope && x.Status == "ACTIVO"); Require(order != null, "No tienes un pedido activo para esa fecha."); }
        var kind = meal ? "MEAL" : "PROVIDER"; var r = await db.Ratings.SingleOrDefaultAsync(x => x.Email == actor.Email && x.Scope == scope && x.Kind == kind);
        if (r == null) { r = new Rating { Kind = kind, Scope = scope, Email = actor.Email, Name = actor.Name, Department = actor.Department!.Name, CreatedAt = rules.Now.ToString("O") }; db.Ratings.Add(r); } else r.Version++;
        r.Score = Num(p, "rating"); r.Comment = Bounded(Str(p, "comment"), 500, "Comentario", false); r.UpdatedAt = rules.Now.ToString("O"); r.OrderId = order?.Id ?? ""; r.Dishes = order?.Summary ?? ""; r.ProviderName = cfg["PROVIDER_NAME"];
        if (!meal) db.RatingHistory.Add(new RatingHistory { VoteId = r.Id, PeriodId = scope, ProviderName = r.ProviderName, Email = actor.Email, Name = actor.Name, Department = r.Department, Score = r.Score, Comment = r.Comment, Timestamp = r.UpdatedAt });
        return meal ? new { ok = true, rating = RatingDto(r) } : (object)new { ok = true, providerRating = RatingDto(r) };
    }
    private async Task<object> RatingSummary(AppUser actor)
    {
        var cfg = await Config(); var period = cfg["PROVIDER_PERIOD_ID"]; var all = await db.Ratings.AsNoTracking().Where(x => x.Kind == "MEAL" || x.Scope == period).ToListAsync();
        object Stats(string kind)
        {
            var rows = all.Where(x => x.Kind == kind).ToArray(); return new { providerInfo = Provider(cfg), total = rows.Length, average = rows.Length == 0 ? 0 : Math.Round(rows.Average(x => x.Score), 1),
                starsCount = Enumerable.Range(1, 5).ToDictionary(x => x.ToString(), x => rows.Count(r => r.Score == x)),
                recentFeedback = rows.Where(x => actor.Role != Roles.DepartmentAdmin || x.Department == actor.DepartmentId || x.Department == actor.Department!.Name).OrderByDescending(x => x.UpdatedAt).Take(actor.Role == Roles.User ? 5 : 50).Select(x => new { id = x.Id, nombre = actor.Role == Roles.User ? "Colaborador" : x.Name, departamento = actor.Role == Roles.User ? "" : x.Department, puntuacion = x.Score, comentario = x.Comment, version = x.Version, fechaConsumo = x.Scope, platos = x.Dishes, fecha = x.UpdatedAt[..Math.Min(10, x.UpdatedAt.Length)] }) };
        }
        return new { ok = true, providerStats = Stats("PROVIDER"), mealStats = Stats("MEAL") };
    }
    private async Task<object> Ranking(AppUser actor, string targetEmail)
    {
        var target = await Target(actor, targetEmail); var users = await db.Users.AsNoTracking().Include(x => x.Department).Where(x => x.Status == "ACTIVO").ToListAsync();
        var sorted = users.Select(u => new { u, g = Game(u) }).OrderByDescending(x => Num(x.g, "score")).ThenByDescending(x => Num(x.g, "hits")).ThenBy(x => Num(x.g, "misses")).ThenBy(x => x.u.Name);
        var rows = sorted.Select((x, i) => new { rank = i + 1, nombre = x.u.Name, departamento = x.u.Department?.Name ?? "", score = Num(x.g, "score"), hits = Num(x.g, "hits"), misses = Num(x.g, "misses"), bestStreak = Num(x.g, "bestStreak"), isCurrent = x.u.Email == target.Email }).ToArray();
        return new { ok = true, monthKey = rules.Today[..7], monthLabel = rules.Now.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-DO")), rows = rows.Take(50), currentUser = rows.FirstOrDefault(x => x.isCurrent) };
    }
    private async Task<object> SyncGame(AppUser actor, JsonNode p)
    {
        var target = await Target(actor, Str(p, "targetEmail")); var old = Game(target); var incoming = p["state"] ?? new JsonObject(); var used = Num(incoming, "usedSeconds"); var elapsed = Obj(target.GameJson)["savedAt"]?.ToString();
        var seconds = DateTimeOffset.TryParse(elapsed, out var previous) ? Math.Max(0, (rules.Now - previous).TotalSeconds) : 900;
        Require(used >= Num(old, "usedSeconds") && used <= 900 && used - Num(old, "usedSeconds") <= seconds + 5, "Tiempo de juego inválido.");
        var delta = Num(incoming, "hits") - Num(old, "hits"); Require(delta >= 0 && delta <= seconds * 15 + 30, "Progreso de juego inválido.");
        Require(Num(incoming, "score") is >= 0 and <= 999999 && Num(incoming, "score") - Num(old, "score") <= delta * 25 + 20, "Puntaje inválido.");
        foreach (var key in new[] { "score", "hits", "misses", "streak", "bestStreak", "penaltySeconds" }) { var value = Num(incoming, key); Require(value >= 0 && value <= (key == "penaltySeconds" ? 30 : 999999), "Estado de juego inválido."); old[key] = value; }
        old["usedSeconds"] = used; old["remainingSeconds"] = 900 - used; old["savedAt"] = rules.Now.ToString("O"); target.GameJson = old.ToJsonString(); return new { ok = true, state = old };
    }
}
