using System.Diagnostics;
using ConsoleApp1.Models;
using SharedMemoryFramework;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: MMFWriter <mapName> <count> [ch1_0] [ch2_0] ...");
    Environment.Exit(1);
}

var mapName = args[0];
var count = int.Parse(args[1]);

var bus = new CoreDataBus(mapName);
bus.Open();

for (int i = 0; i < count; i++)
{
    var sample = new StructuredSample
    {
        Timestamp = i,
        Time = Stopwatch.GetTimestamp(),
        CH1 = args.Length > 2 + i * 2 ? double.Parse(args[2 + i * 2]) : i + 1.0,
        CH2 = args.Length > 3 + i * 2 ? double.Parse(args[3 + i * 2]) : (i + 1.0) * 10.0
    };
    bus.Write(ref sample);
}

bus.Dispose();
