using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;
[TestClass] public sealed class ProcessRelationshipTests
{
    private static ProcessSample Sample(int id, int parent, DateTime? time) => new(id, parent, "fixture", time, "fixture.exe", null, null, null, null, null, "Normal", null, null, null);
    [TestMethod] public void ParentRequiresAnObservedCreationIdentityOlderThanTheChild()
    {
        var time = DateTime.Now; var child = Sample(2, 1, time); var parent = Sample(1, 0, time.AddMinutes(-1));
        Assert.True(ReferenceEquals(parent, ProcessRelationships.Parent(child, [parent])));
        Assert.Null(ProcessRelationships.Parent(child, [parent with { Started = time.AddSeconds(1) }]));
        Assert.Null(ProcessRelationships.Parent(child with { Started = null }, [parent]));
        Assert.Null(ProcessRelationships.Parent(child, [parent with { Started = null }]));
    }
    [TestMethod] public void OwnerNeverJoinsAReusedOrUnverifiablePidAndRetainsTheConnectionSnapshot()
    {
        var time = DateTime.Now;
        var connection = new ConnectionRow("TCP4", "127.0.0.1", 1234, "127.0.0.1", 443, "Established", 2, "original") { OwnerStarted = time, Executable = "original.exe" };
        var wrong = Sample(2, 1, time.AddSeconds(1));
        var original = ProcessRelationships.Owner(connection, [wrong]); Assert.Equal("original.exe", original.Path); Assert.Equal(time, original.Started!.Value);
        Assert.False(ReferenceEquals(wrong, original));
        var exact = wrong with { Started = time }; Assert.True(ReferenceEquals(exact, ProcessRelationships.Owner(connection, [exact])));
        Assert.Null(ProcessRelationships.Owner(connection with { OwnerStarted = null }, [exact]).Started);
    }
}
