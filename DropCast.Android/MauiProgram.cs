using DropCast.Android.Services;
using Microsoft.Extensions.Logging;

namespace DropCast.Android;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Services
		builder.Services.AddSingleton<DiscordService>();
		builder.Services.AddSingleton<VideoResolver>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<OverlayZonePage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
