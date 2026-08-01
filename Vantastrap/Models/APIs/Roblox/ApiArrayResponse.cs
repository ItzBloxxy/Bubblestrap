namespace vantastrap.Models.APIs.Roblox
{
    public class ApiArrayResponse<T>
    {
        [JsonPropertyName("data")]
        public IEnumerable<T> Data { get; set; } = null!;
    }
}
