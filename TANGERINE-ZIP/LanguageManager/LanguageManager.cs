using System.Resources;
using System.Reflection;
using System.Globalization;

public static class LanguageManager
{
    private static readonly ResourceManager rm =
        new ResourceManager(
            "TANGERINE_ZIP.LanguageManager.UiStrings",
            Assembly.GetExecutingAssembly());

    public static string Get(string key)
    {
        return rm.GetString(key, CultureInfo.CurrentUICulture)
            ?? rm.GetString(key, CultureInfo.GetCultureInfo("en-US"))
            ?? key;
    }
}
