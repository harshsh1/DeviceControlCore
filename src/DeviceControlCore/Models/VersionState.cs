using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeviceControlCore.Models
{
	public sealed class VersionState
	{
		public required string CurrentVersion { get; set; }
		public required string LastKnownGoodVersion { get; set; }
		public List<InstallHistoryEntry> History { get; init; } = [];
	}

	public sealed class InstallHistoryEntry
	{
		public required DateTimeOffset Timestamp { get; init; }
		public required string Version { get; init; }
		public required bool Succeeded { get; init; }
		public required string Detail { get; init; }
	}
}
