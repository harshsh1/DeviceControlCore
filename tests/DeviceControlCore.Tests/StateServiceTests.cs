using DeviceControlCore.Models;
using DeviceControlCore.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeviceControlCore.Tests;

public class StateServiceTests
{
	private readonly StateService _sut = new(NullLogger<StateService>.Instance);

	[Fact]
	public void InitialState_IsIdle()
	{
		Assert.Equal(SystemState.Idle, _sut.CurrentState);
	}

	[Fact]
	public void TryTransitionTo_ValidTransition_SucceedsAndUpdatesState()
	{
		var result = _sut.TryTransitionTo(SystemState.Running, "start command");

		Assert.True(result);
		Assert.Equal(SystemState.Running, _sut.CurrentState);
	}

	[Fact]
	public void TryTransitionTo_InvalidTransition_IsRejectedAndStateUnchanged()
	{
		var result = _sut.TryTransitionTo(SystemState.Error, "not a valid path from idle");

		Assert.False(result);
		Assert.Equal(SystemState.Idle, _sut.CurrentState);
	}

	[Fact]
	public void TryTransitionTo_DuplicateTransition_IsRejectedAndIdempotent()
	{
		_sut.TryTransitionTo(SystemState.Maintenance, "signal safety_interlock");

		var duplicate = _sut.TryTransitionTo(SystemState.Maintenance, "repeated peripheral timeout");

		Assert.False(duplicate);
		Assert.Equal(SystemState.Maintenance, _sut.CurrentState);
	}

	[Fact]
	public void TryTransitionTo_FromMaintenanceToRunning_IsAllowed()
	{
		_sut.TryTransitionTo(SystemState.Maintenance, "signal safety_interlock");

		var recovered = _sut.TryTransitionTo(SystemState.Running, "start command after recovery");

		Assert.True(recovered);
		Assert.Equal(SystemState.Running, _sut.CurrentState);
	}

	[Fact]
	public void TryTransitionTo_FromErrorToRunning_IsAllowed()
	{
		_sut.TryTransitionTo(SystemState.Updating, "update requested");
		_sut.TryTransitionTo(SystemState.Error, "unexpected error during update");

		var recovered = _sut.TryTransitionTo(SystemState.Running, "start command after recovery");

		Assert.True(recovered);
		Assert.Equal(SystemState.Running, _sut.CurrentState);
	}

	[Fact]
	public void TryTransitionTo_FromMaintenanceToUpdating_IsRejected()
	{
		_sut.TryTransitionTo(SystemState.Maintenance, "signal safety_interlock");

		var result = _sut.TryTransitionTo(SystemState.Updating, "update requested during maintenance");

		Assert.False(result);
		Assert.Equal(SystemState.Maintenance, _sut.CurrentState);
	}
}
