using SmbEnterprise.Transfer.Pipeline;

namespace SmbEnterprise.Tests;

public class AdaptiveChunkSizerTests
{
    [Fact]
    public void InitialChunkSize_IsDefault()
    {
        var sizer = new AdaptiveChunkSizer(
            initialChunkSize: 1 * 1024 * 1024,
            minChunkSize: 64 * 1024,
            maxChunkSize: 16 * 1024 * 1024);

        Assert.Equal(1 * 1024 * 1024, sizer.CurrentChunkSize);
    }

    [Fact]
    public void AfterFastTransfer_ChunkSizeIncreases()
    {
        var sizer = new AdaptiveChunkSizer(
            initialChunkSize: 1 * 1024 * 1024,
            minChunkSize: 64 * 1024,
            maxChunkSize: 16 * 1024 * 1024);

        // Simulate high throughput (100 MB/s — exceeds 50 MB/s fast threshold)
        for (int i = 0; i < 5; i++)
            sizer.RecordMetrics(bytesTransferred: 1 * 1024 * 1024, elapsed: TimeSpan.FromMilliseconds(10));

        Assert.True(sizer.CurrentChunkSize > 1 * 1024 * 1024);
    }

    [Fact]
    public void AfterSlowTransfer_ChunkSizeDecreases()
    {
        var sizer = new AdaptiveChunkSizer(
            initialChunkSize: 4 * 1024 * 1024,
            minChunkSize: 64 * 1024,
            maxChunkSize: 16 * 1024 * 1024);

        // Simulate very slow throughput
        for (int i = 0; i < 5; i++)
            sizer.RecordMetrics(bytesTransferred: 64 * 1024, elapsed: TimeSpan.FromSeconds(2));

        Assert.True(sizer.CurrentChunkSize < 4 * 1024 * 1024);
    }

    [Fact]
    public void ChunkSize_NeverExceedsMax()
    {
        var sizer = new AdaptiveChunkSizer(
            initialChunkSize: 15 * 1024 * 1024,
            minChunkSize: 64 * 1024,
            maxChunkSize: 16 * 1024 * 1024);

        for (int i = 0; i < 20; i++)
            sizer.RecordMetrics(bytesTransferred: 16 * 1024 * 1024, elapsed: TimeSpan.FromMilliseconds(10));

        Assert.True(sizer.CurrentChunkSize <= 16 * 1024 * 1024);
    }

    [Fact]
    public void ChunkSize_NeverDropsBelowMin()
    {
        var sizer = new AdaptiveChunkSizer(
            initialChunkSize: 128 * 1024,
            minChunkSize: 64 * 1024,
            maxChunkSize: 16 * 1024 * 1024);

        for (int i = 0; i < 20; i++)
            sizer.RecordMetrics(bytesTransferred: 1, elapsed: TimeSpan.FromSeconds(10));

        Assert.True(sizer.CurrentChunkSize >= 64 * 1024);
    }
}
