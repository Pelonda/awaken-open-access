using System;
using System.Security.Cryptography;

namespace GEC.Shared
{
	public static class AdminAuthService
	{
		private const int SaltSize = 16;
		private const int KeySize = 32;
		private const int Iterations = 100_000;

		public static void CreatePasswordHash(string password, out byte[] hash, out byte[] salt)
		{
			if (string.IsNullOrWhiteSpace(password))
				throw new ArgumentException("Password cannot be empty.", nameof(password));

			salt = RandomNumberGenerator.GetBytes(SaltSize);

			hash = Rfc2898DeriveBytes.Pbkdf2(
				password,
				salt,
				Iterations,
				HashAlgorithmName.SHA256,
				KeySize);
		}

		public static bool VerifyPassword(string password, byte[] hash, byte[] salt)
		{
			if (string.IsNullOrWhiteSpace(password)) return false;
			if (hash == null || salt == null) return false;
			if (hash.Length != KeySize || salt.Length != SaltSize) return false;

			var computed = Rfc2898DeriveBytes.Pbkdf2(
				password,
				salt,
				Iterations,
				HashAlgorithmName.SHA256,
				KeySize);

			return CryptographicOperations.FixedTimeEquals(computed, hash);
		}
	}
}
