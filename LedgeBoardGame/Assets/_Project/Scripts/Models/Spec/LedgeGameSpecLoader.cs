using Newtonsoft.Json;

namespace Magi.LedgeBoardGame.Models.Spec
{
    public static class LedgeGameSpecLoader
    {
        public static LedgeGameSpec LoadFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var settings = new JsonSerializerSettings
            {
                // Case-insensitive property matching
                ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                },
                // Handle missing properties gracefully
                MissingMemberHandling = MissingMemberHandling.Ignore,
                // Preserve null values
                NullValueHandling = NullValueHandling.Include
            };

            return JsonConvert.DeserializeObject<LedgeGameSpec>(json, settings);
        }
    }
}

