using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace UnityCodeMcpServer.Protocol
{
    /// <summary>
    /// JSON serialization utilities using Newtonsoft.Json
    /// </summary>
    public static class JsonHelper
    {
        private static readonly JsonSerializerSettings _settings = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        private static readonly JsonSerializerSettings _indentedSettings = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private static readonly JsonSerializer _serializer = JsonSerializer.Create(_settings);
        private static readonly JsonSerializer _indentedSerializer = JsonSerializer.Create(_indentedSettings);

        public static JsonSerializerSettings Settings => _settings;
        public static JsonSerializerSettings IndentedSettings => _indentedSettings;
        public static JsonSerializer Serializer => _serializer;
        public static JsonSerializer IndentedSerializer => _indentedSerializer;

        public static string Serialize<T>(T obj, bool indented = false)
        {
            return JsonConvert.SerializeObject(obj, indented ? _indentedSettings : _settings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, _settings);
        }

        public static bool TryDeserialize<T>(string json, out T result)
        {
            try
            {
                result = JsonConvert.DeserializeObject<T>(json, _settings);
                return true;
            }
            catch (JsonException)
            {
                result = default;
                return false;
            }
        }

        public static JToken ParseElement(string json)
        {
            return JToken.Parse(json);
        }

        public static bool TryGetProperty(this JToken element, string propertyName, out JToken value)
        {
            if (element is JObject jsonObject)
            {
                return jsonObject.TryGetValue(propertyName, out value);
            }

            value = default;
            return false;
        }

        public static string GetStringOrDefault(this JToken element, string propertyName, string defaultValue = null)
        {
            if (element.TryGetProperty(propertyName, out JToken prop) && prop.Type == JTokenType.String)
            {
                return prop.Value<string>();
            }

            return defaultValue;
        }

        public static int GetIntOrDefault(this JToken element, string propertyName, int defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out JToken prop) && prop.Type == JTokenType.Integer)
            {
                return prop.Value<int>();
            }

            return defaultValue;
        }

        public static T Deserialize<T>(this JToken element)
        {
            return element == null ? default : element.ToObject<T>(_serializer);
        }

        public static T Deserialize<T>(object obj)
        {
            if (obj == null)
            {
                return default;
            }

            if (obj is JToken token)
            {
                return token.Deserialize<T>();
            }

            if (obj is string json)
            {
                return Deserialize<T>(json);
            }

            return JToken.FromObject(obj, _serializer).ToObject<T>(_serializer);
        }

        public static JToken ToElement(this JToken nullableElement)
        {
            return nullableElement ?? ParseElement("{}");
        }
    }
}
