using MineDigibyteMobile.Services;
using MineDigibyteMobile.Localization;
using Microsoft.Maui.ApplicationModel;

namespace MineDigibyteMobile;

public partial class SettingsPage : ContentPage
{
    private bool initializing = true;
    private static readonly string[] LanguageCodes = ["pt-BR", "es", "en"];
    private static readonly string[] LanguageNames = ["Português (Brasil)", "Español", "English"];

    private static string T(string key) =>
        LocalizationService.Instance.GetString(key);

    public SettingsPage()
    {
        InitializeComponent();
        LanguagePicker.ItemsSource = LanguageNames;
        LanguagePicker.SelectedIndex = Array.IndexOf(
            LanguageCodes, LocalizationService.Instance.CurrentLanguage);
        IntensitySlider.Value = MiningPreferences.IntensityPercent;
        GpuMiningSwitch.IsToggled = MiningPreferences.GpuEnabled;
        GpuIntensitySlider.Value = MiningPreferences.GpuIntensityPercent;
        GpuIntensityValueLabel.Text = $"{MiningPreferences.GpuIntensityPercent}%";
        GpuStatusLabel.Text = LocalizationService.Instance.TranslateText(
            MiningSessionRuntime.Current.GpuStatus);
        BackgroundMiningSwitch.IsToggled =
            MiningPreferences.BackgroundMiningEnabled ||
            MiningSessionRuntime.Current.IsBackgroundServiceActive;
#if !ANDROID
        BackgroundMiningSwitch.IsEnabled = false;
#endif
        AtualizarEstadoServico(
            MiningSessionRuntime.Current.IsBackgroundServiceActive);
        AtualizarTextoIntensidade();
        initializing = false;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LocalizationService.Instance.LanguageChanged += Localization_LanguageChanged;
        MiningSessionRuntime.Current.BackgroundServiceChanged +=
            BackgroundServiceChanged;
        AtualizarEstadoServico(
            MiningSessionRuntime.Current.IsBackgroundServiceActive);
    }

    protected override void OnDisappearing()
    {
        MiningSessionRuntime.Current.BackgroundServiceChanged -=
            BackgroundServiceChanged;
        LocalizationService.Instance.LanguageChanged -= Localization_LanguageChanged;
        base.OnDisappearing();
    }

    private void IntensitySlider_ValueChanged(
        object? sender,
        ValueChangedEventArgs e)
    {
        int value = Math.Clamp((int)Math.Round(e.NewValue), 1, 100);
        if (Math.Abs(IntensitySlider.Value - value) > 0.001)
            IntensitySlider.Value = value;

        AtualizarTextoIntensidade();
        if (!initializing)
            MiningSessionRuntime.Current.SetIntensity(value);
    }

    private async void BackgroundMiningSwitch_Toggled(
        object? sender,
        ToggledEventArgs e)
    {
        if (initializing)
            return;

#if ANDROID
        if (e.Value && DeviceInfo.Version.Major >= 33)
        {
            PermissionStatus permission =
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            if (permission != PermissionStatus.Granted)
            {
                BackgroundMiningSwitch.IsToggled = false;
                await DisplayAlertAsync(
                    T("NotificationsRequired"),
                    T("NotificationPermissionExplanation"),
                    T("Ok"));
                return;
            }
        }
#endif

        MiningPreferences.BackgroundMiningEnabled = e.Value;
    }

    private async void GpuMiningSwitch_Toggled(object? sender, ToggledEventArgs e)
    {
        if (initializing)
            return;

        MiningSessionRuntime runtime = MiningSessionRuntime.Current;
        GpuStatusLabel.Text = T("GpuStatus");

        try
        {
            await runtime.SetGpuEnabledAsync(e.Value);
            GpuStatusLabel.Text = LocalizationService.Instance.TranslateText(
                runtime.GpuStatus);
        }
        catch (Exception ex)
        {
            GpuStatusLabel.Text = LocalizationService.Instance.TranslateText(
                "Falha ao alterar GPU: " + ex.Message);
        }
    }

    private void GpuIntensitySlider_ValueChanged(
        object? sender,
        ValueChangedEventArgs e)
    {
        int value = Math.Clamp((int)Math.Round(e.NewValue), 5, 100);
        GpuIntensityValueLabel.Text = $"{value}%";

        if (!initializing)
            MiningSessionRuntime.Current.SetGpuIntensity(value);
    }

    private async void DoneButton_Clicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private void AtualizarTextoIntensidade()
    {
        IntensityValueLabel.Text =
            $"{(int)Math.Round(IntensitySlider.Value)}%";
    }

    private void BackgroundServiceChanged(bool active)
    {
        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                AtualizarEstadoServico(active);
                bool enabled =
                    MiningPreferences.BackgroundMiningEnabled || active;
                if (BackgroundMiningSwitch.IsToggled == enabled)
                    return;

                initializing = true;
                BackgroundMiningSwitch.IsToggled = enabled;
                initializing = false;
            });
    }

    private void AtualizarEstadoServico(bool active)
    {
        BackgroundServiceStatusLabel.Text = active
            ? T("AndroidServiceActive")
            : T("AndroidServiceInactive");
    }

    private void LanguagePicker_SelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (initializing || LanguagePicker.SelectedIndex < 0 ||
            LanguagePicker.SelectedIndex >= LanguageCodes.Length)
            return;

        LocalizationService.Instance.SetLanguage(
            LanguageCodes[LanguagePicker.SelectedIndex]);
    }

    private void Localization_LanguageChanged(object? sender, EventArgs e)
    {
        GpuStatusLabel.Text = LocalizationService.Instance.TranslateText(
            MiningSessionRuntime.Current.GpuStatus);
        AtualizarEstadoServico(
            MiningSessionRuntime.Current.IsBackgroundServiceActive);
    }
}
