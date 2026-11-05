namespace DigitalLogin.Maui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = "DigitalLogin", Width = 1200, Height = 820 };
	}
}
