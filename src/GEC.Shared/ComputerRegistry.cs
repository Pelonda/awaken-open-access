using System;
using System.Linq;

namespace GEC.Shared
{
	public static class ComputerRegistry
	{
		/// <summary>
		/// Returns the DB Computer.Id for this physical machine.
		/// If not found, creates a new Computer row using the machine name.
		/// </summary>
		public static int GetOrRegisterComputer()
		{
			using var db = new CafeContext();

			var machineName = Environment.MachineName;

			// Try to find an existing record for this machine
			var comp = db.Computers
						 .SingleOrDefault(c => c.Name == machineName);

			if (comp == null)
			{
				// Create a new record
				comp = new Computer
				{
					Name = machineName,
					Status = ComputerStatus.Idle,
					LastHeartbeat = null
				};

				db.Computers.Add(comp);
				db.SaveChanges();
			}

			return comp.Id;
		}
	}
}
