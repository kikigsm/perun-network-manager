using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace PerunNetworkManager.Services
{
    /// <summary>
    /// Service responsible for managing application localization and language switching.
    /// </summary>
    public class LocalizationService : INotifyPropertyChanged
    {
        private readonly ILogger<LocalizationService> _logger;
        private readonly Dictionary<string, CultureInfo> _availableCultures;
        private readonly Dictionary<string, ResourceManager> _resourceManagers;
        private readonly string _resourcesPath;
        private CultureInfo _currentCulture;
        private FlowDirection _currentFlowDirection;

        // Singleton instance
        private static LocalizationService _instance;
        private static readonly object _lock = new object();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<CultureChangedEventArgs> CultureChanged;

        /// <summary>
        /// Gets the singleton instance of LocalizationService.
        /// </summary>
        public static LocalizationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new LocalizationService();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets the current culture.
        /// </summary>
        public CultureInfo CurrentCulture
        {
            get => _currentCulture;
            private set
            {
                if (_currentCulture != value)
                {
                    var oldCulture = _currentCulture;
                    _currentCulture = value;
                    OnPropertyChanged();
                    OnCultureChanged(oldCulture, value);
                }
            }
        }

        /// <summary>
        /// Gets the current flow direction (LTR/RTL).
        /// </summary>
        public FlowDirection CurrentFlowDirection
        {
            get => _currentFlowDirection;
            private set
            {
                if (_currentFlowDirection != value)
                {
                    _currentFlowDirection = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets all available cultures/languages.
        /// </summary>
        public IReadOnlyDictionary<string, CultureInfo> AvailableCultures => _availableCultures;

        /// <summary>
        /// Gets list of supported languages with native names.
        /// </summary>
        public List<LanguageInfo> SupportedLanguages { get; private set; }

        private LocalizationService()
        {
            // Initialize logger (in real app, inject via DI)
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<LocalizationService>();

            _availableCultures = new Dictionary<string, CultureInfo>();
            _resourceManagers = new Dictionary<string, ResourceManager>();
            _resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");

            InitializeLanguages();
            LoadResourceManagers();
            
            // Set default culture
            SetCulture(CultureInfo.CurrentCulture.Name);
        }

        /// <summary>
        /// Initializes the list of supported languages.
        /// </summary>
        private void InitializeLanguages()
        {
            SupportedLanguages = new List<LanguageInfo>
            {
                // Major languages
                new LanguageInfo("en-US", "English", "English", "🇺🇸"),
                new LanguageInfo("es-ES", "Spanish", "Español", "🇪🇸"),
                new LanguageInfo("fr-FR", "French", "Français", "🇫🇷"),
                new LanguageInfo("de-DE", "German", "Deutsch", "🇩🇪"),
                new LanguageInfo("it-IT", "Italian", "Italiano", "🇮🇹"),
                new LanguageInfo("pt-BR", "Portuguese (Brazil)", "Português (Brasil)", "🇧🇷"),
                new LanguageInfo("pt-PT", "Portuguese", "Português", "🇵🇹"),
                new LanguageInfo("ru-RU", "Russian", "Русский", "🇷🇺"),
                new LanguageInfo("zh-CN", "Chinese (Simplified)", "简体中文", "🇨🇳"),
                new LanguageInfo("zh-TW", "Chinese (Traditional)", "繁體中文", "🇹🇼"),
                new LanguageInfo("ja-JP", "Japanese", "日本語", "🇯🇵"),
                new LanguageInfo("ko-KR", "Korean", "한국어", "🇰🇷"),
                new LanguageInfo("ar-SA", "Arabic", "العربية", "🇸🇦", true),
                new LanguageInfo("he-IL", "Hebrew", "עברית", "🇮🇱", true),
                new LanguageInfo("hi-IN", "Hindi", "हिन्दी", "🇮🇳"),
                new LanguageInfo("tr-TR", "Turkish", "Türkçe", "🇹🇷"),
                new LanguageInfo("pl-PL", "Polish", "Polski", "🇵🇱"),
                new LanguageInfo("nl-NL", "Dutch", "Nederlands", "🇳🇱"),
                new LanguageInfo("sv-SE", "Swedish", "Svenska", "🇸🇪"),
                new LanguageInfo("no-NO", "Norwegian", "Norsk", "🇳🇴"),
                new LanguageInfo("da-DK", "Danish", "Dansk", "🇩🇰"),
                new LanguageInfo("fi-FI", "Finnish", "Suomi", "🇫🇮"),
                new LanguageInfo("cs-CZ", "Czech", "Čeština", "🇨🇿"),
                new LanguageInfo("hu-HU", "Hungarian", "Magyar", "🇭🇺"),
                new LanguageInfo("ro-RO", "Romanian", "Română", "🇷🇴"),
                new LanguageInfo("bg-BG", "Bulgarian", "Български", "🇧🇬"),
                new LanguageInfo("hr-HR", "Croatian", "Hrvatski", "🇭🇷"),
                new LanguageInfo("sr-Latn-RS", "Serbian (Latin)", "Srpski", "🇷🇸"),
                new LanguageInfo("sr-Cyrl-RS", "Serbian (Cyrillic)", "Српски", "🇷🇸"),
                new LanguageInfo("sk-SK", "Slovak", "Slovenčina", "🇸🇰"),
                new LanguageInfo("sl-SI", "Slovenian", "Slovenščina", "🇸🇮"),
                new LanguageInfo("uk-UA", "Ukrainian", "Українська", "🇺🇦"),
                new LanguageInfo("el-GR", "Greek", "Ελληνικά", "🇬🇷"),
                new LanguageInfo("th-TH", "Thai", "ไทย", "🇹🇭"),
                new LanguageInfo("vi-VN", "Vietnamese", "Tiếng Việt", "🇻🇳"),
                new LanguageInfo("id-ID", "Indonesian", "Bahasa Indonesia", "🇮🇩"),
                new LanguageInfo("ms-MY", "Malay", "Bahasa Melayu", "🇲🇾"),
                new LanguageInfo("fa-IR", "Persian", "فارسی", "🇮🇷", true),
                new LanguageInfo("ur-PK", "Urdu", "اردو", "🇵🇰", true),
                new LanguageInfo("bn-BD", "Bengali", "বাংলা", "🇧🇩"),
                new LanguageInfo("ta-IN", "Tamil", "தமிழ்", "🇮🇳"),
                new LanguageInfo("te-IN", "Telugu", "తెలుగు", "🇮🇳"),
                new LanguageInfo("mr-IN", "Marathi", "मराठी", "🇮🇳"),
                new LanguageInfo("gu-IN", "Gujarati", "ગુજરાતી", "🇮🇳"),
                new LanguageInfo("kn-IN", "Kannada", "ಕನ್ನಡ", "🇮🇳"),
                new LanguageInfo("ml-IN", "Malayalam", "മലയാളം", "🇮🇳"),
                new LanguageInfo("pa-IN", "Punjabi", "ਪੰਜਾਬੀ", "🇮🇳"),
                new LanguageInfo("ne-NP", "Nepali", "नेपाली", "🇳🇵"),
                new LanguageInfo("si-LK", "Sinhala", "සිංහල", "🇱🇰"),
                new LanguageInfo("my-MM", "Burmese", "မြန်မာဘာသာ", "🇲🇲"),
                new LanguageInfo("km-KH", "Khmer", "ភាសាខ្មែរ", "🇰🇭"),
                new LanguageInfo("lo-LA", "Lao", "ພາສາລາວ", "🇱🇦"),
                new LanguageInfo("ka-GE", "Georgian", "ქართული", "🇬🇪"),
                new LanguageInfo("am-ET", "Amharic", "አማርኛ", "🇪🇹"),
                new LanguageInfo("sw-KE", "Swahili", "Kiswahili", "🇰🇪"),
                new LanguageInfo("zu-ZA", "Zulu", "isiZulu", "🇿🇦"),
                new LanguageInfo("xh-ZA", "Xhosa", "isiXhosa", "🇿🇦"),
                new LanguageInfo("af-ZA", "Afrikaans", "Afrikaans", "🇿🇦"),
                new LanguageInfo("yo-NG", "Yoruba", "Yorùbá", "🇳🇬"),
                new LanguageInfo("ig-NG", "Igbo", "Igbo", "🇳🇬"),
                new LanguageInfo("ha-Latn-NG", "Hausa", "Hausa", "🇳🇬"),
                new LanguageInfo("fil-PH", "Filipino", "Filipino", "🇵🇭"),
                new LanguageInfo("et-EE", "Estonian", "Eesti", "🇪🇪"),
                new LanguageInfo("lv-LV", "Latvian", "Latviešu", "🇱🇻"),
                new LanguageInfo("lt-LT", "Lithuanian", "Lietuvių", "🇱🇹"),
                new LanguageInfo("sq-AL", "Albanian", "Shqip", "🇦🇱"),
                new LanguageInfo("mk-MK", "Macedonian", "Македонски", "🇲🇰"),
                new LanguageInfo("mt-MT", "Maltese", "Malti", "🇲🇹"),
                new LanguageInfo("is-IS", "Icelandic", "Íslenska", "🇮🇸"),
                new LanguageInfo("ga-IE", "Irish", "Gaeilge", "🇮🇪"),
                new LanguageInfo("cy-GB", "Welsh", "Cymraeg", "🏴󠁧󠁢󠁷󠁬󠁳󠁿"),
                new LanguageInfo("eu-ES", "Basque", "Euskara", "🇪🇸"),
                new LanguageInfo("ca-ES", "Catalan", "Català", "🇪🇸"),
                new LanguageInfo("gl-ES", "Galician", "Galego", "🇪🇸")
            };

            // Populate available cultures dictionary
            foreach (var lang in SupportedLanguages)
            {
                try
                {
                    var culture = new CultureInfo(lang.Code);
                    _availableCultures[lang.Code] = culture;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to create CultureInfo for {lang.Code}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Loads resource managers for different resource files.
        /// </summary>
        private void LoadResourceManagers()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames()
                    .Where(name => name.EndsWith(".resources"))
                    .ToList();

                foreach (var resourceName in resourceNames)
                {
                    var baseName = resourceName.Replace(".resources", "");
                    var resourceManager = new ResourceManager(baseName, assembly);
                    
                    // Extract resource category (e.g., "Strings", "Messages", "Errors")
                    var parts = baseName.Split('.');
                    if (parts.Length > 0)
                    {
                        var category = parts[parts.Length - 1];
                        _resourceManagers[category] = resourceManager;
                    }
                }

                _logger.LogInformation($"Loaded {_resourceManagers.Count} resource managers");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading resource managers");
            }
        }

        /// <summary>
        /// Sets the current culture/language.
        /// </summary>
        public async Task<bool> SetCultureAsync(string cultureCode)
        {
            return await Task.Run(() => SetCulture(cultureCode));
        }

        /// <summary>
        /// Sets the current culture/language synchronously.
        /// </summary>
        public bool SetCulture(string cultureCode)
        {
            try
            {
                if (!_availableCultures.TryGetValue(cultureCode, out var culture))
                {
                    _logger.LogWarning($"Culture {cultureCode} not found, using default");
                    culture = CultureInfo.GetCultureInfo("en-US");
                }

                CurrentCulture = culture;
                
                // Set thread cultures
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;

                // Set flow direction for RTL languages
                var languageInfo = SupportedLanguages.FirstOrDefault(l => l.Code == cultureCode);
                CurrentFlowDirection = (languageInfo?.IsRightToLeft ?? false) 
                    ? FlowDirection.RightToLeft 
                    : FlowDirection.LeftToRight;

                // Update WPF language property
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (Window window in Application.Current.Windows)
                        {
                            window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag);
                            window.FlowDirection = CurrentFlowDirection;
                        }
                    });
                }

                _logger.LogInformation($"Culture changed to {culture.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting culture to {cultureCode}");
                return false;
            }
        }

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        public string GetString(string key, string category = "Strings")
        {
            try
            {
                if (_resourceManagers.TryGetValue(category, out var resourceManager))
                {
                    var value = resourceManager.GetString(key, CurrentCulture);
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }

                // Fallback to key if not found
                _logger.LogDebug($"Resource key '{key}' not found in category '{category}'");
                return key;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting string for key '{key}'");
                return key;
            }
        }

        /// <summary>
        /// Gets a formatted localized string.
        /// </summary>
        public string GetFormattedString(string key, params object[] args)
        {
            var format = GetString(key);
            try
            {
                return string.Format(CurrentCulture, format, args);
            }
            catch
            {
                return format;
            }
        }

        /// <summary>
        /// Formats a number according to current culture.
        /// </summary>
        public string FormatNumber(double number, int decimals = 2)
        {
            return number.ToString($"N{decimals}", CurrentCulture);
        }

        /// <summary>
        /// Formats currency according to current culture.
        /// </summary>
        public string FormatCurrency(decimal amount)
        {
            return amount.ToString("C", CurrentCulture);
        }

        /// <summary>
        /// Formats date according to current culture.
        /// </summary>
        public string FormatDate(DateTime date, string format = null)
        {
            if (string.IsNullOrEmpty(format))
                return date.ToString(CurrentCulture);
            
            return date.ToString(format, CurrentCulture);
        }

        /// <summary>
        /// Formats time according to current culture.
        /// </summary>
        public string FormatTime(DateTime time)
        {
            return time.ToString("t", CurrentCulture);
        }

        /// <summary>
        /// Gets the first day of week for current culture.
        /// </summary>
        public DayOfWeek GetFirstDayOfWeek()
        {
            return CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        }

        /// <summary>
        /// Exports current language resources to XML for translation.
        /// </summary>
        public async Task ExportLanguageResourcesAsync(string filePath, string targetLanguageCode)
        {
            await Task.Run(() =>
            {
                try
                {
                    var doc = new XDocument(new XElement("Resources",
                        new XAttribute("Language", targetLanguageCode),
                        new XAttribute("Version", "1.0")));

                    var root = doc.Root;

                    foreach (var kvp in _resourceManagers)
                    {
                        var category = new XElement("Category", new XAttribute("Name", kvp.Key));
                        
                        var resourceSet = kvp.Value.GetResourceSet(CultureInfo.InvariantCulture, true, true);
                        foreach (System.Collections.DictionaryEntry entry in resourceSet)
                        {
                            category.Add(new XElement("String",
                                new XAttribute("Key", entry.Key.ToString()),
                                new XAttribute("Value", entry.Value?.ToString() ?? "")));
                        }

                        root.Add(category);
                    }

                    doc.Save(filePath);
                    _logger.LogInformation($"Exported language resources to {filePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error exporting language resources");
                    throw;
                }
            });
        }

        /// <summary>
        /// Imports translated resources from XML.
        /// </summary>
        public async Task ImportLanguageResourcesAsync(string filePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    var doc = XDocument.Load(filePath);
                    var languageCode = doc.Root.Attribute("Language")?.Value;

                    if (string.IsNullOrEmpty(languageCode))
                        throw new InvalidOperationException("Language code not found in import file");

                    // TODO: Implement actual resource file generation
                    // This would typically involve creating new .resx files
                    // or updating existing ones with the translated strings

                    _logger.LogInformation($"Imported language resources from {filePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing language resources");
                    throw;
                }
            });
        }

        /// <summary>
        /// Checks if a specific culture is supported.
        /// </summary>
        public bool IsCultureSupported(string cultureCode)
        {
            return _availableCultures.ContainsKey(cultureCode);
        }

        /// <summary>
        /// Gets the percentage of translated strings for a language.
        /// </summary>
        public double GetTranslationCompleteness(string cultureCode)
        {
            try
            {
                var culture = new CultureInfo(cultureCode);
                var totalStrings = 0;
                var translatedStrings = 0;

                foreach (var resourceManager in _resourceManagers.Values)
                {
                    var invariantSet = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
                    var cultureSet = resourceManager.GetResourceSet(culture, true, false);

                    foreach (System.Collections.DictionaryEntry entry in invariantSet)
                    {
                        totalStrings++;
                        if (cultureSet != null)
                        {
                            var translated = cultureSet.GetString(entry.Key.ToString());
                            if (!string.IsNullOrEmpty(translated))
                                translatedStrings++;
                        }
                    }
                }

                return totalStrings > 0 ? (double)translatedStrings / totalStrings * 100 : 0;
            }
            catch
            {
                return 0;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual void OnCultureChanged(CultureInfo oldCulture, CultureInfo newCulture)
        {
            CultureChanged?.Invoke(this, new CultureChangedEventArgs(oldCulture, newCulture));
        }
    }

    /// <summary>
    /// Represents information about a supported language.
    /// </summary>
    public class LanguageInfo
    {
        public string Code { get; set; }
        public string EnglishName { get; set; }
        public string NativeName { get; set; }
        public string Flag { get; set; }
        public bool IsRightToLeft { get; set; }

        public LanguageInfo(string code, string englishName, string nativeName, string flag, bool isRightToLeft = false)
        {
            Code = code;
            EnglishName = englishName;
            NativeName = nativeName;
            Flag = flag;
            IsRightToLeft = isRightToLeft;
        }

        public string DisplayName => $"{Flag} {NativeName} ({EnglishName})";
    }

    /// <summary>
    /// Event args for culture change events.
    /// </summary>
    public class CultureChangedEventArgs : EventArgs
    {
        public CultureInfo OldCulture { get; }
        public CultureInfo NewCulture { get; }

        public CultureChangedEventArgs(CultureInfo oldCulture, CultureInfo newCulture)
        {
            OldCulture = oldCulture;
            NewCulture = newCulture;
        }
    }

    /// <summary>
    /// Markup extension for use in XAML for localized strings.
    /// </summary>
    public class Localize : System.Windows.Markup.MarkupExtension
    {
        private readonly string _key;
        private readonly string _category;

        public Localize(string key, string category = "Strings")
        {
            _key = key;
            _category = category;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return LocalizationService.Instance.GetString(_key, _category);
        }
    }
}
