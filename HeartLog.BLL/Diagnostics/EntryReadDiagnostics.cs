using System.Diagnostics;

namespace HeartLog.BLL.Diagnostics;

// Opt-in timings only. Never attach user IDs, entry IDs, notes, or selections.
public static class EntryReadDiagnostics
{
    public const string SourceName = "HeartLog.EntryReads";
    public static readonly ActivitySource Source = new(SourceName);
}
