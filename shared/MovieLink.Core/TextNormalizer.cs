#nullable enable
using System.Globalization;
using System.Text;

namespace MovieLink.Core
{
    /// <summary>
    /// Normalises names and titles for search: lower case, accents removed, punctuation dropped,
    /// a leading "the " removed. "Amélie" and "amelie" match; "The Matrix" matches "matrix".
    /// </summary>
    public static class TextNormalizer
    {
        public static string ForSearch(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string decomposed = text!.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            bool lastSpace = true;
            foreach (char ch in decomposed)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                    lastSpace = false;
                }
                else if (!lastSpace && ch != '\'' && ch != '’' && ch != '.')
                {
                    sb.Append(' ');
                    lastSpace = true;
                }
            }
            string s = sb.ToString().Trim();
            if (s.StartsWith("the ")) s = s.Substring(4);
            return s.Normalize(NormalizationForm.FormC);
        }
    }
}
