using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SearchBotApplication
{
    public class Config(int apiId, string apiHash, string phoneNumber, string applicationVersion, long searchBotId)
    {
        [JsonPropertyName("api_id")]
        public required int ApiId { get; set; } = apiId;
        [JsonPropertyName("api_hash")]
        public required string ApiHash { get; set; } = apiHash;
        [JsonPropertyName("phone_number")]
        public required string PhoneNumber { get; set; } = phoneNumber;
        [JsonPropertyName("application_version")]
        public required string ApplicationVersion { get; set; } = applicationVersion;
        [JsonPropertyName("search_bot_id")]
        public required long SearchBotId { get; set; } = searchBotId;
    }
}
