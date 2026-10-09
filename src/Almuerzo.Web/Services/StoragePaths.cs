namespace Almuerzo.Web.Services;
public record StoragePaths(string Root)
{
    public string Reports => Path.Combine(Root, "reports");
    public string Mail => Path.Combine(Root, "mail");
    public string Backups => Path.Combine(Root, "backups");
}
