using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Common;
using Ryujinx.Common.Utilities;
using Ryujinx.UI.Common.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace Ryujinx.Ava.Common.Locale
{
    class LocaleManager : BaseModel
    {
        // This fork defaults to Simplified Chinese (zh_CN) for a fully localized
        // out-of-the-box experience. Users can still pick any other language in Settings.
        private const string DefaultLanguageCode = "zh_CN";

        private readonly Dictionary<LocaleKeys, string> _localeStrings;
        private Dictionary<LocaleKeys, string> _localeDefaultStrings;
        private readonly ConcurrentDictionary<LocaleKeys, object[]> _dynamicValues;
        private string _localeLanguageCode;

        public static LocaleManager Instance { get; } = new();
        public event Action LocaleChanged;

        public LocaleManager()
        {
            _localeStrings = new Dictionary<LocaleKeys, string>();
            _localeDefaultStrings = new Dictionary<LocaleKeys, string>();
            _dynamicValues = new ConcurrentDictionary<LocaleKeys, object[]>();

            Load();
        }

        private void Load()
        {
            // This fork falls back to Chinese instead of the system culture, so a
            // Spanish/other-locale system still gets a Chinese UI by default.
            string localeLanguageCode = !string.IsNullOrEmpty(ConfigurationState.Instance.UI.LanguageCode.Value) ?
                ConfigurationState.Instance.UI.LanguageCode.Value : "zh_CN";

            // Load zh_CN as the fallback base, if the target language translation is missing or incomplete.
            LoadDefaultLanguage();
            LoadLanguage(localeLanguageCode);

            // Save whatever we ended up with.
            if (Program.PreviewerDetached)
            {
                ConfigurationState.Instance.UI.LanguageCode.Value = _localeLanguageCode;

                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        public string this[LocaleKeys key]
        {
            get
            {
                // Check if the locale contains the key.
                if (_localeStrings.TryGetValue(key, out string value))
                {
                    // Check if the localized string needs to be formatted.
                    if (_dynamicValues.TryGetValue(key, out object[] dynamicValue))
                    {
                        try
                        {
                            return string.Format(value, dynamicValue);
                        }
                        catch (Exception)
                        {
                            // If formatting failed use the default text instead.
                            if (_localeDefaultStrings.TryGetValue(key, out value))
                            {
                                try
                                {
                                    return string.Format(value, dynamicValue);
                                }
                                catch (Exception)
                                {
                                    // If formatting the default text failed return the key.
                                    return key.ToString();
                                }
                            }
                        }
                    }

                    return value;
                }

                // If the locale doesn't contain the key return the default one.
                if (_localeDefaultStrings.TryGetValue(key, out string defaultValue))
                {
                    return defaultValue;
                }

                // If the locale text doesn't exist return the key.
                return key.ToString();
            }
            set
            {
                _localeStrings[key] = value;

                OnPropertyChanged();
            }
        }

        public bool IsRTL()
        {
            return _localeLanguageCode switch
            {
                "ar_SA" or "he_IL" => true,
                _ => false
            };
        }

        public string UpdateAndGetDynamicValue(LocaleKeys key, params object[] values)
        {
            _dynamicValues[key] = values;

            OnPropertyChanged("Item");

            return this[key];
        }

        private void LoadDefaultLanguage()
        {
            _localeDefaultStrings = LoadJsonLanguage(DefaultLanguageCode);
        }

        public void LoadLanguage(string languageCode)
        {
            var locale = LoadJsonLanguage(languageCode);

            if (locale == null)
            {
                _localeLanguageCode = DefaultLanguageCode;
                locale = _localeDefaultStrings;
            }
            else
            {
                _localeLanguageCode = languageCode;
            }

            foreach (var item in locale)
            {
                _localeStrings[item.Key] = item.Value;
            }

            OnPropertyChanged("Item");

            LocaleChanged?.Invoke();
        }

        private static Dictionary<LocaleKeys, string> LoadJsonLanguage(string languageCode)
        {
            var localeStrings = new Dictionary<LocaleKeys, string>();
            string languageJson = EmbeddedResources.ReadAllText($"Ryujinx/Assets/Locales/{languageCode}.json");

            if (languageJson == null)
            {
                // We were unable to find file for that language code.
                return null;
            }

            Dictionary<string, string> strings = JsonHelper.Deserialize(languageJson, CommonJsonContext.Default.StringDictionary);

            foreach (KeyValuePair<string, string> item in strings)
            {
                if (Enum.TryParse<LocaleKeys>(item.Key, out var key))
                {
                    localeStrings[key] = item.Value;
                }
            }

            return localeStrings;
        }
    }
}
