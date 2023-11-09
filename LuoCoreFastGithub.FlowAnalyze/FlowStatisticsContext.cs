using System.Text.Json.Serialization;

namespace LuoCoreFastGithub.FlowAnalyze
{
    [JsonSerializable(typeof(FlowStatistics))]
    public partial class FlowStatisticsContext : JsonSerializerContext
    {
    }
}
