using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using GEC.Shared;

namespace GEC.Server
{
	public partial class MainWindow : Window
	{
		private readonly ObservableCollection<ComputerViewModel> _allComputers = new();
		private readonly ICollectionView _view;

		// Update countdown in UI every second (NO DB)
		private readonly DispatcherTimer _uiTimer;

		// Refresh statuses/sessions from DB every few seconds
		private readonly DispatcherTimer _dbTimer;

		// Heartbeat timeout (e.g., 2 seconds)
		private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(2);

		public MainWindow()
		{
			InitializeComponent();

			// Bind grid once to observable collection
			ComputersGrid.ItemsSource = _allComputers;

			// Filter view (so countdown can keep updating without replacing ItemsSource)
			_view = CollectionViewSource.GetDefaultView(_allComputers);
			_view.Filter = FilterPredicate;

			SeedDefaultAdminIfNeeded();

			// Load once
			LoadComputers();

			// UI countdown tick (1s)
			_uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			_uiTimer.Tick += (_, __) => UpdateCountdownOnly();
			_uiTimer.Start();

			// DB refresh tick (3s)
			_dbTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
			_dbTimer.Tick += async (_, __) => await RefreshFromDbAsync();
			_dbTimer.Start();
		}

		private bool FilterPredicate(object obj)
		{
			if (obj is not ComputerViewModel c) return false;

			var term = SearchBox.Text?.Trim();
			if (string.IsNullOrWhiteSpace(term)) return true;

			term = term.ToLowerInvariant();

			return (c.Name ?? "").ToLowerInvariant().Contains(term)
				|| c.Id.ToString().Contains(term)
				|| (c.Status ?? "").ToLowerInvariant().Contains(term);
		}

