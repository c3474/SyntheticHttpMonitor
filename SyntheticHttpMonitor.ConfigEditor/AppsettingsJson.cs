using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SyntheticHttpMonitor.Options;

namespace SyntheticHttpMonitor.ConfigEditor;

internal static class AppsettingsJson
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonObject ParseRoot(string json) =>
        JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

    public static SyntheticMonitorOptions ReadSyntheticMonitor(JsonObject root) =>
        root["SyntheticMonitor"]?.Deserialize<SyntheticMonitorOptions>(SerializerOptions) ?? new SyntheticMonitorOptions();

    public static AlertingOptions ReadAlerting(JsonObject root) =>
        root["Alerting"]?.Deserialize<AlertingOptions>(SerializerOptions) ?? new AlertingOptions();

    public static SmtpOptions ReadSmtp(JsonObject root) =>
        root["Smtp"]?.Deserialize<SmtpOptions>(SerializerOptions) ?? new SmtpOptions();

    public static TicketingApiOptions ReadTicketing(JsonObject root) =>
        root["TicketingApi"]?.Deserialize<TicketingApiOptions>(SerializerOptions) ?? new TicketingApiOptions();

    public static PagerDutyOptions ReadPagerDuty(JsonObject root) =>
        root["PagerDuty"]?.Deserialize<PagerDutyOptions>(SerializerOptions) ?? new PagerDutyOptions();

    public static void ApplySections(
        JsonObject root,
        SyntheticMonitorOptions synthetic,
        AlertingOptions alerting,
        SmtpOptions smtp,
        TicketingApiOptions ticketing,
        PagerDutyOptions pagerDuty)
    {
        root["SyntheticMonitor"] = JsonSerializer.SerializeToNode(synthetic, SerializerOptions);
        root["Alerting"] = JsonSerializer.SerializeToNode(alerting, SerializerOptions);
        root["Smtp"] = JsonSerializer.SerializeToNode(smtp, SerializerOptions);
        root["TicketingApi"] = JsonSerializer.SerializeToNode(ticketing, SerializerOptions);
        root["PagerDuty"] = JsonSerializer.SerializeToNode(pagerDuty, SerializerOptions);
    }

    public static string SerializeRoot(JsonObject root) =>
        root.ToJsonString(WriteOptions);
}
