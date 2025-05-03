using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;

namespace Logarithms.Benchmarks;

[IterationTime(750)]
public class UInt64RandomDigitCountBenchmarks
{
    [Benchmark]
    public int DigitCount()
    {
        return DigitCountSum(Log10.DigitCount);
    }

    [Benchmark(Baseline = true)]
    public int DigitCountStl()
    {
        return DigitCountSum(Log10.DigitCountStl);
    }

    private static int DigitCountSum(Func<ulong, int> digitCounter)
    {
        int sum = 0;

        for (int i = 0; i < 4; i++)
        {
            sum += digitCounter(1);
            sum += digitCounter(2);
            sum += digitCounter(100321);
            sum += digitCounter(38);
            sum += digitCounter(13290);
            sum += digitCounter(3128791238719);
            sum += digitCounter(ulong.MaxValue);
            sum += digitCounter(9401);
            sum += digitCounter(100000000);
            sum += digitCounter(10341245214532535663);
            sum += digitCounter(132904351211);
            sum += digitCounter(5429138726719879);
            sum += digitCounter(103);
            sum += digitCounter(5429138726719812379);
            sum += digitCounter(0);
            sum += digitCounter(ulong.MaxValue);
        }

        return sum;
    }
}
