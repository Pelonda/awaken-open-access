using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GEC.Shared;

namespace GEC.Client
{
	public partial class LockWindow : Window
	{
		private readonly int _computerId;

		public LockWindow()
		{
			InitializeComponent();

			try
			{
				// Finds/creates the Computer row
				_computerId = ComputerRegistry.GetOrRegisterComputer();
			}
			catch (Exception ex)
			{
				_computerId = 0; // force login to fail safely

				MessageBox.Show(
					"Unable to register this computer with the server.\n\n" + ex.Message,
					"Client Registration Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}

			Loaded += LockWindow_Loaded;

			// NOTE: Do NOT wire KeyDown here if XAML already has KeyDown="Window_KeyDown"
			// KeyDown += Window_KeyDown;
		}

		private void LockWindow_Loaded(object sender, RoutedEventArgs e)
		{
			if (_computerId > 0)
			{
				var machineName = Environment.MachineName;
				ComputerText.Text = $"Computer: {machineName} (ID: {_computerId})";
			}
			else
			{
				ComputerText.Text = "Error detecting computer ID.";
			}

			StatusText.Text = "";
			PinBox.Password = "";

			// Focus for immediate typing
			Activate();
			Focus();
			Keyboard.Focus(PinBox);
		}

		private void Login_Click(object sender, RoutedEventArgs e)
		{
			StatusText.Text = "";

			var pin = PinBox.Password?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(pin))
			{
				StatusText.Text = "Please enter a PIN.";
				Keyboard.Focus(PinBox);
				return;
			}

			if (_computerId <= 0)
			{
				MessageBox.Show(
					"This client could not register a valid Computer ID.\nPlease contact the administrator.",
					"Client Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
				return;
			}

			try
			{
				using var db = new CafeContext();
				var now = DateTime.UtcNow;

				// Active sessions with this PIN (latest first)
				var sessionsWithPin = db.Sessions
					.Where(s => s.PinCode == pin && s.IsActive)
					.OrderByDescending(s => s.Id)
					.ToList();

				if (sessionsWithPin.Count == 0)
				{
					StatusText.Text =
						"Invalid or expired PIN.\nAsk the admin to create a new session for you.";
					PinBox.Password = "";
					Keyboard.Focus(PinBox);
					return;
				}

				// Prefer a session for THIS computer
				var session = sessionsWithPin.FirstOrDefault(s => s.ComputerId == _computerId)
							  ?? sessionsWithPin.First();

				// If PIN belongs to another computer, explain clearly
				if (session.ComputerId != _computerId)
				{
					StatusText.Text =
						$"This PIN is for Computer ID {session.ComputerId}, but this client is Computer ID {_computerId}.\n" +
						"Please log in on the correct computer or ask the admin to fix it.";
					PinBox.Password = "";
					Keyboard.Focus(PinBox);
					return;
				}

				// Defensive time check
				if (session.EndTime <= now)
				{
					StatusText.Text =
						"This session has already expired.\nAsk the admin to start a new session.";
					PinBox.Password = "";
					Keyboard.Focus(PinBox);
					return;
				}

				// Optional: bump heartbeat immediately (helps server not show Offline)
				var comp = db.Computers.Find(_computerId);
				if (comp != null)
				{
					comp.LastHeartbeat = now;
					if (comp.Status != ComputerStatus.InUse)
						comp.Status = ComputerStatus.InUse;
					db.SaveChanges();
				}

				// Show welcome
				var welcome = new WelcomeWindow { Owner = this };
				welcome.ShowDialog();

				// Show time bar (floating)
				var bar = new SessionBarWindow(session.Id, _computerId, session.EndTime);
				bar.SessionEnded += Bar_SessionEnded;
				bar.Show();

				Hide();
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					"An error occurred while trying to log in:\n\n" + ex.Message,
					"Login Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}

		private void Bar_SessionEnded(object? sender, EventArgs e)
		{
			Dispatcher.Invoke(() =>
			{
				PinBox.Password = "";
				StatusText.Text = "";

				Show();
				Activate();
				Focus();
				Keyboard.Focus(PinBox);
			});
		}

		// 🔐 ADMIN HOTKEY: Ctrl + Shift + A  (wired from XAML KeyDown)
		private void Window_KeyDown(object sender, KeyEventArgs e)
		{
			if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
				Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
				e.Key == Key.A)
			{
				var adminWindow = new AdminExitWindow { Owner = this };
				adminWindow.ShowDialog();
			}
		}
	}
}
