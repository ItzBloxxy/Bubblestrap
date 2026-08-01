namespace vantastrap.Models.vantastrapRPC;

public class Message
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = null!;

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}
