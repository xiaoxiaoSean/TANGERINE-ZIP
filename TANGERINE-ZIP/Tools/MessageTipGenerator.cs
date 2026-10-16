using System;
using System.Text.RegularExpressions;

namespace TANGERINE_ZIP.Tools
{
   public static class MessageTipGenerator
    {
        private const string UnknownStageCode = "UNKWN0001";
        private static readonly Regex StageCodePattern = new("^[A-Z]{5}[0-9]{4}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Displays a language-neutral five-letter component and four-digit
        /// detail code with localized labels. Malformed codes are replaced so
        /// diagnostic labels cannot contain arbitrary exception text.
        /// </summary>
        public static string GenerateTip(string stageCode, string exceptionMessage)
            => GenerateTip(stageCode, exceptionMessage, null);

        /// <summary>
        /// Builds a complete, localized diagnostic for a dialog. The stage code
        /// identifies the failing boundary; the optional exception selects a
        /// more useful recovery hint without changing that stable identifier.
        /// Never inspect exception message text to classify a failure because
        /// framework messages vary by operating system and UI language.
        /// </summary>
        public static string GenerateTip(string stageCode, string exceptionMessage, Exception? exception)
        {
            string code = stageCode is not null && StageCodePattern.IsMatch(stageCode)
                ? stageCode : UnknownStageCode;
            string detail = string.IsNullOrWhiteSpace(exceptionMessage)
                ? LanguageManager.Get("ErrorTitle") : exceptionMessage.Trim();
            string category = GetCategory(code, exception);
            return LanguageManager.Get("StageCodeTip1") + code + Environment.NewLine + Environment.NewLine +
                LanguageManager.Get("StageCodeTip2") + detail + Environment.NewLine + Environment.NewLine +
                LanguageManager.Get("ErrorPossibleCause") + Environment.NewLine +
                LanguageManager.Get("ErrorCause" + category) + Environment.NewLine + Environment.NewLine +
                LanguageManager.Get("ErrorSuggestedAction") + Environment.NewLine +
                LanguageManager.Get("ErrorAction" + category);
        }

        private static string GetCategory(string stageCode, Exception? exception)
        {
            // Known domain failures retain their own message and stage code.
            // Exception types take precedence over module hints because an
            // archive operation can also fail due to a locked output file.
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is UnauthorizedAccessException or System.Security.SecurityException) return "Permission";
                if (current is FileNotFoundException or DirectoryNotFoundException) return "Missing";
                if (current is System.IO.InvalidDataException) return "Archive";
                if (current is OutOfMemoryException) return "Memory";
                if (current is IOException) return "Io";
            }

            return stageCode[..5] switch
            {
                "PWDAR" or "ZIPPD" => "Password",
                "ARCSV" or "ARCED" or "ARUPD" or "NESTR" => "Archive",
                "TMPDR" or "CPRFL" or "MECFG" or "MESVC" or "COLRS" => "Settings",
                "RARTL" or "SZTLS" => "Tool",
                "CTXMN" or "CMSPW" or "SFTAS" or "DEFAS" => "Permission",
                _ => "General"
            };
        }
    }
}
