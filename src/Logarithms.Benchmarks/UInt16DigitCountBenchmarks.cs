using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;

namespace Logarithms.Benchmarks;

[IterationTime(150)]
public class UInt16DigitCountBenchmarks
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
        20000,
        ushort.MaxValue
    )]
    public ushort Value { get; set; }

    [Benchmark]
    public int DigitCount()
    {
        return Log10.DigitCount(Value);
    }

    [Benchmark(Baseline = true)]
    public int DigitCountMath()
    {
        return Log10.DigitCountMath(Value);
    }

    [Benchmark]
    [Obsolete(ReasonStrings.Obsoletion.MarkedToAvoidWarnings)]
    public int DigitCountToString()
    {
        return Log10.DigitCountToString(Value);
    }
}
