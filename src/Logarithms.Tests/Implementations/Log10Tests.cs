using Logarithms.Implementations;

namespace Logarithms.Tests.Implementations;

public class Log10Tests
{
    [Test]
    [Arguments(0.0001)]
    [Arguments(0.001)]
    [Arguments(0.01)]
    [Arguments(0.1)]
    [Arguments(1)]
    [Arguments(9)]
    [Arguments(9.999999)]
    [Arguments(10)]
    [Arguments(10.01)]
    [Arguments(19)]
    [Arguments(99)]
    [Arguments(99.999)]
    [Arguments(100)]
    [Arguments(999)]
    [Arguments(1000)]
    [Arguments(9999)]
    [Arguments(10000)]
    [Arguments(99999)]
    [Arguments(100000)]
    [Arguments(999999)]
    [Arguments(1000000)]
    [Arguments(9999999)]
    [Arguments(10000000)]
    [Arguments(99999999)]
    [Arguments(100000000)]
    [Arguments(999999999)]
    [Arguments(1000000000)]
    [Arguments(9999999999)]
    public async Task ILog(double value)
    {
        await AssertBaseline(Log10.NonNegativeILog, Log10.MathNonNegativeILog, value);
        await AssertBaseline(Log10.ILog, Log10.MathILog, value);
        await AssertBaseline(Log10.ILogSplit, Log10.MathILog, value);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(30)]
    [Arguments(98)]
    [Arguments(99)]
    [Arguments(100)]
    [Arguments(200)]
    [Arguments(255)]
    [Obsolete(ReasonStrings.Obsoletion.MarkedToAvoidWarnings)]
    public async Task DigitCount(byte value)
    {
        using var _ = Assert.Multiple();

        await AssertBaseline(Log10.DigitCount, Log10.DigitCountToString, value);
        await AssertBaseline(Log10.DigitCountMath, Log10.DigitCountToString, value);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(30)]
    [Arguments(98)]
    [Arguments(99)]
    [Arguments(100)]
    [Arguments(200)]
    [Arguments(255)]
    [Arguments(256)]
    [Arguments(999)]
    [Arguments(1000)]
    [Arguments(9999)]
    [Arguments(10000)]
    [Arguments(30000)]
    [Arguments(ushort.MaxValue)]
    [Obsolete(ReasonStrings.Obsoletion.MarkedToAvoidWarnings)]
    public async Task DigitCount(ushort value)
    {
        using var _ = Assert.Multiple();

        await AssertBaseline(Log10.DigitCount, Log10.DigitCountToString, value);
        await AssertBaseline(Log10.DigitCountMath, Log10.DigitCountToString, value);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(30)]
    [Arguments(98)]
    [Arguments(99)]
    [Arguments(100)]
    [Arguments(200)]
    [Arguments(255)]
    [Arguments(256)]
    [Arguments(999)]
    [Arguments(1000)]
    [Arguments(9999)]
    [Arguments(10000)]
    [Arguments(99999)]
    [Arguments(100000)]
    [Arguments(999999)]
    [Arguments(1000000)]
    [Arguments(9999999)]
    [Arguments(uint.MaxValue)]
    public async Task DigitCount(uint value)
    {
        using var _ = Assert.Multiple();

        await AssertBaseline(Log10.DigitCount, Log10.DigitCountMath, value);
        await AssertBaseline(Log10.DigitCountStl, Log10.DigitCountMath, value);
        await AssertBaseline(Log10.DigitCountCompareAll, Log10.DigitCountMath, value);
        await AssertBaseline(Log10.DigitCountCompareAllBitwise, Log10.DigitCountMath, value);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(99)]
    [Arguments(100)]
    [Arguments(999)]
    [Arguments(1000)]
    [Arguments(9999)]
    [Arguments(10000)]
    [Arguments(99999)]
    [Arguments(1000000)]
    [Arguments(9999999)]
    [Arguments(100000000)]
    [Arguments(999999999)]
    [Arguments(10000000000)]
    [Arguments(99999999999)]
    [Arguments(uint.MaxValue)]
    [Arguments(ulong.MaxValue)]
    public async Task DigitCount(ulong value)
    {
        using var _ = Assert.Multiple();

        await AssertBaseline(Log10.DigitCount, Log10.DigitCountStl, value);
    }

    private static async Task AssertBaseline<TSource, TResult>(
        Func<TSource, TResult> tested,
        Func<TSource, TResult> baseline,
        TSource source)
    {
        var testedResult = tested(source);
        var baselineResult = baseline(source);
        await Assert.That(testedResult).IsEqualTo(baselineResult);
    }
}
