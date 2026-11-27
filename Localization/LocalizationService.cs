using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;

namespace MineDigibyteMobile.Localization;

/// <summary>
/// Central runtime localization manager. PT-BR is the original language and
/// fallback; startup rejects incomplete translation dictionaries.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private const string PreferenceKey = "app.language";
    private static readonly string[] SupportedLanguages = ["pt-BR", "es", "en"];
    private readonly Dictionary<string, Dictionary<string, string>> _resources;
    private Dictionary<string, string> _current;
    private string _currentLanguage = "pt-BR";
    private Dictionary<string, string> _sourceTextMap = new(StringComparer.Ordinal);
    private Regex? _sourceTextRegex;

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string CurrentLanguage => _currentLanguage;

    public IReadOnlyList<string> Languages => SupportedLanguages;

    /// <summary>
    /// Indexer used by the XAML Translate markup extension.
    /// </summary>
    public string this[string key] => GetString(key);

    private LocalizationService()
    {
        _resources = LoadResources();
        _current = _resources["pt-BR"];

        // A escolha manual feita dentro do aplicativo sempre tem prioridade.
        // Em uma instalação sem preferência salva, usa a cultura do dispositivo
        // antes de alterar CurrentCulture/CurrentUICulture para a cultura do app.
        string initialLanguage = Preferences.Default.ContainsKey(PreferenceKey)
            ? Preferences.Default.Get(PreferenceKey, "pt-BR")
            : DetectDeviceLanguage();

        ApplyLanguage(initialLanguage, persist: false, notify: false);
    }

    /// <summary>
    /// Detects the device's preferred UI language on first launch.
    /// Supported mappings: Portuguese (any region) -> pt-BR,
    /// Spanish (any region) -> es, English (any region) -> en.
    /// Unsupported device languages fall back to the app's original language.
    /// </summary>
    private static string DetectDeviceLanguage()
    {
        // MAUI/.NET initializes CurrentUICulture from the device UI locale.
        // Read it before ApplyLanguage changes the process/thread cultures.
        string deviceUiLanguage = CultureInfo.CurrentUICulture.Name;
        return TryMapDeviceLanguage(deviceUiLanguage, out string mapped)
            ? mapped
            : "pt-BR";
    }

    private static bool TryMapDeviceLanguage(
        string? languageTag,
        out string mappedLanguage)
    {
        mappedLanguage = "pt-BR";
        if (string.IsNullOrWhiteSpace(languageTag))
            return false;

        string primaryLanguage = languageTag
            .Replace('_', '-')
            .Split('-', StringSplitOptions.RemoveEmptyEntries)[0]
            .Trim()
            .ToLowerInvariant();

        switch (primaryLanguage)
        {
            case "pt":
                mappedLanguage = "pt-BR";
                return true;
            case "es":
                mappedLanguage = "es";
                return true;
            case "en":
                mappedLanguage = "en";
                return true;
            default:
                return false;
        }
    }

    public string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (_current.TryGetValue(key, out string? translated))
            return translated;

        if (_resources["pt-BR"].TryGetValue(key, out string? original))
            return original;

        return key;
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, GetString(key), arguments);

    /// <summary>
    /// Translates existing Portuguese status/log fragments. It allows the
    /// mining engine to keep its internal status values independent from UI language.
    /// </summary>
    public string TranslateText(string? text)
    {
        if (string.IsNullOrEmpty(text) || _currentLanguage == "pt-BR")
            return text ?? string.Empty;

        // Match only against the original string. Sequential Replace calls can
        // accidentally re-process translated output (for example, "Error" contains
        // the original Portuguese substring "Erro" and could become "Errorr").
        return _sourceTextRegex is null
            ? text
            : _sourceTextRegex.Replace(
                text,
                match => _sourceTextMap[match.Value]);
    }

    public bool SetLanguage(string languageCode)
    {
        if (!SupportedLanguages.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
            return false;

        string normalized = SupportedLanguages.First(
            x => string.Equals(x, languageCode, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(_currentLanguage, normalized, StringComparison.Ordinal))
            return true;

        ApplyLanguage(normalized, persist: true, notify: true);
        return true;
    }

    private void ApplyLanguage(string languageCode, bool persist, bool notify)
    {
        if (!_resources.TryGetValue(languageCode, out Dictionary<string, string>? selected))
        {
            languageCode = "pt-BR";
            selected = _resources["pt-BR"];
        }

        _currentLanguage = languageCode;
        _current = selected;

        var culture = new CultureInfo(languageCode);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        _sourceTextMap = BuildSourceTextMap(_resources["pt-BR"], _current);
        _sourceTextRegex = BuildSourceTextRegex(_sourceTextMap);

        if (persist)
            Preferences.Default.Set(PreferenceKey, languageCode);

        if (notify)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Dictionary<string, string> BuildSourceTextMap(
        Dictionary<string, string> source,
        Dictionary<string, string> target)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in source)
        {
            if (string.IsNullOrEmpty(pair.Value) ||
                !target.TryGetValue(pair.Key, out string? translated) ||
                string.IsNullOrEmpty(translated) ||
                string.Equals(pair.Value, translated, StringComparison.Ordinal))
                continue;

            // Keep the first explicitly-authored translation for duplicate source values.
            map.TryAdd(pair.Value, translated);
        }

        return map;
    }


    private static Regex? BuildSourceTextRegex(Dictionary<string, string> map)
    {
        if (map.Count == 0)
            return null;

        string pattern = string.Join(
            "|",
            map.Keys
                .OrderByDescending(x => x.Length)
                .Select(Regex.Escape));

        return new Regex(pattern, RegexOptions.CultureInvariant);
    }

    private static Dictionary<string, Dictionary<string, string>> LoadResources()
    {
        var all = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Assembly assembly = typeof(LocalizationService).Assembly;
        string[] names = assembly.GetManifestResourceNames();

        foreach (string language in SupportedLanguages)
        {
            string suffix = $"Resources.Localization.{language}.json";
            string? resourceName = names.FirstOrDefault(
                x => x.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
                throw new InvalidOperationException(
                    $"The required {language} localization resource was not embedded in the application.");

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Unable to open localization resource: {resourceName}");

            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException($"Invalid localization resource: {resourceName}");

            all[language] = dictionary;
        }

        ValidateResourceKeys(all);
        return all;
    }

    private static void ValidateResourceKeys(
        Dictionary<string, Dictionary<string, string>> resources)
    {
        Dictionary<string, string> source = resources["pt-BR"];
        foreach (string language in SupportedLanguages)
        {
            foreach (string key in source.Keys)
            {
                if (!resources[language].TryGetValue(key, out string? value) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"Missing localization value '{key}' in language '{language}'.");
                }

                string sourcePlaceholders = string.Join("|",
                    System.Text.RegularExpressions.Regex.Matches(source[key], @"\{[^{}]+\}")
                        .Cast<System.Text.RegularExpressions.Match>()
                        .Select(x => x.Value));
                string targetPlaceholders = string.Join("|",
                    System.Text.RegularExpressions.Regex.Matches(value, @"\{[^{}]+\}")
                        .Cast<System.Text.RegularExpressions.Match>()
                        .Select(x => x.Value));

                if (!string.Equals(sourcePlaceholders, targetPlaceholders, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Placeholder mismatch for '{key}' in language '{language}'.");
                }
            }
        }
    }
}
