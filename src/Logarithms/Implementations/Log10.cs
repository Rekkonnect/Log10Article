using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Logarithms.Implementations;

public static class Log10
{
    // Non-negative ILog10
    // For all the ILog10 functions, we assume that the passed value is non-negative
    // and finite. Negative values, infinities and NaN are not handled and result in
    // undefined behavior

    // Behold an ugly but fast creation
    public static int NonNegativeILog(double value)
    {
        const double fastThreshold = 1e9;
        return value is 0 ? int.MinValue
            : value >= fastThreshold ? MathNonNegativeILog(value)
            : value < 1e1 ? 0
            : value < 1e2 ? 1
            : value < 1e3 ? 2
            : value < 1e4 ? 3
            : value < 1e5 ? 4
            : value < 1e6 ? 5
            : value < 1e7 ? 6
            : value < 1e8 ? 7
            : 8
            ;
    }

    [Obsolete(ReasonStrings.Obsoletion.CleanerButSlower)]
    public static int NonNegativeILogSwitch(double value)
    {
        const double fastThreshold = 1e9;
        return value switch
        {
            0 => int.MinValue,
            >= fastThreshold => MathNonNegativeILog(value),

            < 1e1 => 0,
            < 1e2 => 1,
            < 1e3 => 2,
            < 1e4 => 3,
            < 1e5 => 4,
            < 1e6 => 5,
            < 1e7 => 6,
            < 1e8 => 7,
            _ => 8
        };
    }

    public static int MathNonNegativeILog(double value)
    {
        return Math.Max(0, (int)Math.Log10(value));
    }

    public static int NonNegativeILog(float value)
    {
        const float fastThreshold = 1e9f;
        return value is 0 ? int.MinValue
            : value >= fastThreshold ? MathNonNegativeILog(value)
            : value < 1e1f ? 0
            : value < 1e2f ? 1
            : value < 1e3f ? 2
            : value < 1e4f ? 3
            : value < 1e5f ? 4
            : value < 1e6f ? 5
            : value < 1e7f ? 6
            : value < 1e8f ? 7
            : 8
            ;
    }

    [Obsolete(ReasonStrings.Obsoletion.CleanerButSlower)]
    public static int NonNegativeILogSwitch(float value)
    {
        const float fastThreshold = 1e9f;
        return value switch
        {
            0 => int.MinValue,
            >= fastThreshold => MathNonNegativeILog(value),

            < 1e1f => 0,
            < 1e2f => 1,
            < 1e3f => 2,
            < 1e4f => 3,
            < 1e5f => 4,
            < 1e6f => 5,
            < 1e7f => 6,
            < 1e8f => 7,
            _ => 8
        };
    }

    public static int MathNonNegativeILog(float value)
    {
        return Math.Max(0, (int)MathF.Log10(value));
    }

    // ILog10

    public static int ILog(double value)
    {
        const double fastThreshold = 1e9;
        return value is 0 ? int.MinValue
            : value >= fastThreshold ? MathILog(value)
            : value < 1e-8 ? MathILog(value)
            : value < 1e-7 ? -8
            : value < 1e-6 ? -7
            : value < 1e-5 ? -6
            : value < 1e-4 ? -5
            : value < 1e-3 ? -4
            : value < 1e-2 ? -3
            : value < 1e-1 ? -2
            : value < 1e0 ? -1
            : value < 1e1 ? 0
            : value < 1e2 ? 1
            : value < 1e3 ? 2
            : value < 1e4 ? 3
            : value < 1e5 ? 4
            : value < 1e6 ? 5
            : value < 1e7 ? 6
            : value < 1e8 ? 7
            : 8
            ;
    }

    public static int ILogSplit(double value)
    {
        const double fastThreshold = 1e9;
        return value is 0 ? int.MinValue
            : value < 1
                ? (value < 1e-8 ? MathILog(value)
                    : value < 1e-7 ? -8
                    : value < 1e-6 ? -7
                    : value < 1e-5 ? -6
                    : value < 1e-4 ? -5
                    : value < 1e-3 ? -4
                    : value < 1e-2 ? -3
                    : value < 1e-1 ? -2
                    : -1)

                : (value >= fastThreshold ? MathILog(value)
                    : value < 1e1 ? 0
                    : value < 1e2 ? 1
                    : value < 1e3 ? 2
                    : value < 1e4 ? 3
                    : value < 1e5 ? 4
                    : value < 1e6 ? 5
                    : value < 1e7 ? 6
                    : value < 1e8 ? 7
                    : 8)
            ;
    }

