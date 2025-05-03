using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;

namespace Logarithms.Benchmarks;

[IterationTime(150)]
public class FullILog10SearchOrderBenchmarks
{
    [Params(
        0.000000001,
        0.00000001,
        0.0000001,
        0.000001,
        0.00001,
        0.0001,
        0.001,
        0.01,
        0.1,
        1,
        9,
        9.999999,
        10,
        19,
        99.999,
        100,
        999,
        1000,
        9999,
        10000,
        99999,
        999999,
        9999999,
        10000000,
        100000000,
        9999999999
    )]
    public double Value { get; set; }

    [Benchmark(Baseline = true)]
    public int ILog()
    {
        return Log10.ILog(Value);
    }

    [Benchmark]
    public int ILogSplit()
    {
        return Log10.ILogSplit(Value);
    }
}
