using Core.Windows.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert = Core.Testing.TestAssert;
namespace Core.IntegrationTests;

[TestClass] public sealed class Phase5ScanTests
{
    private sealed class InlineProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }
    [TestMethod] public async Task ActiveScanAndDuplicateIndexCancelAndReleaseOwnedResources()
    {
        var root = Path.Combine(Path.GetTempPath(), "core-cancel-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < 1001; i++) File.WriteAllText(Path.Combine(root, i + ".txt"), "fixture");
            var inspector = new FileInspector(NullLogger<FileInspector>.Instance);
            using var scan = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.ScanAsync(root, true, new InlineProgress(_ => scan.Cancel()), scan.Token));
            Assert.True(scan.IsCancellationRequested);
            var before = Directory.GetFiles(Path.GetTempPath(), "core-scan-*.db").ToHashSet(StringComparer.OrdinalIgnoreCase);
            using var duplicates = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.DuplicatesAsync(root, new InlineProgress(_ => duplicates.Cancel()), duplicates.Token));
            Assert.True(duplicates.IsCancellationRequested);
            Assert.Equal(0, Directory.GetFiles(Path.GetTempPath(), "core-scan-*.db").Count(path => !before.Contains(path)));
        }
        finally { Directory.Delete(root, true); }
    }
}
