using ConsoleApp1.Models;
using SharedMemoryFramework;
using Xunit;

namespace WebAPI.Tests;

public class CoreDataBusTests
{
    [Fact]
    public void Create_InitializesHeaderCorrectly()
    {
        var bus = new CoreDataBus(mapName: "TEST_CREATE_HEADER");
        try
        {
            bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

            Assert.Equal(0L, bus.WriteIndex);
            Assert.Equal(2, bus.ChannelCount);
            Assert.Equal(100, bus.BufferLength);
            Assert.Equal(1_000_000, bus.SampleRate);
            Assert.True(bus.ReferenceTick > 0);
            Assert.True(bus.ReferenceUtcTicks > 0);
        }
        finally
        {
            bus.Dispose();
        }
    }

    [Fact]
    public void Write_TryReadLatestSingle_RoundTrip()
    {
        var bus = new CoreDataBus(mapName: "TEST_WRITE_READ_ROUNDTRIP");
        try
        {
            bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

            var written = new StructuredSample
            {
                Timestamp = 42,
                Time = 123456789,
                CH1 = 3.14,
                CH2 = 2.718,
                Vis = 1.0,
                Cn2 = 0.5
            };
            bus.Write(ref written);

            bool ok = bus.TryReadLatestSingle(out var read);
            Assert.True(ok);
            Assert.Equal(written.Timestamp, read.Timestamp);
            Assert.Equal(written.Time, read.Time);
            Assert.Equal(written.CH1, read.CH1);
            Assert.Equal(written.CH2, read.CH2);
            Assert.Equal(written.Vis, read.Vis);
            Assert.Equal(written.Cn2, read.Cn2);
        }
        finally
        {
            bus.Dispose();
        }
    }

    [Fact]
    public void TryReadLatestSingle_EmptyBus_ReturnsFalse()
    {
        var bus = new CoreDataBus(mapName: "TEST_EMPTY_BUS");
        try
        {
            bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

            bool ok = bus.TryReadLatestSingle(out var sample);
            Assert.False(ok);
            Assert.Equal(default(StructuredSample), sample);
        }
        finally
        {
            bus.Dispose();
        }
    }

    [Fact]
    public void TryReadLatestSingle_AfterMultipleWrites_ReturnsLatest()
    {
        var bus = new CoreDataBus(mapName: "TEST_READ_LATEST");
        try
        {
            bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

            for (int i = 0; i < 5; i++)
            {
                var s = new StructuredSample { Timestamp = i, CH1 = i * 10.0 };
                bus.Write(ref s);
            }

            bool ok = bus.TryReadLatestSingle(out var read);
            Assert.True(ok);
            Assert.Equal(4L, read.Timestamp);
            Assert.Equal(40.0, read.CH1);
        }
        finally
        {
            bus.Dispose();
        }
    }

    [Fact]
    public void Write_BufferLengthPlusOne_WrapsCorrectly()
    {
        var bus = new CoreDataBus(mapName: "TEST_WRAP");
        try
        {
            bus.Create(channels: 2, buffer: 4, sampleRate: 1_000_000);

            // Write 5 samples into a buffer of 4 — last one wraps
            for (int i = 0; i < 5; i++)
            {
                var s = new StructuredSample { Timestamp = i, CH1 = i * 10.0 };
                bus.Write(ref s);
            }

            // WriteIndex should be 5 (monotonic, not modulo)
            Assert.Equal(5L, bus.WriteIndex);

            // Latest should be sample 4
            bool ok = bus.TryReadLatestSingle(out var read);
            Assert.True(ok);
            Assert.Equal(4L, read.Timestamp);
            Assert.Equal(40.0, read.CH1);
        }
        finally
        {
            bus.Dispose();
        }
    }

    [Fact]
    public void Open_MapsHeaderPointerCorrectly()
    {
        const string mapName = "TEST_OPEN_MAP";
        var creator = new CoreDataBus(mapName: mapName);
        try
        {
            creator.Create(channels: 2, buffer: 200, sampleRate: 500_000);

            var opener = new CoreDataBus(mapName: mapName);
            try
            {
                opener.Open();

                Assert.Equal(creator.WriteIndex, opener.WriteIndex);
                Assert.Equal(creator.ChannelCount, opener.ChannelCount);
                Assert.Equal(creator.BufferLength, opener.BufferLength);
                Assert.Equal(creator.SampleRate, opener.SampleRate);
                Assert.Equal(creator.ReferenceTick, opener.ReferenceTick);
                Assert.Equal(creator.ReferenceUtcTicks, opener.ReferenceUtcTicks);
            }
            finally
            {
                opener.Dispose();
            }
        }
        finally
        {
            creator.Dispose();
        }
    }
}
