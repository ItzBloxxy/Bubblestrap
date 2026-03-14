using System.ComponentModel;

namespace Bloxstrap.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WebEnvironment
    {
        [Description("prod")]
        Production,

        [Description("stage")]
        Staging,

        [Description("dev")]
        Dev,

        [Description("local")]
        Local
    }
}