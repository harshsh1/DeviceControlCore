using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeviceControlCore.Models
{
	public sealed class PackageManifest
	{
		public required string Name { get; init; }
		public required string Version { get; init; }
	}
}
