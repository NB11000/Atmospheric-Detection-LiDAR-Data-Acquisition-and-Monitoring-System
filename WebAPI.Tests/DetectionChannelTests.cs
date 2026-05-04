using System.Buffers;
using System.Threading.Channels;
using ConsoleApp1.Models;
using Xunit;

namespace WebAPI.Tests;

public class DetectionChannelTests
{
    // Replicates the production configuration from AD_Controlcs.CreateNewDataChannel()
    private static Channel<DetectionBatch> CreateChannel(int capacity = 8)
    {
        return Channel.CreateBounded<DetectionBatch>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true
            },
            dropped =>
            {
                if (dropped.Samples != null)
                    ArrayPool<StructuredSample>.Shared.Return(dropped.Samples);
            });
    }

    [Fact]
    public void WriteThenRead_ArrayReferencePreserved()
    {
        var channel = CreateChannel(capacity: 4);
        var samples = ArrayPool<StructuredSample>.Shared.Rent(10);
        try
        {
            var written = new DetectionBatch { Samples = samples, Count = 10 };
            Assert.True(channel.Writer.TryWrite(written));

            Assert.True(channel.Reader.TryRead(out var read));
            Assert.Same(written.Samples, read.Samples);
            Assert.Equal(10, read.Count);
        }
        finally
        {
            ArrayPool<StructuredSample>.Shared.Return(samples);
        }
    }

    [Fact]
    public void DropOldest_ReturnsEjectedArray()
    {
        var channel = CreateChannel(capacity: 4);
        StructuredSample[][] arrays = new StructuredSample[5][];
        for (int i = 0; i < 5; i++)
            arrays[i] = ArrayPool<StructuredSample>.Shared.Rent(1);

        // Fill channel to capacity
        for (int i = 0; i < 4; i++)
            Assert.True(channel.Writer.TryWrite(new DetectionBatch { Samples = arrays[i], Count = 1 }));

        // 5th write succeeds (DropOldest drops arrays[0], callback returns it to pool)
        Assert.True(channel.Writer.TryWrite(new DetectionBatch { Samples = arrays[4], Count = 1 }));

        // arrays[0] was dropped — should NOT appear when reading
        for (int i = 0; i < 4; i++)
        {
            Assert.True(channel.Reader.TryRead(out var batch));
            Assert.NotSame(arrays[0], batch.Samples);
            ArrayPool<StructuredSample>.Shared.Return(batch.Samples);
        }
        // arrays[0] was already returned by dropped callback
    }

    [Fact]
    public void TryWriteFailure_CallerReturns_MutuallyExclusive()
    {
        var channel = CreateChannel(capacity: 4);
        channel.Writer.Complete();

        var samples = ArrayPool<StructuredSample>.Shared.Rent(1);
        Assert.False(channel.Writer.TryWrite(new DetectionBatch { Samples = samples, Count = 1 }));

        // Caller returns array (simulating Analysis TryWrite-failure path)
        ArrayPool<StructuredSample>.Shared.Return(samples);

        // Channel empty — dropped callback never fired for this item
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public void DrainPattern_ReturnsAllRemainingArrays()
    {
        var channel = CreateChannel(capacity: 8);

        for (int i = 0; i < 3; i++)
        {
            var arr = ArrayPool<StructuredSample>.Shared.Rent(1);
            channel.Writer.TryWrite(new DetectionBatch { Samples = arr, Count = 1 });
        }

        // Same drain pattern used in init()/stop()
        while (channel.Reader.TryRead(out var batch))
        {
            if (batch.Samples != null)
                ArrayPool<StructuredSample>.Shared.Return(batch.Samples);
        }

        Assert.False(channel.Reader.TryRead(out _));
    }
}
