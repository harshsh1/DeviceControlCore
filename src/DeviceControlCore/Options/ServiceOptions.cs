using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DeviceControlCore.Options
{
	public class ServiceOptions
	{
		public const string SectionName = "DeviceControl";

		public required string UpdateStateDirectory { get; init; }

		public int PreInstallScriptTimeoutSeconds { get; init; } = 30;

		public int PeripheralPingIntervalSeconds { get; init; } = 10;

		public int PeripheralAckTimeoutSeconds { get; init; } = 2;

		public required string OsSettingsStateFilePath { get; init; }

		public required string AuditLogPath { get; init; }
	}
}
