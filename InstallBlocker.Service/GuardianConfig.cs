namespace InstallBlocker.Service;

public class GuardianConfig
{
    public int PollingIntervalSeconds { get; set; } = 1;
    public string[] BlockedProcessNames { get; set; } = [];
    public string[] BlockedInstallerNames { get; set; } = [];
    public string[] BlockedDirectoryNames { get; set; } = [];
    public string LogDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Logs");
}
