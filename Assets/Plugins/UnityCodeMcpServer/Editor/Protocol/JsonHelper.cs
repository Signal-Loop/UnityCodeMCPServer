using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityCodeMcpServer.Protocol
{
    /// <summary>
    /// JSON serialization utilities using System.Text.Json
    /// </summary>
    public static class JsonHelper
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private static readonly JsonSerializerOptions _indentedOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        public static JsonSerializerOptions Options => _options;
        public static JsonSerializerOptions IndentedOptions => _indentedOptions;

        /// <summary>
        /// Serialize an object to JSON string
        /// </summary>
        public static string Serialize<T>(T obj, bool indented = false)
        {
            return JsonSerializer.Serialize(obj, indented ? _indentedOptions : _options);
        }

        /// <summary>
        /// Deserialize a JSON string to an object
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _options);
        }

        /// <summary>
        /// Try to deserialize a JSON string to an object
        /// </summary>
        public static bool TryDeserialize<T>(string json, out T result)
        {
            try
            {
                result = JsonSerializer.Deserialize<T>(json, _options);
                return true;
            }
            catch (JsonException)
            {
                result = default;
                return false;
            }
        }

        /// <summary>
        /// Parse a JSON string to a JsonElement
        /// </summary>
        public static JsonElement ParseElement(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        /// <summary>
        /// Try to get a property value from a JsonElement
        /// </summary>
        public static bool TryGetProperty(this JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                return element.TryGetProperty(propertyName, out value);
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Get a string value from a JsonElement, or default if not found
        /// </summary>
        public static string GetStringOrDefault(this JsonElement element, string propertyName, string defaultValue = null)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            return defaultValue;
        }

        /// <summary>
        /// Get an int value from a JsonElement, or default if not found
        /// </summary>
        public static int GetIntOrDefault(this JsonElement element, string propertyName, int defaultValue = 0)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            return defaultValue;
        }

        /// <summary>
        /// Deserialize a JsonElement to a typed object
        /// </summary>
        public static T Deserialize<T>(this JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), _options);
        }

        /// <summary>
        /// Deserialize an object (typically JsonElement from deserialized JSON) to a typed object
        /// </summary>
        public static T Deserialize<T>(object obj)
        {
            if (obj == null)
            {
                return default;
            }

            if (obj is JsonElement element)
            {
                return element.Deserialize<T>();
            }

            if (obj is string json)
            {
                return Deserialize<T>(json);
            }

            // Fallback: serialize then deserialize
            var serialized = JsonSerializer.Serialize(obj, _options);
            return JsonSerializer.Deserialize<T>(serialized, _options);
        }

        /// <summary>
        /// Convert a nullable JsonElement to a non-nullable one, or return a default empty object
        /// </summary>
        public static JsonElement ToElement(this JsonElement? nullableElement)
        {
            if (nullableElement.HasValue)
            {
                return nullableElement.Value;
            }
            return ParseElement("{}");
        }
    }
}
