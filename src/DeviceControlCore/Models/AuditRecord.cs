using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeviceControlCore.Models
{
	public sealed class AuditRecord
	{
		public required DateTimeOffset Timestamp { get; init; }
		public required string InvokedBy { get; init; }
		public required string Setting { get; init; }
		public required string OldValue { get; init; }
		public required string NewValue { get; init; }
	}
}
