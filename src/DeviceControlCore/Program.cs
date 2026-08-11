using DeviceControlCore.Options;
using DeviceControlCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
	Args = args,
	ContentRootPath = AppContext.BaseDirectory
});

Log.Logger = new LoggerConfiguration()
	.ReadFrom.Configuration(builder.Configuration)
	.Enrich.FromLogContext()
	.CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

builder.Services.Configure<ServiceOptions>(
	builder.Configuration.GetSection(ServiceOptions.SectionName));

builder.Services.AddSingleton<IStateService, StateService>();
builder.Services.AddSingleton<IPreInstallScriptRunner, PreInstallScriptRunner>();
builder.Services.AddSingleton<IUpdateService, UpdateService>();
builder.Services.AddSingleton<IOsSettingsService, OsSettingsService>();

builder.Services.AddSingleton<DeviceMonitorService>();
builder.Services.AddSingleton<IDeviceMonitorService>(sp => sp.GetRequiredService<DeviceMonitorService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceMonitorService>());

builder.Services.AddHostedService<ConsoleHostedService>();

var host = builder.Build();

try
{
	await host.RunAsync();
}
finally
{
	Log.CloseAndFlush();
}
