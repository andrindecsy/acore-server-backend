namespace ServerMetricsApi.Models;

public class ServerEvent
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
