namespace DropCast.Android;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("OverlayZone", typeof(OverlayZonePage));
	}
}
