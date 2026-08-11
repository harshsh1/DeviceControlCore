using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DeviceControlCore.Models;

namespace DeviceControlCore.Services
{
	 public interface IStateService
	  {
		SystemState CurrentState { get; }

		bool TryTransitionTo(SystemState newState, string errorMessage);
	}
	  
	
}
