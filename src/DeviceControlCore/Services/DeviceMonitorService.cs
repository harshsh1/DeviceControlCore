using DeviceControlCore.Models;
using DeviceControlCore.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeviceControlCore.Services;

public sealed class DeviceMonitorService : BackgroundService, IDeviceMonitorService
{
	private readonly IStateService _stateService;
	private readonly ServiceOptions _options;
	private readonly ILogger<DeviceMonitorService> _logger;
	private volatile bool _ackEnabled = true;

	public DeviceMonitorService(
		IStateService stateService,
		IOptions<ServiceOptions> options,
		ILogger<DeviceMonitorService> logger)
	{
		_stateService = stateService;
		_options = options.Value;
		_logger = logger;
	}

	public void SetAckEnabled(bool enabled)
	{
		_ackEnabled = enabled;
		_logger.LogInformation(
			"Peripheral ACK simulation set to {Enabled}",
			enabled ? "ON (responding)" : "OFF (not responding)");
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation(
			"Peripheral keep-alive monitor started (ping interval: {Interval}s, ack timeout: {Timeout}s)",
			_options.PeripheralPingIntervalSeconds, _options.PeripheralAckTimeoutSeconds);

		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PeripheralPingIntervalSeconds));

		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				await PingPeripheralAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
			// expected during graceful shutdown
		}

		_logger.LogInformation("Peripheral keep-alive monitor stopped");
	}

	private async Task PingPeripheralAsync(CancellationToken cancellationToken)
	{
		_logger.LogDebug("Sending keep-alive ping to peripheral");

		var ackReceived = await WaitForAckAsync(cancellationToken);

		if (ackReceived)
		{
			_logger.LogDebug("Peripheral ACK received");
			return;
		}

		var transitioned = _stateService.TryTransitionTo(SystemState.Maintenance, "peripheral keep-alive timeout");

		if (transitioned)
		{
			_logger.LogError(
				"ALERT: Peripheral keep-alive timeout — no ACK received within {TimeoutSeconds}s. Entering maintenance mode.",
				_options.PeripheralAckTimeoutSeconds);
		}
		else
		{
			_logger.LogDebug(
				"Peripheral keep-alive timeout persists (system already in {State} state); suppressing duplicate alert",
				_stateService.CurrentState);
		}
	}

	private async Task<bool> WaitForAckAsync(CancellationToken cancellationToken)
	{
		if (_ackEnabled)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
			return true;
		}

		await Task.Delay(TimeSpan.FromSeconds(_options.PeripheralAckTimeoutSeconds), cancellationToken);
		return false;
	}
}
