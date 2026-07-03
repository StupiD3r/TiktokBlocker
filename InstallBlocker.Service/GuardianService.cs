using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InstallBlocker.Service;

public class GuardianService : BackgroundService
{
    private readonly ILogger<GuardianService> _logger;
    private readonly GuardianConfig _config;
    private FileSystemWatcher[]? _watchers;

    public GuardianService(ILogger<GuardianService> logger, IOptions<GuardianConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InstallBlocker Guardian Service started");

        if (!string.IsNullOrEmpty(_config.LogDirectory))
            Directory.CreateDirectory(_config.LogDirectory);

        StartFileWatchers();

        while (!stoppingToken.IsCancellationRequested)
        {
            CheckProcesses();
            CheckFiles();
            await Task.Delay(TimeSpan.FromSeconds(_config.PollingIntervalSeconds), stoppingToken);
        }
    }

    private void CheckProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (_config.BlockedProcessNames.Any(name =>
                    process.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Blocked process detected: {ProcessName} (PID: {PID})",
                        process.ProcessName, process.Id);
                    process.Kill(entireProcessTree: true);
                    _logger.LogInformation("Terminated: {ProcessName}", process.ProcessName);
                    _ = ShowFakeErrorPopupAsync(process.ProcessName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not terminate process {ProcessName}", process.ProcessName);
            }
        }
    }

    private static IEnumerable<string> GetScanDirectories(ILogger logger)
    {
        var dirs = new List<string>(32);

        AddCommonDirs(dirs);

        var sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var usersDir = Path.Combine(sysDrive, "Users");
        if (Directory.Exists(usersDir))
        {
            try
            {
                foreach (var userDir in Directory.EnumerateDirectories(usersDir))
                {
                    var userName = Path.GetFileName(userDir);
                    if (userName is "Public" or "Default" or "Default User" or "All Users")
                        continue;

                    AddIfExists(dirs, Path.Combine(userDir, "Desktop"));
                    AddIfExists(dirs, Path.Combine(userDir, "Downloads"));
                    AddIfExists(dirs, Path.Combine(userDir, "AppData", "Local", "Temp"));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enumerate user directories in {UsersDir}", usersDir);
            }
        }

        AddPublicDirs(dirs);

        dirs.AddRange(DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName));

        return dirs;

        static void AddCommonDirs(List<string> list)
        {
            AddIfExists(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            AddIfExists(list, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddIfExists(list, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddIfExists(list, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            AddIfExists(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        }

        static void AddPublicDirs(List<string> list)
        {
            var sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            var pubDir = Path.Combine(sysDrive, "Users", "Public");
            if (Directory.Exists(pubDir))
            {
                AddIfExists(list, Path.Combine(pubDir, "Desktop"));
                AddIfExists(list, Path.Combine(pubDir, "Downloads"));
            }
        }

        static void AddIfExists(List<string> list, string? path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                list.Add(path);
        }
    }

    private void CheckFiles()
    {
        _logger.LogDebug("File scan started");

        foreach (var dir in GetScanDirectories(_logger))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    if (IsBlockedItem(name))
                    {
                        _logger.LogWarning("Blocked file found: {Path}", file);
                        File.Delete(file);
                        _logger.LogInformation("Deleted file: {Path}", file);
                    }
                }

                foreach (var subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(subDir);
                    if (IsBlockedItem(name))
                    {
                        _logger.LogWarning("Blocked directory found: {Path}", subDir);
                        Directory.Delete(subDir, recursive: true);
                        _logger.LogInformation("Deleted directory: {Path}", subDir);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogInformation("No access to directory {Dir}: {Message}", dir, ex.Message);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning directory {Dir}", dir);
            }
        }
    }

    private bool IsBlockedItem(string name)
    {
        return _config.BlockedDirectoryNames.Any(n =>
            name.Contains(n, StringComparison.OrdinalIgnoreCase)) ||
            _config.BlockedInstallerNames.Any(n =>
                name.Contains(n, StringComparison.OrdinalIgnoreCase)) ||
            _config.BlockedProcessNames.Any(n =>
                Path.GetFileNameWithoutExtension(name).Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ShowFakeErrorPopupAsync(string processName)
    {
        await Task.Run(() =>
        {
            try
            {
                var sessionId = WTSGetActiveConsoleSessionId();
                if (sessionId == 0xFFFFFFFF) return;

                WTSSendMessage(
                    WTS_CURRENT_SERVER_HANDLE,
                    sessionId,
                    "Installation Error",
                    "Installation Error".Length * 2,
                    "driver isn't compatible",
                    "driver isn't compatible".Length * 2,
                    MB_ICONERROR,
                    15000,
                    out _,
                    false);
            }
            catch
            {
            }
        });
    }

    private const int MB_ICONERROR = 0x00000010;
    private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSSendMessage(
        IntPtr hServer,
        uint SessionId,
        [MarshalAs(UnmanagedType.LPWStr)] string pTitle,
        int TitleLength,
        [MarshalAs(UnmanagedType.LPWStr)] string pMessage,
        int MessageLength,
        int Style,
        int Timeout,
        out int pResponse,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);

    private void StartFileWatchers()
    {
        var directories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };

        var watchersList = new List<FileSystemWatcher>();
        foreach (var dir in directories.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName
                };
                watcher.Created += OnItemCreated;
                watcher.EnableRaisingEvents = true;
                watchersList.Add(watcher);
                _logger.LogInformation("Watching directory: {Dir}", dir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start file watcher for {Dir}", dir);
            }
        }

        _watchers = [.. watchersList];
    }

    private void OnItemCreated(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.Name ?? "");
        if (!IsBlockedItem(name)) return;

        _logger.LogWarning("Blocked item detected: {Path}", e.FullPath);

        try
        {
            if (Directory.Exists(e.FullPath))
            {
                Directory.Delete(e.FullPath, recursive: true);
                _logger.LogInformation("Deleted directory: {Path}", e.FullPath);
            }
            else if (File.Exists(e.FullPath))
            {
                File.Delete(e.FullPath);
                _logger.LogInformation("Deleted file: {Path}", e.FullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Path}", e.FullPath);
        }
    }

    public override void Dispose()
    {
        if (_watchers is not null)
        {
            foreach (var w in _watchers)
            {
                try { w.Dispose(); } catch { /* ignore */ }
            }
        }
        base.Dispose();
    }
}
