namespace Sweeprr.API.Models;

public class GlobalSettings
{
    public int Id { get; set; }
    public string InstanceName { get; set; } = "Sweeprr";
    public string JwtSecret { get; set; } = string.Empty;
    public int MaxItemsPerRun { get; set; } = 20;
    public double MaxGbPerRun { get; set; } = 50.0;
    public bool GlobalDryRun { get; set; } = true;
    public string DefaultCron { get; set; } = "0 3 * * *";
}
