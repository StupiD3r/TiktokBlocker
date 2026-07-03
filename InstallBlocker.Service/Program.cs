using InstallBlocker.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "InstallBlocker";
});

builder.Services.AddHostedService<GuardianService>();

builder.Services.Configure<GuardianConfig>(
    builder.Configuration.GetSection("GuardianConfig"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(options =>
    {
        options.SourceName = "InstallBlocker";
        options.LogName = "Application";
    });
}

var host = builder.Build();
await host.RunAsync();
