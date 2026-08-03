namespace ServerMetricsApi.Models;

// Bildet eine Zeile aus der bestehenden memory_log Tabelle ab.
// Die Property-Namen entsprechen bewusst (in PascalCase) den Spaltennamen,
// damit Dapper sie automatisch zuordnen kann.
public class MemoryLogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int TotalMb { get; set; }
    public int UsedMb { get; set; }
    public int FreeMb { get; set; }
    public int AvailableMb { get; set; }
    public int SwapUsedMb { get; set; }
    public int WorldserverRssMb { get; set; }
    public int WorldserverThreads { get; set; }
    public int WorldserverFds { get; set; }
    public int WorldserverUptimeSec { get; set; }
    public int AuthserverRssMb { get; set; }
    public int CharactersOnline { get; set; }
}
