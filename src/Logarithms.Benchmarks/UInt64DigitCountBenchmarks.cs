using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;

namespace Logarithms.Benchmarks;

[IterationTime(150)]
public class UInt64DigitCountBenchmarks
{
    [Params(
        0,
        1,
        9,
        10,
        99,
        100,
        200,
        999,
        1000,
        2000,
        9999,
        10000,
        99999,
        1000000,
        9999999,
        100000000,
        999999999,
        1000000000,
        2147483647,
        uint.MaxValue,
        9999999999,
        10000000000,
        99999999999,
        1000000000000,
        9999999999999,
        1000000000000000,
        9999999999999999,
        100000000000000000,
        999999999999999999,
        ulong.MaxValue
    )]
    public ulong Value { get; set; }

    [Benchmark]
    public int DigitCount()
    {
        return Log10.DigitCount(Value);
    }

    [Benchmark(Baseline = true)]
    public int DigitCountStl()
    {
        return Log10.DigitCountStl(Value);
    }
}