    public static int MathILog(double value)
    {
        return (int)Math.Log10(value);
    }

    public static int ILog(float value)
    {
        const float fastThreshold = 1e9f;
        return value is 0 ? int.MinValue
            : value >= fastThreshold ? MathILog(value)
            : value < 1e-8f ? MathILog(value)
            : value < 1e-7f ? -8
            : value < 1e-6f ? -7
            : value < 1e-5f ? -6
            : value < 1e-4f ? -5
            : value < 1e-3f ? -4
            : value < 1e-2f ? -3
            : value < 1e-1f ? -2
            : value < 1e0f ? -1
            : value < 1e1f ? 0
            : value < 1e2f ? 1
            : value < 1e3f ? 2
            : value < 1e4f ? 3
            : value < 1e5f ? 4
            : value < 1e6f ? 5
            : value < 1e7f ? 6
            : value < 1e8f ? 7
            : 8
            ;
    }

    public static int MathILog(float value)
    {
        return (int)MathF.Log10(value);
    }

    // Positive Digit count

    public static int DigitCount(byte b)
    {
        if (b >= 100)
            return 3;

        if (b >= 10)
            return 2;

        return 1;
    }

    public static int DigitCountMath(byte b)
    {
        if (b is 0)
        {
            return 1;
        }

        if (b is 1)
        {
            return 1;
        }

        return (int)Math.Ceiling(Math.Log10(b + 1));
    }

    [Obsolete(ReasonStrings.Obsoletion.Baseline)]
    public static int DigitCountToString(byte b)
    {
        return b.ToString().Length;
    }

    public static int DigitCount(ushort value)
    {
        if (value >= 10000)
            return 5;

        if (value >= 1000)
            return 4;

        if (value >= 100)
            return 3;

        if (value >= 10)
            return 2;

        return 1;
    }

    public static int DigitCountMath(ushort value)
    {
        if (value is 0)
        {
            return 1;
        }

        if (value is 1)
        {
            return 1;
        }

        return (int)Math.Ceiling(Math.Log10(value + 1));
    }

    [Obsolete(ReasonStrings.Obsoletion.Baseline)]
    public static int DigitCountToString(ushort value)
    {
        return value.ToString().Length;
    }

    public static int DigitCount(uint value)
    {
        if (value >= 1000000000)
            return 10;

        if (value >= 100000000)
            return 9;

        if (value >= 10000000)
            return 8;

        if (value >= 1000000)
            return 7;

        if (value >= 100000)
            return 6;

        if (value >= 10000)
            return 5;

        if (value >= 1000)
            return 4;

        if (value >= 100)
            return 3;

        if (value >= 10)
            return 2;

        return 1;
    }

    public static int DigitCountMath(uint value)
    {
        if (value is 0)
        {
            return 1;
        }

        if (value is 1)
        {
            return 1;
        }

        return (int)Math.Ceiling(Math.Log10((long)value + 1));
    }

    // Source: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/FormattingHelpers.CountDigits.cs#L65
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DigitCountStl(uint value)
    {
        // Algorithm based on https://lemire.me/blog/2021/06/03/computing-the-number-of-digits-of-an-integer-even-faster.
        ReadOnlySpan<long> table =
        [
            4294967296,
            8589934582,
            8589934582,
            8589934582,
            12884901788,
            12884901788,
            12884901788,
            17179868184,
            17179868184,
            17179868184,
            21474826480,
            21474826480,
            21474826480,
            21474826480,
            25769703776,
            25769703776,
            25769703776,
            30063771072,
            30063771072,
            30063771072,
            34349738368,
            34349738368,
            34349738368,
            34349738368,
            38554705664,
            38554705664,
            38554705664,
            41949672960,
            41949672960,
            41949672960,
            42949672960,
            42949672960,
        ];
        Debug.Assert(table.Length == 32, "Every result of uint.Log2(value) needs a long entry in the table.");

        // TODO: Replace with table[uint.Log2(value)] once https://github.com/dotnet/runtime/issues/79257 is fixed
        long tableValue = Unsafe.Add(ref MemoryMarshal.GetReference(table), uint.Log2(value));
        return (int)((value + tableValue) >> 32);
    }

