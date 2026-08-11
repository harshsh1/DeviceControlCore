namespace DeviceControlCore.Services;

public interface IOsSettingsService
{
	Task<OsSettingResult> SetTimezoneAsync(string timezone, string invokedBy, CancellationToken cancellationToken);
}

public sealed class OsSettingResult
{
	public required bool Succeeded { get; init; }
	public required string Message { get; init; }
}
