using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Serilog;
using SyntheticHttpMonitor.Options;
using SyntheticHttpMonitor.Services;

// Windows services default to System32 as CWD; keep relative paths (logs, config) beside the exe.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "logs"));

var builder = Host.CreateApplicationBuilder(args);

// Optional split config (merged after appsettings*.json; later files override the same keys).
builder.Configuration.AddJsonFile("targets.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile("logging.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile("notifications.json", optional: true, reloadOnChange: false);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Synthetic HTTP Monitor";
});

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

builder.Services.Configure<SyntheticMonitorOptions>(builder.Configuration.GetSection(SyntheticMonitorOptions.SectionName));
builder.Services.Configure<AlertingOptions>(builder.Configuration.GetSection(AlertingOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<TicketingApiOptions>(builder.Configuration.GetSection(TicketingApiOptions.SectionName));
builder.Services.Configure<PagerDutyOptions>(builder.Configuration.GetSection(PagerDutyOptions.SectionName));

static SocketsHttpHandler CreateProbeHandler(bool skipCertificateValidation)
{
    var handler = new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
    };
    if (skipCertificateValidation)
    {
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
    }

    return handler;
}

builder.Services.AddHttpClient(HttpProbe.HttpClientNameStrict)
    .ConfigurePrimaryHttpMessageHandler(static _ => CreateProbeHandler(skipCertificateValidation: false));

builder.Services.AddHttpClient(HttpProbe.HttpClientNameInsecure)
    .ConfigurePrimaryHttpMessageHandler(static _ => CreateProbeHandler(skipCertificateValidation: true));

builder.Services.AddHttpClient(nameof(PagerDutyEventsService));
builder.Services.AddHttpClient(OAuthTokenService.HttpClientName);

builder.Services.AddSingleton<HttpProbe>();
builder.Services.AddSingleton<OAuthTokenService>();
builder.Services.AddSingleton<SmtpNotificationService>();
builder.Services.AddSingleton<PagerDutyEventsService>();
builder.Services.AddHostedService<MonitorHostedService>();

try
{
    var host = builder.Build();
    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}
