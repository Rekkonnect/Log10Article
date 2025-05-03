using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;

namespace Logarithms.Benchmarks;

[IterationTime(150)]
public class ILog10Benchmarks
{
    [Params(
        0.0001,
        0.001,
        0.01,
        0.1,
        1,
        9,
        9.999999,
        10,
        10.01,
        19,
        99,
        99.999,
        100,
        999,
        1000,
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
        9999999999
    )]
    public double Value { get; set; }

    [Benchmark]
    public int CustomILog()
    {
        return Log10.NonNegativeILog(Value);
    }

    [Benchmark]
    [Obsolete(ReasonStrings.Obsoletion.MarkedToAvoidWarnings)]
    public int CustomILogSwitch()
    {
        return Log10.NonNegativeILogSwitch(Value);
    }

    [Benchmark]
    public int MathILog()
    {
        return Log10.MathNonNegativeILog(Value);
    }

    [Benchmark(Baseline = true)]
    public double MathLog10Baseline()
    {
        return Math.Log10(Value);
    }
}
