using System;
using System.Text;

namespace GEC.Shared
{
	public static class PinGenerator
	{
		// Use a single Random instance for the whole app (thread-safe)
		private static readonly Random _rng = new Random();
		private static readonly object _lock = new();

		public static string GeneratePin(int length = 6)
		{
			if (length <= 0)
				throw new ArgumentOutOfRangeException(nameof(length));

			var sb = new StringBuilder(length);

			lock (_lock)
			{
				for (int i = 0; i < length; i++)
				{
					int digit = _rng.Next(0, 10);   // 0–9
					sb.Append(digit);
				}
			}

			return sb.ToString();
		}
	}
}
