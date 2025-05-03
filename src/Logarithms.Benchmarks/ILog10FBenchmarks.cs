using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;

namespace Logarithms.Benchmarks;

[IterationTime(150)]
public class ILog10FBenchmarks
{
    [Params(
        0.0001f,
        0.001f,
        0.01f,
        0.1f,
        1f,
        9f,
        9.999999f,
        10f,
        10.01f,
        19f,
        99f,
        99.999f,
        100f,
        999f,
        1000f,
        9999f,
        10000f,
        99999f,
        100000f,
        999999f,
        1000000f,
        9999999f,
        10000000f,
        99999999f,
        100000000f,
        999999999f,
        1000000000f,
        9999999999f
    )]
    public float Value { get; set; }

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
    public float MathLog10Baseline()
    {
        return MathF.Log10(Value);
    }
}