		private void SeedDefaultAdminIfNeeded()
		{
			try
			{
				using var db = new CafeContext();

				if (db.AdminUsers.Any())
					return;

				const string initialPassword = "ChangeMe#2025!";

				AdminAuthService.CreatePasswordHash(initialPassword, out var hash, out var salt);

				var admin = new AdminUser
				{
					Username = "admin",
					PasswordHash = hash,
					PasswordSalt = salt,
					IsActive = true,
					CreatedAt = DateTime.UtcNow
				};

				db.AdminUsers.Add(admin);
				db.SaveChanges();

				MessageBox.Show(
					"Initial admin account created.\n\n" +
					"Username: admin\n" +
					"Password: ChangeMe#2025!\n\n" +
					"Please change this password as soon as possible.",
					"Admin Setup",
					MessageBoxButton.OK,
					MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					"Failed to initialize admin user.\n\n" +
					"Error: " + ex.Message,
					"Admin Setup Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}

		private async Task RefreshFromDbAsync()
		{
			try
			{
				// Do DB work off the UI thread
				var fresh = await Task.Run(() => LoadComputersSnapshot());

				// Apply snapshot to UI collection on UI thread
				Dispatcher.Invoke(() =>
				{
					_allComputers.Clear();
					foreach (var vm in fresh.OrderBy(x => x.Id))
						_allComputers.Add(vm);

					_view.Refresh();
				});
			}
			catch
			{
				// avoid spamming popups; if you want, log later
			}
		}

		private void LoadComputers()
		{
			try
			{
				var fresh = LoadComputersSnapshot();

				_allComputers.Clear();
				foreach (var vm in fresh.OrderBy(x => x.Id))
					_allComputers.Add(vm);

				_view.Refresh();
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					"Failed to load computers.\n\n" + ex.Message,
					"Load Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}

		// ✅ Snapshot loader (NO DB updates!)
		private List<ComputerViewModel> LoadComputersSnapshot()
		{
			using var db = new CafeContext();
			var now = DateTime.UtcNow;

			var computers = db.Computers.ToList();
			var sessions = db.Sessions.ToList();

			var lastSessionByComputer = sessions
				.GroupBy(s => s.ComputerId)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Id).FirstOrDefault());

			var activeSessionByComputer = sessions
				.Where(s => s.IsActive)
				.GroupBy(s => s.ComputerId)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Id).FirstOrDefault());

			var data = computers.Select(c =>
			{
				activeSessionByComputer.TryGetValue(c.Id, out var activeSession);
				lastSessionByComputer.TryGetValue(c.Id, out var lastSession);

				// Compute status for display (do NOT write back)
				var displayStatus = ComputeStatus(c, activeSession, now);

				var info = GetRemainingInfo(displayStatus, activeSession, now);

				return new ComputerViewModel
				{
					Id = c.Id,
					Name = c.Name,
					Status = displayStatus.ToString(),
					LastPin = lastSession?.PinCode ?? string.Empty,

					// store active session info so UI timer can update countdown without DB
					ActiveStartUtc = activeSession?.StartTime,
					ActiveEndUtc = activeSession?.EndTime,
					IsActiveSession = activeSession != null && activeSession.IsActive,

					RemainingTime = info.Text,
					RemainingPercent = info.Percent,
					RemainingBrush = info.Brush,
				};
			}).ToList();

			return data;
		}

		private static ComputerStatus ComputeStatus(Computer c, Session? activeSession, DateTime nowUtc)
		{
			if (activeSession == null || !activeSession.IsActive)
				return ComputerStatus.Idle;

			if (!c.LastHeartbeat.HasValue)
				return ComputerStatus.Offline;

			return (nowUtc - c.LastHeartbeat.Value) > HeartbeatTimeout
				? ComputerStatus.Offline
				: ComputerStatus.InUse;
		}

		private static (string Text, double Percent, Brush Brush) GetRemainingInfo(
			ComputerStatus displayStatus,
			Session? activeSession,
			DateTime nowUtc)
		{
			if (displayStatus == ComputerStatus.Offline)
				return ("Offline", 0, Brushes.Gray);

			if (activeSession == null || !activeSession.IsActive)
				return ("Idle", 0, Brushes.Gray);

			if (activeSession.EndTime <= nowUtc)
				return ("Expired", 0, Brushes.Red);

			var totalSeconds = (activeSession.EndTime - activeSession.StartTime).TotalSeconds;
			var remainingSeconds = (activeSession.EndTime - nowUtc).TotalSeconds;

			if (totalSeconds <= 0)
				totalSeconds = Math.Max(remainingSeconds, 1);

			var percent = (remainingSeconds / totalSeconds) * 100.0;
			if (percent < 0) percent = 0;
			if (percent > 100) percent = 100;

			var remaining = activeSession.EndTime - nowUtc;

			Brush brush =
				remaining <= TimeSpan.FromMinutes(5) ? Brushes.Red :
				remaining <= TimeSpan.FromMinutes(15) ? Brushes.OrangeRed :
				Brushes.LimeGreen;

			return (remaining.ToString(@"hh\:mm\:ss"), percent, brush);
		}

		// ✅ this runs every 1 second (no DB)
		private void UpdateCountdownOnly()
		{
			var now = DateTime.UtcNow;

			foreach (var vm in _allComputers)
			{
				// only update active/inuse rows
				if (!vm.IsActiveSession || vm.ActiveEndUtc == null || vm.ActiveStartUtc == null)
					continue;

				// if offline, show Offline
				if (vm.Status.Equals(ComputerStatus.Offline.ToString(), StringComparison.OrdinalIgnoreCase))
				{
					vm.RemainingTime = "Offline";
					vm.RemainingPercent = 0;
					vm.RemainingBrush = Brushes.Gray;
					continue;
				}

				var end = vm.ActiveEndUtc.Value;
				var start = vm.ActiveStartUtc.Value;

				if (end <= now)
				{
					vm.RemainingTime = "Expired";
					vm.RemainingPercent = 0;
					vm.RemainingBrush = Brushes.Red;
					continue;
				}

				var remaining = end - now;

				var totalSeconds = (end - start).TotalSeconds;
				var remainingSeconds = remaining.TotalSeconds;

				if (totalSeconds <= 0)
					totalSeconds = Math.Max(remainingSeconds, 1);

				var percent = (remainingSeconds / totalSeconds) * 100.0;
				if (percent < 0) percent = 0;
				if (percent > 100) percent = 100;

				vm.RemainingTime = remaining.ToString(@"hh\:mm\:ss");
				vm.RemainingPercent = percent;

				vm.RemainingBrush =
					remaining <= TimeSpan.FromMinutes(5) ? Brushes.Red :
					remaining <= TimeSpan.FromMinutes(15) ? Brushes.OrangeRed :
					Brushes.LimeGreen;
			}
		}

		private void ApplyFilter() => _view.Refresh();

		private void Refresh_Click(object sender, RoutedEventArgs e) => LoadComputers();
		private void Search_Click(object sender, RoutedEventArgs e) => ApplyFilter();

		private void ClearSearch_Click(object sender, RoutedEventArgs e)
		{
			SearchBox.Text = "";
			ApplyFilter();
		}

		private void NewSession_Click(object sender, RoutedEventArgs e)
		{
			if (ComputersGrid.SelectedItem is not ComputerViewModel selected)
			{
				MessageBox.Show("Select a computer first.");
				return;
			}

			var win = new NewSessionWindow(selected.Id, selected.Name) { Owner = this };
			win.ShowDialog();
			LoadComputers();
		}

		private void ViewSessions_Click(object sender, RoutedEventArgs e)
		{
			var win = new SessionsWindow { Owner = this };
			win.ShowDialog();
		}

		private void WelcomeMessage_Click(object sender, RoutedEventArgs e) => WelcomeEditor_Click(sender, e);

		private void WelcomeEditor_Click(object sender, RoutedEventArgs e)
		{
			var win = new WelcomeEditorWindow { Owner = this };
			win.ShowDialog();
		}

		private void EndSession_Click(object sender, RoutedEventArgs e)
		{
			if (ComputersGrid.SelectedItem is not ComputerViewModel selected)
			{
				MessageBox.Show("Select a computer first.");
				return;
			}

			using var db = new CafeContext();

			var session = db.Sessions
				.Where(s => s.ComputerId == selected.Id && s.IsActive)
				.OrderByDescending(s => s.Id)
				.FirstOrDefault();

			if (session == null)
			{
				MessageBox.Show("There is no active session on this computer.");
				return;
			}

			var confirm = MessageBox.Show(
				$"End session for {selected.Name} (PIN {session.PinCode})?",
				"End Session",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question);

			if (confirm != MessageBoxResult.Yes)
				return;

			session.IsActive = false;
			session.ActualEndTime = DateTime.UtcNow;

			var comp = db.Computers.Find(selected.Id);
			if (comp != null)
				comp.Status = ComputerStatus.Idle;

			db.SaveChanges();

			MessageBox.Show("Session ended by admin.", "End Session");
			LoadComputers();
		}
	}

	public class ComputerViewModel : INotifyPropertyChanged
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";

		private string _status = "";
		public string Status
		{
			get => _status;
			set { _status = value; OnPropertyChanged(); }
		}

		// stored active session info (so UI countdown can update without DB)
		public DateTime? ActiveStartUtc { get; set; }
		public DateTime? ActiveEndUtc { get; set; }
		public bool IsActiveSession { get; set; }

		private string _remainingTime = "";
		public string RemainingTime
		{
			get => _remainingTime;
			set { _remainingTime = value; OnPropertyChanged(); }
		}

		private double _remainingPercent;
		public double RemainingPercent
		{
			get => _remainingPercent;
			set { _remainingPercent = value; OnPropertyChanged(); }
		}

		private Brush _remainingBrush = Brushes.Gray;
		public Brush RemainingBrush
		{
			get => _remainingBrush;
			set { _remainingBrush = value; OnPropertyChanged(); }
		}

		public string LastPin { get; set; } = "";

		public event PropertyChangedEventHandler? PropertyChanged;
		private void OnPropertyChanged([CallerMemberName] string? name = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
