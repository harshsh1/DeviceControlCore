using DeviceControlCore.Models;
using Microsoft.Extensions.Logging;

namespace DeviceControlCore.Services;

public sealed class StateService : IStateService
{
	private static readonly Dictionary<SystemState, List<SystemState>> AllowedTransitions = new()
	{
		[SystemState.Idle] = [SystemState.Running, SystemState.Updating, SystemState.Maintenance],
		[SystemState.Running] = [SystemState.Idle, SystemState.Updating, SystemState.Maintenance],
		[SystemState.Updating] = [SystemState.Idle, SystemState.Maintenance, SystemState.Error],
		[SystemState.Maintenance] = [SystemState.Idle, SystemState.Running],
		[SystemState.Error] = [SystemState.Idle, SystemState.Running],
	};


	private readonly ILogger<StateService> _logger;
	private readonly object _gate = new();
	private SystemState _currentState = SystemState.Idle;

	public StateService(ILogger<StateService> logger)
	{
		_logger = logger;
	}

	public SystemState CurrentState
	{
		get { lock (_gate) { return _currentState; } }
	}

	public bool TryTransitionTo(SystemState newState, string reason)
	{
		lock (_gate)
		{
			if (_currentState == newState)
			{
				_logger.LogDebug(
					"Ignoring duplicate transition request to {State} (reason: {Reason})",
					newState, reason);
				return false;
			}

			if (!AllowedTransitions[_currentState].Contains(newState))
			{
				_logger.LogWarning(
					"Rejected invalid state transition {From} -> {To} (reason: {Reason})",
					_currentState, newState, reason);
				return false;
			}

			var previous = _currentState;
			_currentState = newState;
			_logger.LogInformation(
				"State transition: {From} -> {To} (reason: {Reason})",
				previous, newState, reason);
			return true;
		}
	}
}
