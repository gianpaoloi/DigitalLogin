using DigitalLogin.Auth;
using DigitalLogin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radzen;

namespace DigitalLogin.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

		builder.Services.AddMauiBlazorWebView();

		// Radzen services (NotificationService, DialogService, ...) used by the layout and pages.
		builder.Services.AddRadzenComponents();

		// The auth client is stateless apart from its HttpClient, so one shared instance per app is
		// enough. The timeout bounds each of the two calls; a hung endpoint should fail a test, not stall it.
		builder.Services.AddSingleton(_ => new AgyoAuthClient(new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(30),
		}));

		// One desktop window and one user: the run history should survive a WebView reload.
		// SQLite file under %LOCALAPPDATA%DigitalLogin so history survives restarts.
		var historyDb = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitalLogin", "history.db");
		builder.Services.AddSingleton(_ => new LoginRunHistory(historyDb));
		// Session-only state (the client secret), kept across page navigation. Never persisted.
		builder.Services.AddSingleton<LoginSessionState>();

		builder.Logging.ClearProviders();
		builder.Logging.AddDebug();
		builder.Logging.SetMinimumLevel(LogLevel.Information);

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
#endif

		return builder.Build();
	}
}
