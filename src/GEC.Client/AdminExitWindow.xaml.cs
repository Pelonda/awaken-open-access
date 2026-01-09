using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GEC.Shared;

namespace GEC.Client
{
	public partial class AdminExitWindow : Window
	{
		public AdminExitWindow()
		{
			InitializeComponent();

			Loaded += (_, __) =>
			{
				AdminPasswordBox.Focus();
				Keyboard.Focus(AdminPasswordBox);
			};

			// Optional: press Enter to exit
			PreviewKeyDown += AdminExitWindow_PreviewKeyDown;
		}

		private void AdminExitWindow_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				Exit_Click(sender, new RoutedEventArgs());
				e.Handled = true;
			}
			else if (e.Key == Key.Escape)
			{
				Cancel_Click(sender, new RoutedEventArgs());
				e.Handled = true;
			}
		}

		private void Exit_Click(object sender, RoutedEventArgs e)
		{
			var password = AdminPasswordBox.Password ?? string.Empty;

			if (string.IsNullOrWhiteSpace(password))
			{
				MessageBox.Show(
					"Please enter the admin password.",
					"Admin Exit",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);

				AdminPasswordBox.Focus();
				return;
			}

			try
			{
				using var db = new CafeContext();

				var admins = db.AdminUsers
					.Where(a => a.IsActive)
					.ToList();

				if (admins.Count == 0)
				{
					MessageBox.Show(
						"No admin account is configured. Please contact the system administrator.",
						"Admin Exit",
						MessageBoxButton.OK,
						MessageBoxImage.Error);
					return;
				}

				var ok = admins.Any(a =>
					AdminAuthService.VerifyPassword(password, a.PasswordHash, a.PasswordSalt));

				if (!ok)
				{
					MessageBox.Show(
						"Invalid admin password.",
						"Admin Exit",
						MessageBoxButton.OK,
						MessageBoxImage.Error);

					AdminPasswordBox.Clear();
					AdminPasswordBox.Focus();
					return;
				}

				// Password accepted → exit client
				Application.Current.Shutdown();
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					"Unable to verify admin password.\n\n" + ex.Message,
					"Admin Exit",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}

		private void Cancel_Click(object sender, RoutedEventArgs e)
		{
			AdminPasswordBox.Clear();
			Close();
		}
	}
}
