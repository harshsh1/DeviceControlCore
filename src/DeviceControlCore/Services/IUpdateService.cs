namespace DeviceControlCore.Services;

public interface IUpdateService
{
	Task<UpdateResult> InstallAsync(string packagePath, CancellationToken cancellationToken);
}

public class UpdateResult
{
	public required bool Succeeded { get; init; }
	public required string Message { get; init; }
}
