using System.IO.Compression;
using System.Text.Json;
using DeviceControlCore.Models;
using DeviceControlCore.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceControlCore.Services;

public sealed class UpdateService : IUpdateService
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly IStateService _stateService;
	private readonly IPreInstallScriptRunner _scriptRunner;
	private readonly ServiceOptions _options;
	private readonly ILogger<UpdateService> _logger;

	public UpdateService(
		IStateService stateService,
		IPreInstallScriptRunner scriptRunner,
		IOptions<ServiceOptions> options,
		ILogger<UpdateService> logger)
	{
		_stateService = stateService;
		_scriptRunner = scriptRunner;
		_options = options.Value;
		_logger = logger;
	}

	public async Task<UpdateResult> InstallAsync(string packagePath, CancellationToken cancellationToken)
	{
		if (!_stateService.TryTransitionTo(SystemState.Updating, $"update requested: {packagePath}"))
		{
			return Fail($"Cannot start update while system is in {_stateService.CurrentState} state.");
		}

		string packageDirectory;
		PackageManifest manifest;
		try
		{
			packageDirectory = ResolvePackageDirectory(packagePath);
			manifest = ReadManifest(packageDirectory);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Package validation failed for {PackagePath}", packagePath);
			_stateService.TryTransitionTo(SystemState.Idle, "update package validation failed");
			return Fail($"Package validation failed: {ex.Message}");
		}

		try
		{
			var state = LoadVersionState();
			var previousVersion = state.CurrentVersion;

			_logger.LogInformation(
				"Starting update to version {NewVersion} (current: {CurrentVersion})",
				manifest.Version, previousVersion);

			var scriptResult = await RunPreInstallScriptAsync(packageDirectory, cancellationToken);

			if (!scriptResult.Succeeded)
			{
				_logger.LogError(
					"Pre-install script failed for version {NewVersion}: {Error}",
					manifest.Version, scriptResult.ErrorMessage);
				_logger.LogWarning(
					"Rolling back: current_version remains {Version}, last_known_good_version unchanged ({LastKnownGood})",
					previousVersion, state.LastKnownGoodVersion);

				state.History.Add(new InstallHistoryEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					Version = manifest.Version,
					Succeeded = false,
					Detail = $"Pre-install script failed: {scriptResult.ErrorMessage}. Rolled back to {previousVersion}."
				});
				SaveVersionState(state);

				_stateService.TryTransitionTo(SystemState.Idle, "rollback completed after pre-install failure");
				return Fail($"Update to {manifest.Version} failed pre-install checks; rolled back, current version remains {previousVersion}.");
			}

			state.LastKnownGoodVersion = previousVersion;
			state.CurrentVersion = manifest.Version;
			state.History.Add(new InstallHistoryEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				Version = manifest.Version,
				Succeeded = true,
				Detail = $"Installed successfully; last_known_good_version set to {previousVersion}."
			});
			SaveVersionState(state);

			_logger.LogInformation(
				"Update succeeded: current_version={CurrentVersion}, last_known_good_version={LastKnownGood}",
				state.CurrentVersion, state.LastKnownGoodVersion);

			_stateService.TryTransitionTo(SystemState.Idle, "update completed successfully");
			return new UpdateResult { Succeeded = true, Message = $"Update to {manifest.Version} completed successfully." };
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error during update to {NewVersion}", manifest.Version);
			_stateService.TryTransitionTo(SystemState.Error, "unexpected error during update");
			return Fail($"Update failed unexpectedly: {ex.Message}");
		}
	}

	private string ResolvePackageDirectory(string packagePath)
	{
		if (Directory.Exists(packagePath))
		{
			return packagePath;
		}

		if (File.Exists(packagePath) && Path.GetExtension(packagePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
		{
			var stagingDirectory = Path.Combine(_options.UpdateStateDirectory, "staging", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(stagingDirectory);
			ZipFile.ExtractToDirectory(packagePath, stagingDirectory);
			_logger.LogInformation("Extracted package {PackagePath} to {StagingDirectory}", packagePath, stagingDirectory);
			return stagingDirectory;
		}

		throw new FileNotFoundException($"Package path '{packagePath}' does not exist or is not a directory/.zip file.");
	}

	private static PackageManifest ReadManifest(string packageDirectory)
	{
		var manifestPath = Path.Combine(packageDirectory, "manifest.json");
		if (!File.Exists(manifestPath))
		{
			throw new FileNotFoundException($"manifest.json not found in package at '{packageDirectory}'.");
		}

		var json = File.ReadAllText(manifestPath);
		var manifest = JsonSerializer.Deserialize<PackageManifest>(json, JsonOptions);

		if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Version))
		{
			throw new InvalidOperationException("manifest.json is missing required 'name' or 'version' fields.");
		}

		return manifest;
	}

	private async Task<PreInstallScriptResult> RunPreInstallScriptAsync(string packageDirectory, CancellationToken cancellationToken)
	{
		var scriptFileName = OperatingSystem.IsWindows() ? "pre-install.bat" : "pre-install.sh";
		var scriptPath = Path.Combine(packageDirectory, scriptFileName);

		if (!File.Exists(scriptPath))
		{
			_logger.LogInformation("No pre-install script found at {ScriptPath}; skipping hook", scriptPath);
			return new PreInstallScriptResult { Succeeded = true, ExitCode = 0 };
		}

		using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.PreInstallScriptTimeoutSeconds));
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

		return await _scriptRunner.RunAsync(Path.GetFullPath(scriptPath), Path.GetFullPath(packageDirectory), linkedCts.Token);
	}

	private VersionState LoadVersionState()
	{
		if (!File.Exists(VersionStateFilePath))
		{
			return new VersionState { CurrentVersion = "none", LastKnownGoodVersion = "none" };
		}

		var json = File.ReadAllText(VersionStateFilePath);
		return JsonSerializer.Deserialize<VersionState>(json, JsonOptions)
			?? new VersionState { CurrentVersion = "none", LastKnownGoodVersion = "none" };
	}

	private void SaveVersionState(VersionState state)
	{
		Directory.CreateDirectory(_options.UpdateStateDirectory);
		var json = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
		File.WriteAllText(VersionStateFilePath, json);
	}

	private string VersionStateFilePath => Path.Combine(_options.UpdateStateDirectory, "version-state.json");

	private static UpdateResult Fail(string message) => new() { Succeeded = false, Message = message };
}
