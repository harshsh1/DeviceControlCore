namespace DeviceControlCore.Services;

public interface IPreInstallScriptRunner
{
	Task<PreInstallScriptResult> RunAsync(
		string scriptPath,
		string workingDirectory,
		CancellationToken cancellationToken);
}

public sealed class PreInstallScriptResult
{
	public required bool Succeeded { get; init; }
	public required int ExitCode { get; init; }
	public string? ErrorMessage { get; init; }
}
