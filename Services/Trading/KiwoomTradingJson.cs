using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TradingDashboard.Services.Trading
{
    internal static class KiwoomTradingJson
    {
        public static string ReadString(JsonNode? node, string propertyName)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(propertyName, out JsonNode? value) || value is null)
                return string.Empty;

            return value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>() ?? string.Empty,
                JsonValueKind.Number => value.ToJsonString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.ToString()
            };
        }

        public static long ReadLong(JsonNode? node, string propertyName)
        {
            string value = ReadString(node, propertyName);
            return ParseLong(value);
        }

        public static decimal ReadDecimal(JsonNode? node, string propertyName)
        {
            string value = ReadString(node, propertyName);
            return ParseDecimal(value);
        }

        public static long ParseLong(string? value)
        {
            string normalized = NormalizeNumber(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return 0;

            if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
                return result;

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalResult))
                return (long)decimalResult;

            return 0;
        }

        public static decimal ParseDecimal(string? value)
        {
            string normalized = NormalizeNumber(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return 0;

            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result)
                ? result
                : 0;
        }

        public static string NormalizeStockCode(string? value)
        {
            string text = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (text.StartsWith("A", StringComparison.OrdinalIgnoreCase) && text.Length >= 7)
                text = text[1..];

            text = text.Replace("_NX", string.Empty, StringComparison.OrdinalIgnoreCase)
                       .Replace("_AL", string.Empty, StringComparison.OrdinalIgnoreCase);

            return text.Length > 6 ? text[^6..] : text;
        }

        public static string FindStringRecursive(JsonNode? node, params string[] names)
        {
            JsonNode? found = FindNodeRecursive(node, names);
            if (found is null)
                return string.Empty;

            return found.GetValueKind() switch
            {
                JsonValueKind.String => found.GetValue<string>() ?? string.Empty,
                JsonValueKind.Number => found.ToJsonString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => found.ToString()
            };
        }

        public static long FindLongRecursive(JsonNode? node, params string[] names) =>
            ParseLong(FindStringRecursive(node, names));

        public static JsonArray? FindArrayRecursive(JsonNode? node, params string[] names)
        {
            JsonNode? found = FindNodeRecursive(node, names);
            return found as JsonArray;
        }

        private static string NormalizeNumber(string? value)
        {
            string text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text) || text == "-")
                return string.Empty;

            text = text.Replace(",", string.Empty)
                       .Replace("+", string.Empty)
                       .Replace("%", string.Empty)
                       .Replace("--", "-", StringComparison.Ordinal);

            return text;
        }

        private static JsonNode? FindNodeRecursive(JsonNode? node, params string[] names)
        {
            if (node is null || names.Length == 0)
                return null;

            if (node is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    if (names.Any(name => string.Equals(name, property.Key, StringComparison.OrdinalIgnoreCase)))
                        return property.Value;

                    JsonNode? child = FindNodeRecursive(property.Value, names);
                    if (child is not null)
                        return child;
                }
            }
            else if (node is JsonArray arr)
            {
                foreach (JsonNode? item in arr)
                {
                    JsonNode? child = FindNodeRecursive(item, names);
                    if (child is not null)
                        return child;
                }
            }

            return null;
        }
    }
}
