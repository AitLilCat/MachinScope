using Core.Application.Services;
using Core.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.UnitTests;

[TestClass] public sealed class InspectionDescriptionTests
{
    [TestMethod] public void EveryRegisteredDescriptionHasUniqueStableMetadata()
    {
        Assert.Equal(InspectionDescriptions.Tabs.Count, InspectionDescriptions.Tabs.Select(t => t.Id).Distinct().Count());
        Assert.True(InspectionDescriptions.Tabs.All(t => t.Title.Length > 0 && t.ShortDescription.Length > 25));
    }
    [TestMethod] public void SwitchingTabsChangesOnlyDescriptionStateAndRejectsUnavailableOrUnknownTabs()
    {
        var context = new InspectionContext(InspectionScope.Connection, PlatformKind.Windows);
        Assert.True(context.Select("Connection")); Assert.Equal("connection.endpoint", context.Current.Id);
        var endpoint = context.Current; Assert.True(context.Select("Owner")); Assert.Equal("connection.owner", context.Current.Id);
        Assert.True(context.Select("Connection")); Assert.True(ReferenceEquals(endpoint, context.Current));
        Assert.False(context.SelectId("not-registered")); Assert.True(ReferenceEquals(endpoint, context.Current));
        var linux = new InspectionContext(InspectionScope.Device, PlatformKind.Linux);
        Assert.False(linux.Select("Driver")); Assert.Equal("general", linux.Current.Id);
    }
    [TestMethod] public void DeviceAndDiskContextsDoNotPromiseNetworkOrDiskSpecificAttribution()
    {
        Assert.Equal("device.connection", InspectionDescriptions.Resolve(InspectionScope.Device, "Connection").Id);
        Assert.Equal("connection.endpoint", InspectionDescriptions.Resolve(InspectionScope.Connection, "Connection").Id);
        var disk = InspectionDescriptions.Resolve(InspectionScope.Disk, "Events"); Assert.Contains("not guaranteed", disk.ShortDescription);
        var adapter = InspectionDescriptions.Resolve(InspectionScope.Adapter, "Connections"); Assert.Contains("Wildcard", adapter.ShortDescription);
        Assert.Equal("general", InspectionDescriptions.Resolve(InspectionScope.General, "Unregistered provider tab").Id);
    }
}
