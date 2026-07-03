using System.Diagnostics;

if (!IsAdministrator())
{
    Console.Error.WriteLine("This tool must be run as Administrator.");
    return;
}

var action = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

switch (action)
{
    case "install":
        InstallService();
        break;
    case "uninstall":
        UninstallService();
        break;
    case "status":
        ShowStatus();
        break;
    default:
        ShowHelp();
        break;
}

static void InstallService()
{
    var exePath = Path.Combine(AppContext.BaseDirectory, "InstallBlocker.Service.exe");

    if (!File.Exists(exePath))
    {
        Console.Error.WriteLine("Service executable not found. Build the Service project first.");
        Console.Error.WriteLine($"Expected: {exePath}");
        return;
    }

    RunSc($"create InstallBlocker binPath=\"{exePath}\" start=auto");
    RunSc("description InstallBlocker \"Prevents installation of TikTok Live Studio\"");
    RunSc("failure InstallBlocker reset=86400 actions=restart/5000/restart/10000/restart/30000");
    RunSc("start InstallBlocker");

    Console.WriteLine("Service installed and started.");
}

static void UninstallService()
{
    RunSc("stop InstallBlocker");
    RunSc("delete InstallBlocker");
    Console.WriteLine("Service uninstalled.");
}

static void ShowStatus()
{
    RunSc("query InstallBlocker");
}

static void ShowHelp()
{
    Console.WriteLine("Usage: InstallBlocker.Installer <command>");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  install    Install and start the service");
    Console.WriteLine("  uninstall  Stop and remove the service");
    Console.WriteLine("  status     Show service status");
}

static bool IsAdministrator()
{
    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
    var principal = new System.Security.Principal.WindowsPrincipal(identity);
    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
}

static void RunSc(string arguments)
{
    var psi = new ProcessStartInfo("sc", arguments)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    using var process = Process.Start(psi);

    if (process is null) return;

    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (!string.IsNullOrWhiteSpace(output))
        Console.WriteLine(output);

    if (!string.IsNullOrWhiteSpace(error))
        Console.Error.WriteLine(error);

    if (process.ExitCode != 0)
        Environment.Exit(process.ExitCode);
}
