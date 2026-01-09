using System.Windows;

namespace GEC.Client
{
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			var win = new LockWindow(); // THIS EXISTS
			win.Show();
		}
	}
}
