using System.Text.Json;
using DeviceControlCore.Models;
using DeviceControlCore.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceControlCore.Services;

public sealed class OsSettingsService : IOsSettingsService
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly ServiceOptions _options;
	private readonly ILogger<OsSettingsService> _logger;

	public OsSettingsService(IOptions<ServiceOptions> options, ILogger<OsSettingsService> logger)
	{
		_options = options.Value;
		_logger = logger;
	}

	public async Task<OsSettingResult> SetTimezoneAsync(string timezone, string invokedBy, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(timezone))
		{
			_logger.LogWarning("Rejected os set-timezone request with empty value (invoked by {InvokedBy})", invokedBy);
			return new OsSettingResult { Succeeded = false, Message = "Timezone value must not be empty." };
		}

		var state = LoadState();
		var oldValue = state.Timezone;

		var simulatedCommand = OperatingSystem.IsWindows()
			? $"tzutil /s \"{timezone}\""
			: $"timedatectl set-timezone {timezone}";

		_logger.LogInformation("Simulated OS command: {Command}", simulatedCommand);

		state.Timezone = timezone;
		await SaveStateAsync(state, cancellationToken);

		var auditRecord = new AuditRecord
		{
			Timestamp = DateTimeOffset.UtcNow,
			InvokedBy = invokedBy,
			Setting = "Timezone",
			OldValue = oldValue,
			NewValue = timezone
		};
		await AppendAuditRecordAsync(auditRecord, cancellationToken);

		_logger.LogInformation(
			"OS setting changed: Timezone {OldValue} -> {NewValue} (invoked by {InvokedBy})",
			oldValue, timezone, invokedBy);

		return new OsSettingResult { Succeeded = true, Message = $"Timezone updated to {timezone}." };
	}

	private OsSettingsState LoadState()
	{
		if (!File.Exists(_options.OsSettingsStateFilePath))
		{
			return new OsSettingsState { Timezone = "UTC" };
		}

		var json = File.ReadAllText(_options.OsSettingsStateFilePath);
		return JsonSerializer.Deserialize<OsSettingsState>(json, JsonOptions)
			?? new OsSettingsState { Timezone = "UTC" };
	}

	private async Task SaveStateAsync(OsSettingsState state, CancellationToken cancellationToken)
	{
		var directory = Path.GetDirectoryName(_options.OsSettingsStateFilePath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var json = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
		await File.WriteAllTextAsync(_options.OsSettingsStateFilePath, json, cancellationToken);
	}

	private async Task AppendAuditRecordAsync(AuditRecord record, CancellationToken cancellationToken)
	{
		var directory = Path.GetDirectoryName(_options.AuditLogPath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
		await File.AppendAllTextAsync(_options.AuditLogPath, line, cancellationToken);
	}

	private sealed class OsSettingsState
	{
		public required string Timezone { get; set; }
	}
}
