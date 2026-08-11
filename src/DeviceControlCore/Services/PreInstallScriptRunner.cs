using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DeviceControlCore.Services;

public sealed class PreInstallScriptRunner : IPreInstallScriptRunner
{
	private readonly ILogger<PreInstallScriptRunner> _logger;

	public PreInstallScriptRunner(ILogger<PreInstallScriptRunner> logger)
	{
		_logger = logger;
	}

	public async Task<PreInstallScriptResult> RunAsync(
		string scriptPath,
		string workingDirectory,
		CancellationToken cancellationToken)
	{
		var startInfo = OperatingSystem.IsWindows()
			? new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
			: new ProcessStartInfo("/bin/sh", $"-c \"{scriptPath}\"");

		startInfo.WorkingDirectory = workingDirectory;
		startInfo.UseShellExecute = false;
		startInfo.RedirectStandardError = true;

		using var process = new Process { StartInfo = startInfo };

		_logger.LogInformation("Running pre-install script: {ScriptPath}", scriptPath);

		try
		{
			process.Start();
			await process.WaitForExitAsync(cancellationToken);

			var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken);

			if (process.ExitCode != 0)
			{
				var message = string.IsNullOrWhiteSpace(errorOutput)
					? $"Script exited with code {process.ExitCode}"
					: errorOutput.Trim();

				_logger.LogError(
					"Pre-install script exited with non-zero code {ExitCode}: {Message}",
					process.ExitCode, message);

				return new PreInstallScriptResult
				{
					Succeeded = false,
					ExitCode = process.ExitCode,
					ErrorMessage = message
				};
			}

			_logger.LogInformation("Pre-install script completed successfully");
			return new PreInstallScriptResult { Succeeded = true, ExitCode = 0 };
		}
		catch (OperationCanceledException)
		{
			_logger.LogError("Pre-install script timed out or was cancelled: {ScriptPath}", scriptPath);
			KillSafely(process);
			return new PreInstallScriptResult
			{
				Succeeded = false,
				ExitCode = -1,
				ErrorMessage = "Script execution was cancelled or timed out"
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Pre-install script threw an exception: {ScriptPath}", scriptPath);
			KillSafely(process);
			return new PreInstallScriptResult
			{
				Succeeded = false,
				ExitCode = -1,
				ErrorMessage = ex.Message
			};
		}
	}

	private static void KillSafely(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch
		{
			// best-effort cleanup; process may already have exited
		}
	}
}
