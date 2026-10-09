using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Data;

public static class DemoData
{
    public static async Task InitializeAsync(AppDbContext db, BusinessRules rules)
    {
        const string email = "demo.almuerzo@infotep.gob.do";
        if (!await db.Departments.AnyAsync()) db.Departments.Add(new Department { Id = "demo", Name = "Departamento de demostración" });
        if (!await db.Users.AnyAsync()) db.Users.Add(new AppUser { Email = email, Name = "Demostración local", DepartmentId = "demo", Role = Roles.GeneralAdmin, Status = "ACTIVO", EmployeeCode = "0000" });
        var holidays = new HashSet<string>(); var next = rules.NextBusinessDay(holidays);
        for (var i = 0; i < 5; i++) {
            var date = next.ToString("yyyy-MM-dd");
            if (!await db.Menu.AnyAsync(x => x.Date == date)) db.Menu.AddRange(new MenuItem { Date = date, CategoryId = "Arroces", Dish = "Arroz Blanco" }, new MenuItem { Date = date, CategoryId = "Carnes", Dish = "Pollo al Horno", Description = "Con hierbas y vegetales" }, new MenuItem { Date = date, CategoryId = "Ensaladas", Dish = "Ensalada Verde" });
            do { next = next.AddDays(1); } while (!BusinessRules.BusinessDay(next, holidays));
        }
        await db.SaveChangesAsync();
    }
}
