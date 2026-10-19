using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;
[TestClass] public sealed class RefreshTests
{
    private static InspectionRow Row(string key) => new(key, [key], [key], new Dictionary<string, object?>());
    [TestMethod] public void UnsortedRefreshKeepsExistingOrderAndAppendsOnlyNewRows()
    {
        var refreshed = TableRows.KeepExistingOrder([Row("c"), Row("a"), Row("d"), Row("b")], ["a", "b", "c"]); Assert.Equal<string>(["a", "b", "c", "d"], refreshed.Select(r => r.Key)); var gone = TableRows.KeepExistingOrder([Row("d"), Row("c"), Row("a")], refreshed.Select(r => r.Key)); Assert.Equal<string>(["a", "c", "d"], gone.Select(r => r.Key));
    }
    [TestMethod] public void NativeTimestampPreservesUnreadableSocketObservationWithoutGrantingOwnerIdentity()
    {
        var row = new ConnectionRow("TCP4", "127.0.0.1", 444, "127.0.0.1", 443, "TimeWait", 0, "Exited") { NativeCreated = 123 }; var tracker = new ConnectionTracker(); var first = tracker.Update([row], DateTimeOffset.Now)[0]; var next = tracker.Update([row], DateTimeOffset.Now)[0]; Assert.Equal(first.ObservationId, next.ObservationId); Assert.Null(next.OwnerStarted);
    }
    [TestMethod] public void GraphHistoryUsesRealTimeAndBreaksAtMissingSamplesAndLongGaps()
    {
        var now = DateTimeOffset.Now; var points = new (DateTimeOffset, double?)[] { (now.AddSeconds(-80), 10), (now.AddSeconds(-60), 20), (now.AddSeconds(-58), 30), (now.AddSeconds(-56), null), (now.AddSeconds(-54), 40), (now.AddSeconds(-10), 50), (now.AddSeconds(1), 60) }; var segments = GraphMath.Segments(points, now, 60, 10); Assert.Equal(3, segments.Count); Assert.Equal(2, segments[0].Count); Assert.Equal(0d, segments[0][0].X); Assert.InRange(segments[2][0].X, 0.83, 0.84); Assert.Equal(1024d, GraphMath.RateCeiling(0)); Assert.Equal(4096d, GraphMath.RateCeiling(2500));
    }
}
