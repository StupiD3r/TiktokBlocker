using System.Diagnostics;
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
                    process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Blocked process detected: {ProcessName} (PID: {PID})",
                        process.ProcessName, process.Id);
                    process.Kill(entireProcessTree: true);
                    _logger.LogInformation("Terminated: {ProcessName}", process.ProcessName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not terminate process {ProcessName}", process.ProcessName);
            }
        }
    }

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

        bool isBlocked = _config.BlockedDirectoryNames.Any(n =>
            name.Contains(n, StringComparison.OrdinalIgnoreCase)) ||
            _config.BlockedInstallerNames.Any(n =>
                name.Equals(n, StringComparison.OrdinalIgnoreCase)) ||
            _config.BlockedProcessNames.Any(n =>
                Path.GetFileNameWithoutExtension(name).Equals(n, StringComparison.OrdinalIgnoreCase));

        if (!isBlocked) return;

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
