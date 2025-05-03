using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;

namespace Logarithms.Benchmarks;

[IterationTime(150)]
public class UInt32DigitCountBenchmarks
{
    [Params(
        0,
        1,
        8,
        9,
        10,
        30,
        98,
        99,
        100,
        200,
        255,
        999,
        1000,
        2000,
        9999,
        10000,
        99999,
        100000,
        999999,
        1000000,
        9999999,
        10000000,
        99999999,
        100000000,
        999999999,
        1000000000,
        2147483647,
        uint.MaxValue
    )]
    public uint Value { get; set; }

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

    [Benchmark]
    public int DigitCountCompareAll()
    {
        return Log10.DigitCountCompareAll(Value);
    }

    [Benchmark]
    public int DigitCountCompareAllBitwise()
    {
        return Log10.DigitCountCompareAllBitwise(Value);
    }
}
