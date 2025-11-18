using System.Text.Json;

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

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<LedgeGameSpec>(json, options);
        }
    }
}

