using vantastrap.Models.APIs.RoValra;

namespace vantastrap.Models.APIs
{
    public class RoValraTimeResponse
    {
        [JsonPropertyName("servers")]
        public List<RoValraServer>? Servers { get; set; } = null!;

        [JsonPropertyName("status")]
        public string Status = null!;
    }
}
