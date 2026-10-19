using Core.Domain.Models;
namespace Core.Application.Services;

public static class NetworkTables
{
    public static InspectionTable Traffic(InspectionTable adapters)
    {
        int[] columns = [0, 2, 15, 16, 17, 18, 11, 12, 13, 14];
        var rows = TableRows.From(adapters).Select(row => {
            var status = !long.TryParse(row.Raw.ElementAtOrDefault(11), out _) ? "Windows counter provider unavailable; inspect Properties"
                : row.Raw.ElementAtOrDefault(2) != "Up" ? "Interface " + row.Raw.ElementAtOrDefault(2) + "; rates unavailable"
                : !double.TryParse(row.Raw.ElementAtOrDefault(15), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) ? "Waiting for second sample / counter reset" : "Available";
            return row with { Subject = row, Cells = [.. columns.Select(i => row.Cells.ElementAtOrDefault(i) ?? "Unavailable"), status], Raw = [.. columns.Select(i => row.Raw.ElementAtOrDefault(i) ?? "Unavailable"), status] };
        }).ToArray();
        return new("Interface traffic", ["Interface", "State", "Receive/s", "Send/s", "Packets in/s", "Packets out/s", "Received B", "Sent B", "Errors in/out", "Discards in/out", "Counter status"], rows.Select(r => r.Cells).ToArray(), "Interface counters only. First/down/reset samples are unavailable. Packet capture, direction attribution, per-process bandwidth and connection byte counts are unsupported.", rows);
    }
}
