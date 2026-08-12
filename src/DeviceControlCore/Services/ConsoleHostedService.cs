using DeviceControlCore.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeviceControlCore.Services;

public sealed class ConsoleHostedService : BackgroundService
{
	private readonly IStateService _stateService;
	private readonly IUpdateService _updateService;
	private readonly IDeviceMonitorService _deviceMonitor;
	private readonly IOsSettingsService _osSettingsService;
	private readonly IHostApplicationLifetime _appLifetime;
	private readonly ILogger<ConsoleHostedService> _logger;

	private CancellationTokenSource? _workerCts;
	private Task? _workerTask;

	public ConsoleHostedService(
		IStateService stateService,
		IUpdateService updateService,
		IDeviceMonitorService deviceMonitor,
		IOsSettingsService osSettingsService,
		IHostApplicationLifetime appLifetime,
		ILogger<ConsoleHostedService> logger)
	{
		_stateService = stateService;
		_updateService = updateService;
		_deviceMonitor = deviceMonitor;
		_osSettingsService = osSettingsService;
		_appLifetime = appLifetime;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation(
			"Device Control Core ready. Commands: start | stop | signal safety_interlock | " +
			"update --package <path> | device peripheral ack on|off | os set-timezone <tz> | status | exit");

		while (!stoppingToken.IsCancellationRequested)
		{
			Console.Write("> ");

			string? line;
			try
			{
				line = await Console.In.ReadLineAsync(stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}

			if (line is null)
			{
				break;
			}

			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			bool shouldExit;
			try
			{
				shouldExit = await DispatchAsync(line.Trim(), stoppingToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Unhandled error while processing command '{Command}'", line);
				continue;
			}

			if (shouldExit)
			{
				break;
			}
		}

		await StopWorkerLoopAsync();
		_logger.LogInformation("Final status — state: {State}", _stateService.CurrentState);
		_appLifetime.StopApplication();
	}

	private async Task<bool> DispatchAsync(string input, CancellationToken cancellationToken)
	{
		var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var command = parts[0].ToLowerInvariant();

		switch (command)
		{
			case "start":
				StartWorkerLoop();
				break;

			case "stop":
				await HandleStopAsync();
				break;

			case "signal":
				if (parts.Length < 2)
				{
					_logger.LogWarning("Usage: signal <name>");
					break;
				}
				await HandleSignalAsync(parts[1]);
				break;

			case "update":
				await HandleUpdateAsync(parts, cancellationToken);
				break;

			case "device":
				HandleDevice(parts);
				break;

			case "os":
				await HandleOsAsync(parts, cancellationToken);
				break;

			case "status":
				_logger.LogInformation("Status: {State}", _stateService.CurrentState);
				break;

			case "exit":
				_logger.LogInformation("Exit requested. Shutting down.");
				return true;

			default:
				_logger.LogWarning("Unknown command '{Command}'. Type a supported command.", command);
				break;
		}

		return false;
	}

	private void StartWorkerLoop()
	{
		if (_workerTask is { IsCompleted: false })
		{
			_logger.LogWarning("Worker loop already running; ignoring start request.");
			return;
		}

		if (!_stateService.TryTransitionTo(SystemState.Running, "start command"))
		{
			_logger.LogWarning("Cannot start worker loop from state {State}", _stateService.CurrentState);
			return;
		}

		_workerCts = new CancellationTokenSource();
		var token = _workerCts.Token;
		_workerTask = Task.Run(() => RunWorkerLoopAsync(token), token);
		_logger.LogInformation("Worker loop started.");
	}

	private async Task RunWorkerLoopAsync(CancellationToken token)
	{
		try
		{
			while (!token.IsCancellationRequested)
			{
				if (_stateService.CurrentState != SystemState.Running)
				{
					_logger.LogInformation(
						"Worker loop detected state left Running ({State}); halting.",
						_stateService.CurrentState);
					break;
				}

				_logger.LogDebug("Worker loop tick (state: {State})", _stateService.CurrentState);
				await Task.Delay(TimeSpan.FromSeconds(5), token);
			}
		}
		catch (OperationCanceledException)
		{
			// expected on stop/cancel
		}

		_logger.LogInformation("Worker loop halted.");
	}

	private async Task StopWorkerLoopAsync()
	{
		if (_workerCts is null || _workerTask is null)
		{
			return;
		}

		_workerCts.Cancel();

		try
		{
			await _workerTask;
		}
		catch (OperationCanceledException)
		{
			// expected
		}

		_workerCts.Dispose();
		_workerCts = null;
		_workerTask = null;
	}

	private async Task HandleStopAsync()
	{
		var currentState = _stateService.CurrentState;

		if (currentState != SystemState.Running && currentState != SystemState.Maintenance)
		{
			_logger.LogWarning("Nothing to stop; system is already {State}.", currentState);
			return;
		}

		await StopWorkerLoopAsync();

		if (_stateService.TryTransitionTo(SystemState.Idle, "stop command"))
		{
			_logger.LogInformation("Worker loop stopped; system is idle.");
		}
	}

	private async Task HandleSignalAsync(string signalName)
	{
		if (!string.Equals(signalName, "safety_interlock", StringComparison.OrdinalIgnoreCase))
		{
			_logger.LogWarning("Unknown signal '{Signal}'", signalName);
			return;
		}

		var transitioned = _stateService.TryTransitionTo(SystemState.Maintenance, "safety_interlock signal");

		if (transitioned)
		{
			_logger.LogError("ALERT: Safety interlock triggered — entering maintenance mode.");
			await StopWorkerLoopAsync();
		}
		else
		{
			_logger.LogWarning(
				"Safety interlock signal received but system already in {State}; no action taken.",
				_stateService.CurrentState);
		}
	}

	private async Task HandleUpdateAsync(string[] parts, CancellationToken cancellationToken)
	{
		var packageIndex = Array.IndexOf(parts, "--package");
		if (packageIndex < 0 || packageIndex + 1 >= parts.Length)
		{
			_logger.LogWarning("Usage: update --package <path>");
			return;
		}

		await StopWorkerLoopAsync();

		var result = await _updateService.InstallAsync(parts[packageIndex + 1], cancellationToken);

		if (result.Succeeded)
		{
			_logger.LogInformation("Update result: {Message}", result.Message);
		}
		else
		{
			_logger.LogError("Update result: {Message}", result.Message);
		}
	}

	private void HandleDevice(string[] parts)
	{
		if (parts.Length < 4
			|| !string.Equals(parts[1], "peripheral", StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(parts[2], "ack", StringComparison.OrdinalIgnoreCase))
		{
			_logger.LogWarning("Usage: device peripheral ack on|off");
			return;
		}

		switch (parts[3].ToLowerInvariant())
		{
			case "on":
				_deviceMonitor.SetAckEnabled(true);
				break;
			case "off":
				_deviceMonitor.SetAckEnabled(false);
				break;
			default:
				_logger.LogWarning("Usage: device peripheral ack on|off");
				break;
		}
	}

	private async Task HandleOsAsync(string[] parts, CancellationToken cancellationToken)
	{
		if (parts.Length < 3 || !string.Equals(parts[1], "set-timezone", StringComparison.OrdinalIgnoreCase))
		{
			_logger.LogWarning("Usage: os set-timezone <timezone>");
			return;
		}

		var result = await _osSettingsService.SetTimezoneAsync(parts[2], "operator", cancellationToken);

		if (result.Succeeded)
		{
			_logger.LogInformation("OS setting result: {Message}", result.Message);
		}
		else
		{
			_logger.LogError("OS setting result: {Message}", result.Message);
		}
	}
}
