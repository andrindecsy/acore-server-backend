namespace ServerMetricsApi.Models;

public class StatusSummary
{
    public bool IsOnline { get; set; }
    public DateTime LastMeasurement { get; set; }
    public int WorldserverRssMb { get; set; }
    public int AuthserverRssMb { get; set; }
    public int CharactersOnline { get; set; }
    public int WorldserverUptimeSec { get; set; }
}
