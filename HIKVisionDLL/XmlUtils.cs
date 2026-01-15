using System;
using System.Text.RegularExpressions;

namespace HIKVisionDLL
{
    public static class XmlUtils
    {
        public static string ExtractValue(string xml, string tagName)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(tagName))
                return null;
            
            string pattern = $@"<(?:[^:>]+:)?{tagName}(?:\s+[^>]*)?>(.*?)</(?:[^:>]+:)?{tagName}>";
            var match = Regex.Match(xml, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }
}
