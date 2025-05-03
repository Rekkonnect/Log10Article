using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;

namespace Logarithms.Benchmarks;

[IterationTime(150)]
public class ByteDigitCountBenchmarks
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
        255
    )]
    public byte Value { get; set; }

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
