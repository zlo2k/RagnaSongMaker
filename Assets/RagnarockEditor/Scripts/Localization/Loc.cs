using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace RagnarockEditor.Localization
{
    /// <summary>
    /// Точка доступа к строкам интерфейса: Loc.T("record.ready.title").
    ///
    /// Строки лежат в Assets/Resources/Localization/&lt;code&gt;.json (плоский словарь
    /// «ключ → текст»), поэтому перевод можно поправить без пересборки C#.
    /// Если ключа нет в выбранном языке — берётся английский, если нет и там — сам ключ.
    /// Язык хранится в PlayerPrefs; при первом запуске подхватывается язык системы.
    /// </summary>
    public static class Loc
    {
        /// <summary>Ключ PlayerPrefs с кодом выбранного языка.</summary>
        public const string PrefsKey = "ragna.locale";

        private const string ResourceFolder = "Localization/";
        private const Language FallbackLanguage = Language.English;

        private static readonly Dictionary<Language, Dictionary<string, string>> Tables =
            new Dictionary<Language, Dictionary<string, string>>();

        private static readonly Dictionary<string, string> MissingKeys = new Dictionary<string, string>();

        private static bool _initialized;
        private static Language _current = Language.English;

        /// <summary>Язык интерфейса сменился (подписаны LocalizedText и экраны).</summary>
        public static event Action OnLanguageChanged;

        /// <summary>Текущий язык.</summary>
        public static Language Current
        {
            get
            {
                EnsureInitialized();
                return _current;
            }
        }

        /// <summary>Код текущего языка ("ru").</summary>
        public static string CurrentCode => LanguageCatalog.CodeOf(Current);

        /// <summary>Родное название текущего языка ("Русский").</summary>
        public static string CurrentNativeName => LanguageCatalog.NativeNameOf(Current);

        public static IReadOnlyList<LanguageInfo> Available => LanguageCatalog.All;

        /// <summary>Инициализация «на всякий случай» — например, из Awake экрана.</summary>
        public static void Initialize() => EnsureInitialized();

        /// <summary>
        /// Переключает язык, сохраняет выбор и рассылает OnLanguageChanged.
        /// </summary>
        public static void SetLanguage(Language language, bool persist = true)
        {
            EnsureInitialized();

            if (_current == language && Tables.ContainsKey(language))
                return;

            _current = language;
            LoadTable(language);

            if (persist)
            {
                PlayerPrefs.SetString(PrefsKey, LanguageCatalog.CodeOf(language));
                PlayerPrefs.Save();
            }

            Debug.Log($"[Loc] язык: {LanguageCatalog.NativeNameOf(language)} ({LanguageCatalog.CodeOf(language)})");
            OnLanguageChanged?.Invoke();
        }

        public static void NextLanguage() => SetLanguage(LanguageCatalog.Next(Current));

        public static void PreviousLanguage() => SetLanguage(LanguageCatalog.Previous(Current));

        /// <summary>
        /// Возвращает строку по ключу. Поддерживает string.Format:
        /// Loc.T("hud.hits", 12) → "Ударов: 12".
        /// </summary>
        public static string T(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            EnsureInitialized();

            if (!TryGet(_current, key, out string value) && !TryGet(FallbackLanguage, key, out value))
            {
                RegisterMissing(key);
                value = key;
            }

            if (args != null && args.Length > 0)
            {
                try
                {
                    value = string.Format(CultureInfo.InvariantCulture, value, args);
                }
                catch (FormatException)
                {
                    Debug.LogWarning($"[Loc] неверный формат строки '{key}': {value}");
                }
            }

            return value;
        }

        /// <summary>Есть ли такой ключ хотя бы в английской таблице.</summary>
        public static bool Has(string key)
        {
            EnsureInitialized();
            return TryGet(_current, key, out _) || TryGet(FallbackLanguage, key, out _);
        }

        /// <summary>
        /// Сбрасывает кэш и перечитывает JSON (нужно после правки переводов
        /// и после переимпорта ассетов редактором).
        /// </summary>
        public static void Reload()
        {
            Tables.Clear();
            MissingKeys.Clear();
            _initialized = false;
            EnsureInitialized();
            OnLanguageChanged?.Invoke();
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            _current = DetectLanguage();
            LoadTable(_current);
        }

        private static Language DetectLanguage()
        {
            string saved = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (LanguageCatalog.TryFromCode(saved, out var savedLanguage))
                return savedLanguage;

            return LanguageCatalog.FromSystemLanguage(Application.systemLanguage);
        }

        private static bool TryGet(Language language, string key, out string value)
        {
            value = null;
            var table = LoadTable(language);
            return table != null && table.TryGetValue(key, out value) && !string.IsNullOrEmpty(value);
        }

        private static Dictionary<string, string> LoadTable(Language language)
        {
            if (Tables.TryGetValue(language, out var cached))
                return cached;

            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            Tables[language] = table;

            string code = LanguageCatalog.CodeOf(language);
            var asset = Resources.Load<TextAsset>(ResourceFolder + code);

            if (asset == null)
            {
                Debug.LogWarning($"[Loc] нет файла Resources/{ResourceFolder}{code}.json — используются английские строки");
                return table;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(asset.text);
                if (parsed != null)
                {
                    foreach (var pair in parsed)
                    {
                        if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                            table[pair.Key] = pair.Value;
                    }
                }

                Debug.Log($"[Loc] загружено {table.Count} строк: {code}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Loc] не удалось разобрать {code}.json: {e.Message}");
            }

            return table;
        }

        private static void RegisterMissing(string key)
        {
            if (MissingKeys.ContainsKey(key))
                return;

            MissingKeys[key] = key;
            Debug.LogWarning($"[Loc] нет ключа '{key}'");
        }
    }
}
