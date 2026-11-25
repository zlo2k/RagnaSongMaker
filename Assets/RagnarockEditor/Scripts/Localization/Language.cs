using UnityEngine;

namespace RagnarockEditor.Localization
{
    /// <summary>
    /// Язык интерфейса. <see cref="LanguageInfo.Code"/> совпадает с именем JSON-файла
    /// в Assets/Resources/Localization (например, "ru.json").
    /// </summary>
    public enum Language
    {
        English = 0,
        Russian = 1,
        German = 2,
        French = 3,
        Spanish = 4,
        Italian = 5,
        Polish = 6,
        Portuguese = 7
    }

    /// <summary>Метаданные языка: код файла и подпись в переключателе (родное название).</summary>
    public readonly struct LanguageInfo
    {
        public readonly Language Language;
        public readonly string Code;
        public readonly string NativeName;

        public LanguageInfo(Language language, string code, string nativeName)
        {
            Language = language;
            Code = code;
            NativeName = nativeName;
        }
    }

    /// <summary>
    /// Каталог поддерживаемых языков. Все языки набраны шрифтом LegacyRuntime.ttf
    /// (латиница + кириллица); CJK не добавлены, т.к. для них нужен TMP-шрифт.
    /// </summary>
    public static class LanguageCatalog
    {
        public static readonly LanguageInfo[] All =
        {
            new LanguageInfo(Language.English, "en", "English"),
            new LanguageInfo(Language.Russian, "ru", "Русский"),
            new LanguageInfo(Language.German, "de", "Deutsch"),
            new LanguageInfo(Language.French, "fr", "Français"),
            new LanguageInfo(Language.Spanish, "es", "Español"),
            new LanguageInfo(Language.Italian, "it", "Italiano"),
            new LanguageInfo(Language.Polish, "pl", "Polski"),
            new LanguageInfo(Language.Portuguese, "pt", "Português"),
        };

        public static int Count => All.Length;

        public static LanguageInfo Get(Language language)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Language == language)
                    return All[i];
            }

            return All[0];
        }

        public static string CodeOf(Language language) => Get(language).Code;

        public static string NativeNameOf(Language language) => Get(language).NativeName;

        public static bool TryFromCode(string code, out Language language)
        {
            language = Language.English;

            if (string.IsNullOrEmpty(code))
                return false;

            code = code.Trim().ToLowerInvariant();

            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Code == code)
                {
                    language = All[i].Language;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Язык системы → поддерживаемый язык (иначе английский).</summary>
        public static Language FromSystemLanguage(SystemLanguage system)
        {
            switch (system)
            {
                case SystemLanguage.Russian:
                case SystemLanguage.Belarusian:
                case SystemLanguage.Ukrainian:
                    return Language.Russian;

                case SystemLanguage.German:
                    return Language.German;

                case SystemLanguage.French:
                    return Language.French;

                case SystemLanguage.Spanish:
                    return Language.Spanish;

                case SystemLanguage.Italian:
                    return Language.Italian;

                case SystemLanguage.Polish:
                    return Language.Polish;

                case SystemLanguage.Portuguese:
                    return Language.Portuguese;

                default:
                    return Language.English;
            }
        }

        /// <summary>Следующий язык по кругу (для кнопки «▶»).</summary>
        public static Language Next(Language current)
        {
            int index = IndexOf(current);
            return All[(index + 1) % All.Length].Language;
        }

        /// <summary>Предыдущий язык по кругу (для кнопки «◀»).</summary>
        public static Language Previous(Language current)
        {
            int index = IndexOf(current);
            return All[(index - 1 + All.Length) % All.Length].Language;
        }

        public static int IndexOf(Language language)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Language == language)
                    return i;
            }

            return 0;
        }
    }
}
