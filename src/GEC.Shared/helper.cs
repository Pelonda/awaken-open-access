using Microsoft.EntityFrameworkCore;

namespace GEC.Shared
{
	public static class DbDebugHelper
	{
		public static string GetDatabaseInfo()
		{
			using var db = new CafeContext();
			var connection = db.Database.GetDbConnection();

			return $"Server={connection.DataSource}; Database={connection.Database}";
		}
	}
}
