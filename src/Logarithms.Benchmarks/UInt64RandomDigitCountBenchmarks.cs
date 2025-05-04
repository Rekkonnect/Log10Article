using BenchmarkDotNet.Attributes;
using Logarithms.Implementations;
using System.Runtime.CompilerServices;

namespace Logarithms.Benchmarks;

[IterationTime(750)]
public class UInt64RandomDigitCountBenchmarks
{
    [Benchmark(Baseline = true)]
    public ulong DigitCountStl()
    {
        return DigitCountSum(Log10.DigitCountStl);
    }

    [Benchmark]
    public ulong DigitCount()
    {
        return DigitCountSum(Log10.DigitCount);
    }

    [Benchmark]
    public ulong DigitCountStl_Interdependent()
    {
        return DigitCountSumInterdependent(Log10.DigitCountStl);
    }

    [Benchmark]
    public ulong DigitCount_Interdependent()
    {
        return DigitCountSumInterdependent(Log10.DigitCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong DigitCountSum(Func<ulong, int> digitCounter)
    {
        ulong sum = 0;

        sum += (uint)digitCounter(1);
        sum += (uint)digitCounter(2);
        sum += (uint)digitCounter(100321);
        sum += (uint)digitCounter(38);
        sum += (uint)digitCounter(13290);
        sum += (uint)digitCounter(3128791238719);
        sum += (uint)digitCounter(ulong.MaxValue);
        sum += (uint)digitCounter(9401);
        sum += (uint)digitCounter(100000000);
        sum += (uint)digitCounter(10341245214532535663);
        sum += (uint)digitCounter(132904351211);
        sum += (uint)digitCounter(5429138726719879);
        sum += (uint)digitCounter(103);
        sum += (uint)digitCounter(5429138726719812379);
        sum += (uint)digitCounter(0);
        sum += (uint)digitCounter(ulong.MaxValue);

        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong DigitCountSumInterdependent(Func<ulong, int> digitCounter)
    {
        ulong sum = 0;

        sum += (uint)digitCounter(sum + 1);
        sum += (uint)digitCounter(sum + 2);
        sum += (uint)digitCounter(sum + 100321);
        sum += (uint)digitCounter(sum + 38);
        sum += (uint)digitCounter(sum + 13290);
        sum += (uint)digitCounter(sum + 3128791238719);
        sum += (uint)digitCounter(sum + ulong.MaxValue);
        sum += (uint)digitCounter(sum + 9401);
        sum += (uint)digitCounter(sum + 100000000);
        sum += (uint)digitCounter(sum + 10341245214532535663);
        sum += (uint)digitCounter(sum + 132904351211);
        sum += (uint)digitCounter(sum + 5429138726719879);
        sum += (uint)digitCounter(sum + 103);
        sum += (uint)digitCounter(sum + 5429138726719812379);
        sum += (uint)digitCounter(sum + 0);
        sum += (uint)digitCounter(sum + ulong.MaxValue);

        return sum;
    }
}
