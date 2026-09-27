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
        {
            string code = stageCode is not null && StageCodePattern.IsMatch(stageCode)
                ? stageCode : UnknownStageCode;
            string detail = string.IsNullOrWhiteSpace(exceptionMessage)
                ? LanguageManager.Get("ErrorTitle") : exceptionMessage.Trim();
            return LanguageManager.Get("StageCodeTip1") + code + Environment.NewLine + Environment.NewLine +
                LanguageManager.Get("StageCodeTip2") + detail;
        }
    }
}
