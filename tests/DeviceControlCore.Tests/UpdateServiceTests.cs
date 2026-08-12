using DeviceControlCore.Models;
using DeviceControlCore.Options;
using DeviceControlCore.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using Xunit;

namespace DeviceControlCore.Tests;

public class UpdateServiceTests : IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly string _rootDirectory;
	private readonly string _stateDirectory;
	private readonly string _packageDirectory;
	private readonly Mock<IStateService> _stateService;
	private readonly Mock<IPreInstallScriptRunner> _scriptRunner;
	private readonly UpdateService _sut;

	public UpdateServiceTests()
	{
		_rootDirectory = Path.Combine(Path.GetTempPath(), "dcc-tests-" + Guid.NewGuid().ToString("N"));
		_stateDirectory = Path.Combine(_rootDirectory, "state");
		_packageDirectory = Path.Combine(_rootDirectory, "package");
		Directory.CreateDirectory(_stateDirectory);
		Directory.CreateDirectory(_packageDirectory);

		_stateService = new Mock<IStateService>();
		_scriptRunner = new Mock<IPreInstallScriptRunner>();

		var options = Microsoft.Extensions.Options.Options.Create(new ServiceOptions
		{
			UpdateStateDirectory = _stateDirectory,
			PreInstallScriptTimeoutSeconds = 5,
			OsSettingsStateFilePath = Path.Combine(_rootDirectory, "os-settings.json"),
			AuditLogPath = Path.Combine(_rootDirectory, "audit.jsonl")
		});

		_sut = new UpdateService(_stateService.Object, _scriptRunner.Object, options, NullLogger<UpdateService>.Instance);
	}

	public void Dispose()
	{
		if (Directory.Exists(_rootDirectory))
		{
			Directory.Delete(_rootDirectory, recursive: true);
		}
	}

	private void WriteManifest(string version) =>
		File.WriteAllText(
			Path.Combine(_packageDirectory, "manifest.json"),
			$$"""{"name": "device-control-core", "version": "{{version}}"}""");

	private void WriteExistingVersionState(string currentVersion, string lastKnownGoodVersion) =>
		File.WriteAllText(
			Path.Combine(_stateDirectory, "version-state.json"),
			$$"""{"currentVersion": "{{currentVersion}}", "lastKnownGoodVersion": "{{lastKnownGoodVersion}}", "history": []}""");

	private string ScriptFileName => OperatingSystem.IsWindows() ? "pre-install.bat" : "pre-install.sh";

	private VersionState ReadVersionState() =>
		JsonSerializer.Deserialize<VersionState>(
			File.ReadAllText(Path.Combine(_stateDirectory, "version-state.json")), JsonOptions)!;

	[Fact]
	public async Task InstallAsync_SuccessfulPreInstall_ActivatesNewVersionAndUpdatesLastKnownGood()
	{
		WriteManifest("2.0.0");
		WriteExistingVersionState(currentVersion: "1.0.0", lastKnownGoodVersion: "1.0.0");
		_stateService.Setup(s => s.TryTransitionTo(It.IsAny<SystemState>(), It.IsAny<string>())).Returns(true);

		var result = await _sut.InstallAsync(_packageDirectory, CancellationToken.None);

		Assert.True(result.Succeeded);

		var state = ReadVersionState();
		Assert.Equal("2.0.0", state.CurrentVersion);
		Assert.Equal("1.0.0", state.LastKnownGoodVersion);
		Assert.Single(state.History);
		Assert.True(state.History[0].Succeeded);

		_stateService.Verify(s => s.TryTransitionTo(SystemState.Updating, It.IsAny<string>()), Times.Once);
		_stateService.Verify(s => s.TryTransitionTo(SystemState.Idle, "update completed successfully"), Times.Once);
	}

	[Fact]
	public async Task InstallAsync_PreInstallScriptFails_RollsBackAndKeepsCurrentVersion()
	{
		WriteManifest("2.0.0");
		WriteExistingVersionState(currentVersion: "1.0.0", lastKnownGoodVersion: "1.0.0");
		File.WriteAllText(Path.Combine(_packageDirectory, ScriptFileName), "exit 1");

		_stateService.Setup(s => s.TryTransitionTo(It.IsAny<SystemState>(), It.IsAny<string>())).Returns(true);
		_scriptRunner
			.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PreInstallScriptResult { Succeeded = false, ExitCode = 1, ErrorMessage = "simulated failure" });

		var result = await _sut.InstallAsync(_packageDirectory, CancellationToken.None);

		Assert.False(result.Succeeded);

		var state = ReadVersionState();
		Assert.Equal("1.0.0", state.CurrentVersion);
		Assert.Equal("1.0.0", state.LastKnownGoodVersion);
		Assert.Single(state.History);
		Assert.False(state.History[0].Succeeded);

		_stateService.Verify(s => s.TryTransitionTo(SystemState.Idle, "rollback completed after pre-install failure"), Times.Once);
	}

	[Fact]
	public async Task InstallAsync_MissingManifest_FailsValidationAndReturnsToIdleWithoutRunningScript()
	{
		_stateService.Setup(s => s.TryTransitionTo(It.IsAny<SystemState>(), It.IsAny<string>())).Returns(true);

		var result = await _sut.InstallAsync(_packageDirectory, CancellationToken.None);

		Assert.False(result.Succeeded);
		Assert.Contains("validation failed", result.Message);

		_scriptRunner.Verify(
			r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Never);
		_stateService.Verify(s => s.TryTransitionTo(SystemState.Idle, "update package validation failed"), Times.Once);
	}
}