    public static int DigitCountCompareAll(uint value)
    {
        uint mask = (UIntBool(value >= 0 && value < 10) << 1)
            | (UIntBool(value >= 10 && value < 100) << 2)
            | (UIntBool(value >= 100 && value < 1000) << 3)
            | (UIntBool(value >= 1000 && value < 10000) << 4)
            | (UIntBool(value >= 10000 && value < 100000) << 5)
            | (UIntBool(value >= 100000 && value < 1000000) << 6)
            | (UIntBool(value >= 1000000 && value < 10000000) << 7)
            | (UIntBool(value >= 10000000 && value < 100000000) << 8)
            | (UIntBool(value >= 100000000 && value < 1000000000) << 9)
            | (UIntBool(value >= 1000000000) << 10)
            ;
        return (int)uint.Log2(mask);
    }

    public static int DigitCountCompareAllBitwise(uint value)
    {
        uint mask = ((UIntBool(value >= 0) & UIntBool(value < 10)) << 1)
            | ((UIntBool(value >= 10) & UIntBool(value < 100)) << 2)
            | ((UIntBool(value >= 100) & UIntBool(value < 1000)) << 3)
            | ((UIntBool(value >= 1000) & UIntBool(value < 10000)) << 4)
            | ((UIntBool(value >= 10000) & UIntBool(value < 100000)) << 5)
            | ((UIntBool(value >= 100000) & UIntBool(value < 1000000)) << 6)
            | ((UIntBool(value >= 1000000) & UIntBool(value < 10000000)) << 7)
            | ((UIntBool(value >= 10000000) & UIntBool(value < 100000000)) << 8)
            | ((UIntBool(value >= 100000000) & UIntBool(value < 1000000000)) << 9)
            | (UIntBool(value >= 1000000000) << 10)
            ;
        return (int)uint.Log2(mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint UIntBool(bool x)
    {
        return x ? 1U : 0U;
    }

    public static int DigitCount(ulong value)
    {
        const ulong largeThreshold = 10_000_000_000;
        
        if (value >= largeThreshold)
        {
            ulong remaining = value / largeThreshold;
            return DigitCount(remaining) + 10;
        }

        if (value >= 1000000000)
            return 10;

        if (value >= 100000000)
            return 9;

        if (value >= 10000000)
            return 8;

        if (value >= 1000000)
            return 7;

        if (value >= 100000)
            return 6;

        if (value >= 10000)
            return 5;

        if (value >= 1000)
            return 4;

        if (value >= 100)
            return 3;

        if (value >= 10)
            return 2;

        return 1;
    }

    // Source: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/FormattingHelpers.CountDigits.cs#L15

    // Based on do_count_digits from https://github.com/fmtlib/fmt/blob/662adf4f33346ba9aba8b072194e319869ede54a/include/fmt/format.h#L1124
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DigitCountStl(ulong value)
    {
        // Map the log2(value) to a power of 10.
        ReadOnlySpan<byte> log2ToPow10 =
        [
            1,  1,  1,  2,  2,  2,  3,  3,  3,  4,  4,  4,  4,  5,  5,  5,
            6,  6,  6,  7,  7,  7,  7,  8,  8,  8,  9,  9,  9,  10, 10, 10,
            10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 15, 15,
            15, 16, 16, 16, 16, 17, 17, 17, 18, 18, 18, 19, 19, 19, 19, 20
        ];
        Debug.Assert(log2ToPow10.Length == 64);

        // TODO: Replace with log2ToPow10[BitOperations.Log2(value)] once https://github.com/dotnet/runtime/issues/79257 is fixed
        nint elementOffset = Unsafe.Add(ref MemoryMarshal.GetReference(log2ToPow10), BitOperations.Log2(value));

        // Read the associated power of 10.
        ReadOnlySpan<ulong> powersOf10 =
        [
            0, // unused entry to avoid needing to subtract
            0,
            10,
            100,
            1000,
            10000,
            100000,
            1000000,
            10000000,
            100000000,
            1000000000,
            10000000000,
            100000000000,
            1000000000000,
            10000000000000,
            100000000000000,
            1000000000000000,
            10000000000000000,
            100000000000000000,
            1000000000000000000,
            10000000000000000000,
        ];
        Debug.Assert((elementOffset + 1) <= powersOf10.Length);
        ulong powerOf10 = Unsafe.Add(ref MemoryMarshal.GetReference(powersOf10), elementOffset);

        // Return the number of digits based on the power of 10, shifted by 1
        // if it falls below the threshold.
        int index = (int)elementOffset;
        return index - (value < powerOf10 ? 1 : 0);
    }
}
