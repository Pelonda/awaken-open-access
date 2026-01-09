using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using GEC.Shared;

namespace GEC.Server
{
	public partial class NewSessionWindow : Window
	{
		private readonly int _computerId;

		public NewSessionWindow(int computerId, string computerName)
		{
			InitializeComponent();

			_computerId = computerId;
			ComputerNameText.Text = $"New Session for {computerName}";

			GeneratePin();
		}

		private void GeneratePin()
		{
			PinText.Text = PinGenerator.GeneratePin(6);
		}

		private void RegeneratePin_Click(object sender, RoutedEventArgs e)
		{
			GeneratePin();
		}

		private void Ok_Click(object sender, RoutedEventArgs e)
		{
			// Duration
			if (DurationCombo.SelectedItem is not ComboBoxItem item ||
				!int.TryParse(item.Content?.ToString(), out var minutes) ||
				minutes <= 0)
			{
				MessageBox.Show("Invalid duration.", "New Session",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			// PIN
			var pin = (PinText.Text ?? string.Empty).Trim();

			if (pin.Length < 4 || pin.Length > 12)
			{
				MessageBox.Show("PIN must be between 4 and 12 characters.", "New Session",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			// Optional: enforce digits-only PIN (recommended)
			if (!pin.All(char.IsDigit))
			{
				MessageBox.Show("PIN must contain only numbers.", "New Session",
					MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			try
			{
				using var db = new CafeContext();

				// Ensure computer exists
				var comp = db.Computers.Find(_computerId);
				if (comp == null)
				{
					MessageBox.Show($"Computer ID {_computerId} not found.", "New Session",
						MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				// Do not allow multiple active sessions for same computer
				var existingActive = db.Sessions.AsNoTracking()
					.Any(s => s.ComputerId == _computerId && s.IsActive);

				if (existingActive)
				{
					MessageBox.Show("This computer already has an active session. End it first.", "New Session",
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}

				// Optional: Do not allow duplicate active PINs across computers
				// (Enable if you want PINs to be unique while active)
				var pinInUse = db.Sessions.AsNoTracking()
					.Any(s => s.IsActive && s.PinCode == pin);

				if (pinInUse)
				{
					MessageBox.Show("That PIN is already in use. Click Regenerate PIN.", "New Session",
						MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}

				var now = DateTime.UtcNow;

				var session = new Session
				{
					ComputerId = _computerId,
					PinCode = pin,
					StartTime = now,
					EndTime = now.AddMinutes(minutes),
					IsActive = true,
					CreatedBy = "Admin"
				};

				db.Sessions.Add(session);

				// Update computer status + heartbeat
				comp.Status = ComputerStatus.InUse;
				comp.LastHeartbeat = now;

				db.SaveChanges();

				MessageBox.Show(
					$"Session created.\nPIN: {session.PinCode}",
					"Global Center Education",
					MessageBoxButton.OK,
					MessageBoxImage.Information);

				DialogResult = true;
				Close();
			}
			catch (DbUpdateException ex)
			{
				// This usually contains FK/constraint/unique/null/length errors
				var sb = new StringBuilder();
				sb.AppendLine("Failed to create session.");
				sb.AppendLine();
				sb.AppendLine("Inner exception:");
				sb.AppendLine(ex.InnerException?.Message ?? "(none)");
				sb.AppendLine();
				sb.AppendLine("Details:");
				sb.AppendLine(ex.Message);

				MessageBox.Show(sb.ToString(),
					"Global Center Education",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					"Failed to create session.\n\n" + ex,
					"Global Center Education",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}
	}
}
