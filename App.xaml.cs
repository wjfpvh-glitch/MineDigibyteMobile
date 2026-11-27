using Microsoft.Extensions.DependencyInjection;

using MineDigibyteMobile.Localization;

namespace MineDigibyteMobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
        _ = LocalizationService.Instance;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}