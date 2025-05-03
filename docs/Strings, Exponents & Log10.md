By Alex "Rekkon", at 2025/05/03

## Disclaimer

This article was written in an attempt to raise awareness and explore the world of calculations related to processing numbers in base 10. It is not any piece of formal or meticulous research, and each point must be taken with a grain of salt as with everything anywhere.

All the code examples below are in C#, and is run in .NET. Despite showing opinionated examples, the key takeaway is not bound to a specific language, framework, runtime and their corresponding tricks for juicing out faster JIT assemblies. I only chose this set of tools for familiarity reasons. All numeric functions that were written in this article should be trivially translatable to other languages, including C, C++, Java, Python, Rust, Go, Scala, Kotlin, Brainfuck, Moo, Shakespeare, you name it.

The associated benchmarks were run with Benchmark.NET, which is pretty mature and insightful, suitable for both macro- and microbenchmarks. Be sure to check it out if you're interested in how it executes benchmarks and what features it offers.

The configuration for all the benchmarks is:
```
BenchmarkDotNet v0.14.0, Windows 10 (10.0.19045.5737/22H2/2022Update)
AMD Ryzen 9 5950X, 1 CPU, 32 logical and 16 physical cores
.NET SDK 9.0.300-preview.0.25177.5
  [Host]     : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2 [AttachedDebugger]
  Job-HPLCQC : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
```
The iteration time is usually set to 150ms to shorten the time the benchmarks take to execute (B.NET defaults to 500ms). If the benchmark code is present, it may include the iteration time that the benchmark is set to, in milliseconds.

This article contains extensive tables with benchmark results. You may skip them at your will, or you may cross-check their validity by running the benchmarks in the repository locally.

## Debrief

A log-standing problem in computer science is calculating the number of digits a number has. In undergrads, you will usually find solutions like "keep dividing until the result is 0" or "if the number is > 10^k, the count is k + 1". Unfortunately, even [bad solutions](https://www.geeksforgeeks.org/program-count-digits-integer-3-different-methods/)  are being blatantly posted without any regards to performance or best practices for the specific ecosystem (language, STL, etc.). Most experienced devs settle with a logarithm of base 10 to deal with the problem without worrying about re-inventing the wheel, so it's important that you can rely on its accuracy for all inputs and its speed. Hardware implementations that are used in the process of calculating a base-10 logarithm include instructions for calculating intermediate results to the finest precision, as IEEE-754 demands.

However there is also the integral log10, which returns just the integer part of the logarithm's result. When creating a string representation of a number, that's all we care about. All the fractional parts of the logarithm's result are useless and unnecessary. We need specific specialized instructions for calculating the integral log10 of a number, whether it be a 32- or 64-bit floating-point, or an integer of 8, 16, 32 or 64 bits.

Following https://en.wikipedia.org/wiki/X86_instruction_listings, we can find the following instructions available in x87: `FYL2X` and `FYL2XP1`. The only problem is that they are described as base-2 logarithms. Obviously, they can be used when calculating any logarithm in any base, applying the required transformations between each logarithmic base. But it's all software-driven, including log10. We lack actually designed instructions for the most basic task an application does multiple times throughout its lifetime; converting a number to a base-10 string representation.

> NOTE: ARM does not even have any logarithmic functions. But this is something that won't be touched on because it's a more specialized instruction set meant to be used in lower-end machines, where the cost of implementing any more logic into the circuit would cause unnecessary penalties in the rest of the system, and thus a software implementation of any transcendental function is more appropriate.

I'm not much of a hardware guy, my whole life has been software pretty much. But I do know that logarithms are expensive. And there are two problems with using logarithms for base-10 digit count:
- Logarithms are not even necessary. We throw away the entire carefully-calculated fractional part of the result. Any cheaper implementation of the logarithm would still over-suffice.
- Hardware instructions for log10 itself do not exist in x86 or ARM, the most commonly-used instruction sets in most popular commercially-available devices (FPGAs and supercomputer-specialized components are not generally available and this article won't explore that area).
So to solve this, we need hardware instructions that make base-10 digit count easier to calculate, preferably just one hardware instruction for each data type (32/64-bit floats, 8/16/32/64-bit unsigned integers).

But this solution doesn't make me sleep at nights. I'm a software developer, I don't have the hardware implementation, might as well roll my own software implementation until that day. And so I did.

## Software Rescue

We need to develop a base-10 digit count function for all of the 6 data types described above: 32/64-bit floats, 8/16/32/64-bit unsigned integers. We will start with the integers since they are the most basic ones. First let's analyze the limits of the data types:
- 8-bit ranges in `[0, 255]`, so it's got 1-3 decimal digits.
- 16-bit ranges in `[0, 65,535]`, so 1-5 digits.
- 32-bit ranges in `[0, 4,294,967,296]`, making it 1-10 digits.
- 64-bit ranges in `[0, 18,446,744,073,709,551,615]`, with 1-20 digits.

From the above we can tell that the 8/16-bit implementations have a tiny result set, and the 32/64-bit are still modest with a few extra values. We can leverage a trivial cheap solution for 8-bit as a PoC:
```csharp
public static int DigitCount(byte b)
{
    if (b >= 100)
        return 3;
        
    if (b >= 10)
        return 2;
        
    return 1;
}
```

This is a pretty self-explanatory solution. The only problem we have here are the potential branch mispredictions, which are very expensive and would completely defeat the purpose of all the optimizations we tried to implement.

For a first, we'll compare this implementation to others. Take for example two other common solutions to this problem:
```csharp
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

public static int DigitCountToString(byte b)
{
	return b.ToString().Length;
}
```

In `DigitCountToString`, by calling `ToString` we are defeating the purpose of implementing a digit count for the purposes of a faster string conversion. Obviously this is only for baseline purposes.

It would make sense if we assumed the solution using `ToString` would be the slowest; allocating a new string, iterating the number's digits, much more involved than a "simple" ceiling and log over a byte converted to a double.

"What's a string allocation?", said the .NET runtime:

| Method             | Value | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD |
|------------------- |------ |----------:|----------:|----------:|----------:|------:|--------:|
| DigitCount         | 0     | 0.0069 ns | 0.0079 ns | 0.0074 ns | 0.0039 ns |  0.03 |    0.03 |
| DigitCountMath     | 0     | 0.2402 ns | 0.0236 ns | 0.0221 ns | 0.2338 ns |  1.01 |    0.12 |
| DigitCountToString | 0     | 0.8803 ns | 0.0138 ns | 0.0129 ns | 0.8864 ns |  3.69 |    0.31 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 1     | 0.0076 ns | 0.0064 ns | 0.0060 ns | 0.0068 ns |  0.02 |    0.01 |
| DigitCountMath     | 1     | 0.4429 ns | 0.0080 ns | 0.0075 ns | 0.4433 ns |  1.00 |    0.02 |
| DigitCountToString | 1     | 0.8757 ns | 0.0095 ns | 0.0080 ns | 0.8779 ns |  1.98 |    0.04 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 8     | 0.0290 ns | 0.0222 ns | 0.0237 ns | 0.0238 ns | 0.006 |    0.00 |
| DigitCountMath     | 8     | 5.1601 ns | 0.0534 ns | 0.0473 ns | 5.1613 ns | 1.000 |    0.01 |
| DigitCountToString | 8     | 0.8776 ns | 0.0121 ns | 0.0113 ns | 0.8801 ns | 0.170 |    0.00 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 9     | 0.0090 ns | 0.0078 ns | 0.0069 ns | 0.0101 ns | 0.002 |    0.00 |
| DigitCountMath     | 9     | 5.3632 ns | 0.1129 ns | 0.1208 ns | 5.3346 ns | 1.000 |    0.03 |
| DigitCountToString | 9     | 0.8828 ns | 0.0114 ns | 0.0095 ns | 0.8836 ns | 0.165 |    0.00 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 10    | 0.2239 ns | 0.0099 ns | 0.0092 ns | 0.2238 ns |  0.04 |    0.00 |
| DigitCountMath     | 10    | 5.6829 ns | 0.0244 ns | 0.0229 ns | 5.6860 ns |  1.00 |    0.01 |
| DigitCountToString | 10    | 0.8776 ns | 0.0082 ns | 0.0077 ns | 0.8809 ns |  0.15 |    0.00 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 30    | 0.2249 ns | 0.0076 ns | 0.0068 ns | 0.2237 ns |  0.04 |    0.00 |
| DigitCountMath     | 30    | 5.5337 ns | 0.1205 ns | 0.1006 ns | 5.5228 ns |  1.00 |    0.02 |
| DigitCountToString | 30    | 0.8700 ns | 0.0093 ns | 0.0087 ns | 0.8729 ns |  0.16 |    0.00 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 98    | 0.2213 ns | 0.0107 ns | 0.0089 ns | 0.2241 ns |  0.04 |    0.00 |
| DigitCountMath     | 98    | 5.2897 ns | 0.0458 ns | 0.0406 ns | 5.2910 ns |  1.00 |    0.01 |
| DigitCountToString | 98    | 0.8801 ns | 0.0147 ns | 0.0138 ns | 0.8825 ns |  0.17 |    0.00 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 99    | 0.2222 ns | 0.0072 ns | 0.0063 ns | 0.2230 ns |  0.04 |    0.00 |
| DigitCountMath     | 99    | 5.5021 ns | 0.0402 ns | 0.0336 ns | 5.4986 ns |  1.00 |    0.01 |
| DigitCountToString | 99    | 0.8820 ns | 0.0092 ns | 0.0081 ns | 0.8832 ns |  0.16 |    0.00 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 100   | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.000 |    0.00 |
| DigitCountMath     | 100   | 5.5914 ns | 0.0484 ns | 0.0453 ns | 5.5903 ns | 1.000 |    0.01 |
| DigitCountToString | 100   | 0.9026 ns | 0.0242 ns | 0.0226 ns | 0.9103 ns | 0.161 |    0.00 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 200   | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.000 |    0.00 |
| DigitCountMath     | 200   | 5.4576 ns | 0.0315 ns | 0.0279 ns | 5.4543 ns | 1.000 |    0.01 |
| DigitCountToString | 200   | 0.8718 ns | 0.0132 ns | 0.0103 ns | 0.8713 ns | 0.160 |    0.00 |
|                    |       |           |           |           |           |       |         |
| DigitCount         | 255   | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.000 |    0.00 |
| DigitCountMath     | 255   | 5.6109 ns | 0.0504 ns | 0.0471 ns | 5.6214 ns | 1.000 |    0.01 |
| DigitCountToString | 255   | 0.8777 ns | 0.0117 ns | 0.0098 ns | 0.8802 ns | 0.156 |    0.00 |

Our `DigitCount` is almost indistinguishable from a method invocation (literally what Benchmark.NET says):
> `ByteDigitCountBenchmarks.DigitCount: IterationTime=150ms -> The method duration is indistinguishable from the empty method duration`

But most importantly, why is `ToString` significantly faster than the math solution for the cases other than 0 and 1? Nobody would have seen that ceiling + log is so terrible, right?

One important note is that the .NET runtime has incorporated tons of optimizations having undergone strenuous benchmarking effort throughout the over two decades that it has been alive. So usually it should not come as a surprise that common methods like `ToString` would be blazingly fast and any homebrew solution would appear terrible compared against it.

When going to the `ToString` definition for `byte`, we see this:
```csharp
public override string ToString()
{
	return Number.UInt32ToDecStr(m_value);
}
```

Browsing the code for the .NET Runtime [on GitHub](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Number.Formatting.cs#L645) we find that this actually makes use of cached strings for small numbers. For non-Mono platforms, which most are nowadays, this includes cached strings for values \[0, 300), therefore clearing out the entire byte range. This is also great because we avoid unnecessary string allocations, at the cost of only `10 * 1 + 90 * 2 + 200 * 3` = 10 + 180 + 600 = 790 characters across 300 strings, summing to 1580 bytes for the characters themselves and 300 pointers to the instances, meaning 2400 bytes for pointers alone. Without diving too deep into specifics and nerd analysis, let's assume that a total of 10~15 KiB is used for small number cache. It's important to note that those small strings are lazily created on-demand, meaning the theoretical memory allocated for the cached strings is usually much less than 10~15 KiB; even more negligible especially for the performance value it provides.

With that out of the way, it's now time to test our technique for 16-bit integers. We use the same code as above, for the `ushort` type, except we add two more cases for our custom `DigitCount`:
```csharp
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
```

| Method             | Value | Mean      | Error     | StdDev    | Ratio | RatioSD |
|------------------- |------ |----------:|----------:|----------:|------:|--------:|
| DigitCount         | 0     | 0.4121 ns | 0.0087 ns | 0.0081 ns |  1.82 |    0.08 |
| DigitCountMath     | 0     | 0.2270 ns | 0.0101 ns | 0.0095 ns |  1.00 |    0.06 |
| DigitCountToString | 0     | 0.8779 ns | 0.0118 ns | 0.0110 ns |  3.87 |    0.16 |
|                    |       |           |           |           |       |         |
| DigitCount         | 1     | 0.4147 ns | 0.0094 ns | 0.0088 ns |  0.94 |    0.03 |
| DigitCountMath     | 1     | 0.4412 ns | 0.0094 ns | 0.0088 ns |  1.00 |    0.03 |
| DigitCountToString | 1     | 0.8773 ns | 0.0387 ns | 0.0362 ns |  1.99 |    0.09 |
|                    |       |           |           |           |       |         |
| DigitCount         | 8     | 0.4161 ns | 0.0079 ns | 0.0070 ns |  0.07 |    0.00 |
| DigitCountMath     | 8     | 5.6042 ns | 0.0519 ns | 0.0460 ns |  1.00 |    0.01 |
| DigitCountToString | 8     | 0.8831 ns | 0.0097 ns | 0.0086 ns |  0.16 |    0.00 |
|                    |       |           |           |           |       |         |
| DigitCount         | 9     | 0.4241 ns | 0.0062 ns | 0.0055 ns |     ? |       ? |
| DigitCountMath     | 9     | 0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |
| DigitCountToString | 9     | 0.8785 ns | 0.0117 ns | 0.0104 ns |     ? |       ? |
|                    |       |           |           |           |       |         |
| DigitCount         | 10    | 0.4200 ns | 0.0033 ns | 0.0031 ns |  0.08 |    0.00 |
| DigitCountMath     | 10    | 5.5055 ns | 0.0498 ns | 0.0466 ns |  1.00 |    0.01 |
| DigitCountToString | 10    | 0.9109 ns | 0.0361 ns | 0.0338 ns |  0.17 |    0.01 |
|                    |       |           |           |           |       |         |
| DigitCount         | 30    | 0.3966 ns | 0.0055 ns | 0.0051 ns |  0.07 |    0.00 |
| DigitCountMath     | 30    | 5.5375 ns | 0.0369 ns | 0.0308 ns |  1.00 |    0.01 |
| DigitCountToString | 30    | 0.8790 ns | 0.0102 ns | 0.0095 ns |  0.16 |    0.00 |
|                    |       |           |           |           |       |         |
| DigitCount         | 98    | 0.4175 ns | 0.0082 ns | 0.0073 ns |  0.07 |    0.00 |
| DigitCountMath     | 98    | 5.6315 ns | 0.0455 ns | 0.0425 ns |  1.00 |    0.01 |
| DigitCountToString | 98    | 0.8771 ns | 0.0124 ns | 0.0116 ns |  0.16 |    0.00 |
|                    |       |           |           |           |       |         |
| DigitCount         | 99    | 0.4197 ns | 0.0070 ns | 0.0062 ns |  0.08 |    0.00 |
| DigitCountMath     | 99    | 5.5897 ns | 0.0217 ns | 0.0203 ns |  1.00 |    0.00 |
| DigitCountToString | 99    | 0.8831 ns | 0.0045 ns | 0.0040 ns |  0.16 |    0.00 |
|                    |       |           |           |           |       |         |
| DigitCount         | 100   | 0.2383 ns | 0.0193 ns | 0.0181 ns |  0.04 |    0.00 |
| DigitCountMath     | 100   | 5.6491 ns | 0.0440 ns | 0.0390 ns |  1.00 |    0.01 |
| DigitCountToString | 100   | 0.8790 ns | 0.0192 ns | 0.0179 ns |  0.16 |    0.00 |
|                    |       |           |           |           |       |         |
| DigitCount         | 200   | 0.2197 ns | 0.0133 ns | 0.0111 ns |  0.04 |    0.00 |
| DigitCountMath     | 200   | 5.5887 ns | 0.0538 ns | 0.0503 ns |  1.00 |    0.01 |
| DigitCountToString | 200   | 0.8766 ns | 0.0116 ns | 0.0109 ns |  0.16 |    0.00 |
|                    |       |           |           |           |       |         |
| DigitCount         | 255   | 0.2198 ns | 0.0087 ns | 0.0081 ns |  0.04 |    0.00 |
| DigitCountMath     | 255   | 5.5531 ns | 0.0491 ns | 0.0459 ns |  1.00 |    0.01 |
| DigitCountToString | 255   | 0.8736 ns | 0.0095 ns | 0.0089 ns |  0.16 |    0.00 |
|                    |       |           |           |           |       |         |
| DigitCount         | 999   | 0.2173 ns | 0.0138 ns | 0.0122 ns |  0.04 |    0.00 |
| DigitCountMath     | 999   | 5.5038 ns | 0.1236 ns | 0.1032 ns |  1.00 |    0.03 |
| DigitCountToString | 999   | 4.7956 ns | 0.1051 ns | 0.1291 ns |  0.87 |    0.03 |
|                    |       |           |           |           |       |         |
| DigitCount         | 1000  | 0.1921 ns | 0.0119 ns | 0.0105 ns |  0.03 |    0.00 |
| DigitCountMath     | 1000  | 5.7471 ns | 0.0312 ns | 0.0292 ns |  1.00 |    0.01 |
| DigitCountToString | 1000  | 4.6288 ns | 0.1071 ns | 0.1001 ns |  0.81 |    0.02 |
|                    |       |           |           |           |       |         |
| DigitCount         | 2000  | 0.2110 ns | 0.0091 ns | 0.0085 ns |  0.04 |    0.00 |
| DigitCountMath     | 2000  | 5.3261 ns | 0.0498 ns | 0.0389 ns |  1.00 |    0.01 |
| DigitCountToString | 2000  | 4.7141 ns | 0.1167 ns | 0.1248 ns |  0.89 |    0.02 |
|                    |       |           |           |           |       |         |
| DigitCount         | 9999  | 0.0414 ns | 0.0052 ns | 0.0044 ns | 0.008 |    0.00 |
| DigitCountMath     | 9999  | 5.1945 ns | 0.0439 ns | 0.0389 ns | 1.000 |    0.01 |
| DigitCountToString | 9999  | 4.6821 ns | 0.1144 ns | 0.1566 ns | 0.901 |    0.03 |
|                    |       |           |           |           |       |         |
| DigitCount         | 10000 | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.000 |    0.00 |
| DigitCountMath     | 10000 | 5.3869 ns | 0.0243 ns | 0.0216 ns | 1.000 |    0.01 |
| DigitCountToString | 10000 | 5.3157 ns | 0.1282 ns | 0.1424 ns | 0.987 |    0.03 |
|                    |       |           |           |           |       |         |
| DigitCount         | 20000 | 0.2045 ns | 0.0072 ns | 0.0060 ns |  0.04 |    0.00 |
| DigitCountMath     | 20000 | 5.5767 ns | 0.0379 ns | 0.0355 ns |  1.00 |    0.01 |
| DigitCountToString | 20000 | 5.0338 ns | 0.1284 ns | 0.1529 ns |  0.90 |    0.03 |
|                    |       |           |           |           |       |         |
| DigitCount         | 65535 | 0.2070 ns | 0.0076 ns | 0.0060 ns |  0.04 |    0.00 |
| DigitCountMath     | 65535 | 5.6082 ns | 0.0382 ns | 0.0339 ns |  1.00 |    0.01 |
| DigitCountToString | 65535 | 5.2610 ns | 0.1249 ns | 0.1169 ns |  0.94 |    0.02 |

> For some reason the execution of `DigitCountMath` with `Value = 9` failed miserably. Here are the logs
```
> Setup power plan (GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c FriendlyName: High performance)
// **************************
// Benchmark: UInt16DigitCountBenchmarks.DigitCountMath: Job-EPJKKB(IterationTime=150ms) [Value=9]
// *** Execute ***
// Launch: 1 / 1
// Execute: dotnet eebeaa47-e8aa-4380-8a7f-77e28007bc69.dll --anonymousPipes 1784 1780 --benchmarkName "Logarithms.Benchmarks.UInt16DigitCountBenchmarks.DigitCountMath(Value: 9)" --job IterationTime=150ms --benchmarkId 10 in E:\repos\benchmarks\Logarithms\Logarithms\Logarithms.Benchmarks\bin\Release\net9.0\eebeaa47-e8aa-4380-8a7f-77e28007bc69\bin\Release\net9.0
// BeforeAnythingElse

// Benchmark Process Environment Information:
// BenchmarkDotNet v0.14.0
// Runtime=.NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
// GC=Concurrent Workstation
// HardwareIntrinsics=AVX2,AES,BMI1,BMI2,FMA,LZCNT,PCLMUL,POPCNT VectorSize=256
// Job: Job-THXRDK(IterationTime=150ms)

OverheadJitting  1: 1 op, 150400.00 ns, 150.4000 us/op
WorkloadJitting  1: 1 op, 327300.00 ns, 327.3000 us/op

OverheadJitting  2: 16 op, 301600.00 ns, 18.8500 us/op
WorkloadJitting  2: 16 op, 308500.00 ns, 19.2813 us/op

WorkloadPilot    1: 16 op, 600.00 ns, 37.5000 ns/op
WorkloadPilot    2: 4000000 op, 44868700.00 ns, 11.2172 ns/op
WorkloadPilot    3: 13372352 op, 153876900.00 ns, 11.5071 ns/op
WorkloadPilot    4: 13035440 op, 113701000.00 ns, 8.7225 ns/op
WorkloadPilot    5: 17197008 op, 116568200.00 ns, 6.7784 ns/op
WorkloadPilot    6: 22129120 op, 149863500.00 ns, 6.7722 ns/op
WorkloadPilot    7: 22149280 op, 151336800.00 ns, 6.8326 ns/op
WorkloadPilot    8: 21953632 op, 147605300.00 ns, 6.7235 ns/op
WorkloadPilot    9: 22309808 op, 167585500.00 ns, 7.5117 ns/op

OverheadWarmup   1: 22309808 op, 247889600.00 ns, 11.1112 ns/op
OverheadWarmup   2: 22309808 op, 244464100.00 ns, 10.9577 ns/op
OverheadWarmup   3: 22309808 op, 241717700.00 ns, 10.8346 ns/op
OverheadWarmup   4: 22309808 op, 245131900.00 ns, 10.9876 ns/op
OverheadWarmup   5: 22309808 op, 242889200.00 ns, 10.8871 ns/op
OverheadWarmup   6: 22309808 op, 244921400.00 ns, 10.9782 ns/op
OverheadWarmup   7: 22309808 op, 243247800.00 ns, 10.9032 ns/op

OverheadActual   1: 22309808 op, 244687600.00 ns, 10.9677 ns/op
OverheadActual   2: 22309808 op, 241937500.00 ns, 10.8444 ns/op
OverheadActual   3: 22309808 op, 245756500.00 ns, 11.0156 ns/op
OverheadActual   4: 22309808 op, 242984100.00 ns, 10.8914 ns/op
OverheadActual   5: 22309808 op, 245127700.00 ns, 10.9874 ns/op
OverheadActual   6: 22309808 op, 242214700.00 ns, 10.8569 ns/op
OverheadActual   7: 22309808 op, 245192900.00 ns, 10.9904 ns/op
OverheadActual   8: 22309808 op, 241787800.00 ns, 10.8377 ns/op
OverheadActual   9: 22309808 op, 244824400.00 ns, 10.9738 ns/op
OverheadActual  10: 22309808 op, 241873200.00 ns, 10.8416 ns/op
OverheadActual  11: 22309808 op, 245890700.00 ns, 11.0216 ns/op
OverheadActual  12: 22309808 op, 243904300.00 ns, 10.9326 ns/op
OverheadActual  13: 22309808 op, 245222800.00 ns, 10.9917 ns/op
OverheadActual  14: 22309808 op, 241990000.00 ns, 10.8468 ns/op
OverheadActual  15: 22309808 op, 243230200.00 ns, 10.9024 ns/op

WorkloadWarmup   1: 22309808 op, 151244100.00 ns, 6.7793 ns/op
WorkloadWarmup   2: 22309808 op, 150595700.00 ns, 6.7502 ns/op
WorkloadWarmup   3: 22309808 op, 151240400.00 ns, 6.7791 ns/op
WorkloadWarmup   4: 22309808 op, 150680400.00 ns, 6.7540 ns/op
WorkloadWarmup   5: 22309808 op, 151096100.00 ns, 6.7726 ns/op
WorkloadWarmup   6: 22309808 op, 150765700.00 ns, 6.7578 ns/op

// BeforeActualRun
WorkloadActual   1: 22309808 op, 156780800.00 ns, 7.0274 ns/op
WorkloadActual   2: 22309808 op, 155633100.00 ns, 6.9760 ns/op
WorkloadActual   3: 22309808 op, 155573600.00 ns, 6.9733 ns/op
WorkloadActual   4: 22309808 op, 155295400.00 ns, 6.9609 ns/op
WorkloadActual   5: 22309808 op, 156055200.00 ns, 6.9949 ns/op
WorkloadActual   6: 22309808 op, 156223600.00 ns, 7.0025 ns/op
WorkloadActual   7: 22309808 op, 157261500.00 ns, 7.0490 ns/op
WorkloadActual   8: 22309808 op, 156897100.00 ns, 7.0327 ns/op
WorkloadActual   9: 22309808 op, 156150600.00 ns, 6.9992 ns/op
WorkloadActual  10: 22309808 op, 157407500.00 ns, 7.0555 ns/op
WorkloadActual  11: 22309808 op, 156627000.00 ns, 7.0205 ns/op
WorkloadActual  12: 22309808 op, 154433400.00 ns, 6.9222 ns/op
WorkloadActual  13: 22309808 op, 156903600.00 ns, 7.0329 ns/op
WorkloadActual  14: 22309808 op, 156460100.00 ns, 7.0131 ns/op
WorkloadActual  15: 22309808 op, 156496300.00 ns, 7.0147 ns/op

// AfterActualRun
WorkloadResult   1: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult   2: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult   3: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult   4: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult   5: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult   6: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult   7: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult   8: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult   9: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult  10: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult  11: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult  12: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult  13: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult  14: 22309808 op, 0.00 ns, 0.0000 ns/op
WorkloadResult  15: 22309808 op, 0.00 ns, 0.0000 ns/op

// AfterAll
// Benchmark Process 122904 has exited with code 0.

Mean = 0.000 ns, StdErr = 0.000 ns (NaN%), N = 15, StdDev = 0.000 ns
Min = 0.000 ns, Q1 = 0.000 ns, Median = 0.000 ns, Q3 = 0.000 ns, Max = 0.000 ns
IQR = 0.000 ns, LowerFence = 0.000 ns, UpperFence = 0.000 ns
ConfidenceInterval = [0.000 ns; 0.000 ns] (CI 99.9%), Margin = 0.000 ns (NaN% of Mean)
Skewness = NaN, Kurtosis = NaN, MValue = 2
```

For the above results, at Value = 999 onwards we're starting to see `ToString` take a huge perf penalty, which aligns with the use of a small number string cache. Still it's faster to construct a garbage-collectable string than to use ceil + log up until the max value of u16. In the best case, iterative divisions are faster, in the worst case there's also the overhead of a digit count calculation to pre-compute the string length. Indeed, this is the implementation we're interested in: [here](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Number.Formatting.cs#L1673):
```csharp
internal static string UInt32ToDecStr(uint value)
{
	// For small numbers, consult a lazily-populated cache.
	if (value < SmallNumberCacheLength)
	{
		return UInt32ToDecStrForKnownSmallNumber(value);
	}

	return UInt32ToDecStr_NoSmallNumberCheck(value);
}

internal static string UInt32ToDecStrForKnownSmallNumber(uint value)
{
	// omitted for brevity
}

private static unsafe string UInt32ToDecStr_NoSmallNumberCheck(uint value)
{
	int bufferLength = FormattingHelpers.CountDigits(value);

	string result = string.FastAllocateString(bufferLength);
	fixed (char* buffer = result)
	{
		char* p = buffer + bufferLength;
		p = UInt32ToDecChars(p, value);
		Debug.Assert(p == buffer);
	}
	return result;
}
```

The implementation does pre-calculate the digit count, so it's definitely fast. Let's see what they've done (source [here](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/FormattingHelpers.CountDigits.cs#L65)):

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int CountDigits(uint value)
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
		// ... omitted values
	];
	Debug.Assert(table.Length == 32, "Every result of uint.Log2(value) needs a long entry in the table.");

	// TODO: Replace with table[uint.Log2(value)] once https://github.com/dotnet/runtime/issues/79257 is fixed
	long tableValue = Unsafe.Add(ref MemoryMarshal.GetReference(table), uint.Log2(value));
	return (int)((value + tableValue) >> 32);
}
```

Now we have a good idea as to how the digit count is calculated. But I fear that using log 2 is still not the best solution, so it's best to benchmark this. Unfortunately the `CountDigit` method is inside an internal class named `FormattingHelpers`. We will have to copy this implementation in our own code to properly test it.

To compare apples to apples, we will now move towards the 32-bit integers.

| Method         | Value      |       Mean |     Error |    StdDev |     Median | Ratio | RatioSD |
| -------------- | ---------- | ---------: | --------: | --------: | ---------: | ----: | ------: |
| DigitCount     | 0          |  0.7965 ns | 0.0079 ns | 0.0073 ns |  0.7955 ns |  3.83 |    0.12 |
| DigitCountMath | 0          |  0.2079 ns | 0.0069 ns | 0.0065 ns |  0.2081 ns |  1.00 |    0.04 |
| DigitCountStl  | 0          |  0.0136 ns | 0.0120 ns | 0.0113 ns |  0.0140 ns |  0.07 |    0.05 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 1          |  0.7930 ns | 0.0131 ns | 0.0123 ns |  0.7928 ns |  1.90 |    0.06 |
| DigitCountMath | 1          |  0.4187 ns | 0.0128 ns | 0.0114 ns |  0.4147 ns |  1.00 |    0.04 |
| DigitCountStl  | 1          |  0.0146 ns | 0.0077 ns | 0.0068 ns |  0.0151 ns |  0.03 |    0.02 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 8          |  0.7940 ns | 0.0071 ns | 0.0063 ns |  0.7923 ns | 0.141 |    0.00 |
| DigitCountMath | 8          |  5.6485 ns | 0.0618 ns | 0.0547 ns |  5.6316 ns | 1.000 |    0.01 |
| DigitCountStl  | 8          |  0.0007 ns | 0.0012 ns | 0.0011 ns |  0.0001 ns | 0.000 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 9          |  0.8034 ns | 0.0210 ns | 0.0186 ns |  0.7979 ns | 0.144 |    0.00 |
| DigitCountMath | 9          |  5.5621 ns | 0.0359 ns | 0.0335 ns |  5.5739 ns | 1.000 |    0.01 |
| DigitCountStl  | 9          |  0.0006 ns | 0.0024 ns | 0.0020 ns |  0.0000 ns | 0.000 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 10         |  0.7819 ns | 0.0118 ns | 0.0111 ns |  0.7844 ns | 0.141 |    0.00 |
| DigitCountMath | 10         |  5.5448 ns | 0.0352 ns | 0.0312 ns |  5.5543 ns | 1.000 |    0.01 |
| DigitCountStl  | 10         |  0.0120 ns | 0.0095 ns | 0.0088 ns |  0.0110 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 30         |  0.7967 ns | 0.0182 ns | 0.0170 ns |  0.7958 ns | 0.148 |    0.00 |
| DigitCountMath | 30         |  5.3754 ns | 0.0351 ns | 0.0293 ns |  5.3800 ns | 1.000 |    0.01 |
| DigitCountStl  | 30         |  0.0126 ns | 0.0090 ns | 0.0084 ns |  0.0105 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 98         |  0.7861 ns | 0.0082 ns | 0.0064 ns |  0.7859 ns | 0.140 |    0.00 |
| DigitCountMath | 98         |  5.5972 ns | 0.0649 ns | 0.0576 ns |  5.5882 ns | 1.000 |    0.01 |
| DigitCountStl  | 98         |  0.0181 ns | 0.0096 ns | 0.0085 ns |  0.0177 ns | 0.003 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 99         |  0.7907 ns | 0.0055 ns | 0.0049 ns |  0.7899 ns | 0.152 |    0.00 |
| DigitCountMath | 99         |  5.1861 ns | 0.0468 ns | 0.0438 ns |  5.1938 ns | 1.000 |    0.01 |
| DigitCountStl  | 99         |  0.0098 ns | 0.0077 ns | 0.0068 ns |  0.0103 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 100        |  0.7981 ns | 0.0068 ns | 0.0063 ns |  0.7976 ns | 0.142 |    0.00 |
| DigitCountMath | 100        |  5.6037 ns | 0.0342 ns | 0.0320 ns |  5.6025 ns | 1.000 |    0.01 |
| DigitCountStl  | 100        |  0.0131 ns | 0.0086 ns | 0.0080 ns |  0.0126 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 200        |  0.7912 ns | 0.0115 ns | 0.0096 ns |  0.7909 ns | 0.140 |    0.00 |
| DigitCountMath | 200        |  5.6367 ns | 0.0520 ns | 0.0486 ns |  5.6382 ns | 1.000 |    0.01 |
| DigitCountStl  | 200        |  0.0214 ns | 0.0214 ns | 0.0190 ns |  0.0151 ns | 0.004 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 255        |  0.7959 ns | 0.0124 ns | 0.0116 ns |  0.7961 ns | 0.143 |    0.00 |
| DigitCountMath | 255        |  5.5602 ns | 0.0429 ns | 0.0401 ns |  5.5544 ns | 1.000 |    0.01 |
| DigitCountStl  | 255        |  0.0090 ns | 0.0056 ns | 0.0050 ns |  0.0091 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 999        |  0.7888 ns | 0.0105 ns | 0.0098 ns |  0.7921 ns | 0.145 |    0.00 |
| DigitCountMath | 999        |  5.4449 ns | 0.0527 ns | 0.0493 ns |  5.4495 ns | 1.000 |    0.01 |
| DigitCountStl  | 999        |  0.0062 ns | 0.0072 ns | 0.0068 ns |  0.0032 ns | 0.001 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 1000       |  0.3910 ns | 0.0082 ns | 0.0077 ns |  0.3918 ns | 0.072 |    0.00 |
| DigitCountMath | 1000       |  5.4603 ns | 0.0427 ns | 0.0400 ns |  5.4540 ns | 1.000 |    0.01 |
| DigitCountStl  | 1000       |  0.0159 ns | 0.0114 ns | 0.0101 ns |  0.0150 ns | 0.003 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 2000       |  0.5778 ns | 0.0040 ns | 0.0037 ns |  0.5786 ns | 0.112 |    0.00 |
| DigitCountMath | 2000       |  5.1629 ns | 0.0448 ns | 0.0397 ns |  5.1648 ns | 1.000 |    0.01 |
| DigitCountStl  | 2000       |  0.0141 ns | 0.0073 ns | 0.0068 ns |  0.0123 ns | 0.003 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 10000      |  0.4198 ns | 0.0032 ns | 0.0026 ns |  0.4205 ns | 0.074 |    0.00 |
| DigitCountMath | 10000      |  5.6927 ns | 0.0295 ns | 0.0276 ns |  5.6905 ns | 1.000 |    0.01 |
| DigitCountStl  | 10000      |  0.0134 ns | 0.0075 ns | 0.0063 ns |  0.0153 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 100000     |  0.4157 ns | 0.0082 ns | 0.0073 ns |  0.4176 ns |  0.08 |    0.00 |
| DigitCountMath | 100000     |  5.1700 ns | 0.0335 ns | 0.0297 ns |  5.1634 ns |  1.00 |    0.01 |
| DigitCountStl  | 100000     |  0.1226 ns | 0.0254 ns | 0.0312 ns |  0.1261 ns |  0.02 |    0.01 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 1000000    |  0.4174 ns | 0.0119 ns | 0.0105 ns |  0.4165 ns | 0.076 |    0.00 |
| DigitCountMath | 1000000    |  5.5268 ns | 0.0485 ns | 0.0405 ns |  5.5170 ns | 1.000 |    0.01 |
| DigitCountStl  | 1000000    |  0.0132 ns | 0.0112 ns | 0.0099 ns |  0.0112 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 4294967295 |  0.1785 ns | 0.0144 ns | 0.0127 ns |  0.1846 ns | 0.004 |    0.00 |
| DigitCountMath | 4294967295 | 44.7922 ns | 0.2744 ns | 0.2292 ns | 44.6872 ns | 1.000 |    0.01 |
| DigitCountStl  | 4294967295 |  0.0087 ns | 0.0087 ns | 0.0077 ns |  0.0074 ns | 0.000 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 9999       |  0.5805 ns | 0.0115 ns | 0.0107 ns |  0.5837 ns | 0.103 |    0.00 |
| DigitCountMath | 9999       |  5.6184 ns | 0.1338 ns | 0.1593 ns |  5.5114 ns | 1.001 |    0.04 |
| DigitCountStl  | 9999       |  0.0109 ns | 0.0074 ns | 0.0069 ns |  0.0097 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 99999      |  0.4225 ns | 0.0065 ns | 0.0054 ns |  0.4215 ns | 0.077 |    0.00 |
| DigitCountMath | 99999      |  5.5176 ns | 0.0482 ns | 0.0376 ns |  5.5232 ns | 1.000 |    0.01 |
| DigitCountStl  | 99999      |  0.0034 ns | 0.0037 ns | 0.0034 ns |  0.0019 ns | 0.001 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 999999     |  0.4193 ns | 0.0069 ns | 0.0061 ns |  0.4195 ns | 0.078 |    0.00 |
| DigitCountMath | 999999     |  5.3785 ns | 0.0303 ns | 0.0253 ns |  5.3790 ns | 1.000 |    0.01 |
| DigitCountStl  | 999999     |  0.0036 ns | 0.0034 ns | 0.0032 ns |  0.0050 ns | 0.001 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 9999999    |  0.4226 ns | 0.0050 ns | 0.0046 ns |  0.4212 ns | 0.076 |    0.00 |
| DigitCountMath | 9999999    |  5.5908 ns | 0.0458 ns | 0.0406 ns |  5.6020 ns | 1.000 |    0.01 |
| DigitCountStl  | 9999999    |  0.0034 ns | 0.0050 ns | 0.0047 ns |  0.0000 ns | 0.001 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 10000000   |  0.4171 ns | 0.0066 ns | 0.0061 ns |  0.4182 ns | 0.079 |    0.00 |
| DigitCountMath | 10000000   |  5.2725 ns | 0.0489 ns | 0.0458 ns |  5.2827 ns | 1.000 |    0.01 |
| DigitCountStl  | 10000000   |  0.0081 ns | 0.0045 ns | 0.0037 ns |  0.0082 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 99999999   |  0.4141 ns | 0.0089 ns | 0.0079 ns |  0.4159 ns | 0.077 |    0.00 |
| DigitCountMath | 99999999   |  5.3638 ns | 0.0526 ns | 0.0439 ns |  5.3670 ns | 1.000 |    0.01 |
| DigitCountStl  | 99999999   |  0.0071 ns | 0.0048 ns | 0.0042 ns |  0.0060 ns | 0.001 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 100000000  |  0.4177 ns | 0.0073 ns | 0.0068 ns |  0.4181 ns | 0.075 |    0.00 |
| DigitCountMath | 100000000  |  5.5876 ns | 0.0236 ns | 0.0221 ns |  5.5919 ns | 1.000 |    0.01 |
| DigitCountStl  | 100000000  |  0.0074 ns | 0.0056 ns | 0.0052 ns |  0.0080 ns | 0.001 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 999999999  |  0.4156 ns | 0.0085 ns | 0.0075 ns |  0.4170 ns | 0.081 |    0.00 |
| DigitCountMath | 999999999  |  5.1526 ns | 0.0467 ns | 0.0414 ns |  5.1635 ns | 1.000 |    0.01 |
| DigitCountStl  | 999999999  |  0.0079 ns | 0.0069 ns | 0.0065 ns |  0.0093 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 1000000000 |  0.2022 ns | 0.0071 ns | 0.0067 ns |  0.2025 ns | 0.037 |    0.00 |
| DigitCountMath | 1000000000 |  5.4694 ns | 0.0497 ns | 0.0465 ns |  5.4826 ns | 1.000 |    0.01 |
| DigitCountStl  | 1000000000 |  0.0100 ns | 0.0072 ns | 0.0068 ns |  0.0078 ns | 0.002 |    0.00 |
|                |            |            |           |           |            |       |         |
| DigitCount     | 2147483647 |  0.2055 ns | 0.0102 ns | 0.0090 ns |  0.2047 ns | 0.038 |    0.00 |
| DigitCountMath | 2147483647 |  5.4119 ns | 0.0372 ns | 0.0291 ns |  5.4066 ns | 1.000 |    0.01 |
| DigitCountStl  | 2147483647 |  0.0087 ns | 0.0100 ns | 0.0089 ns |  0.0082 ns | 0.002 |    0.00 |

Somewhat surprisingly and somewhat unsurprisingly, our solution is much worse than the STL version. And it passes all tests when comparing against any solution, slower or not. The key is that `uint.Log2`  uses `BitOperations.Log2` which is a simple leading zero count (`LZCNT`) calculation and a subtraction from the bit count of the data type. Specifically also supported on various architectures as seen [in the source](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Numerics/BitOperations.cs#L277):
```csharp
[Intrinsic]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
[CLSCompliant(false)]
public static int Log2(uint value)
{
	// The 0->0 contract is fulfilled by setting the LSB to 1.
	// Log(1) is 0, and setting the LSB for values > 1 does not change the log2 result.
	value |= 1;

	// value    lzcnt   actual  expected
	// ..0001   31      31-31    0
	// ..0010   30      31-30    1
	// 0010..    2      31-2    29
	// 0100..    1      31-1    30
	// 1000..    0      31-0    31
	if (Lzcnt.IsSupported)
	{
		return 31 ^ (int)Lzcnt.LeadingZeroCount(value);
	}

	if (ArmBase.IsSupported)
	{
		return 31 ^ ArmBase.LeadingZeroCount(value);
	}

	if (WasmBase.IsSupported)
	{
		return 31 ^ WasmBase.LeadingZeroCount(value);
	}

	// BSR returns the log2 result directly. However BSR is slower than LZCNT
	// on AMD processors, so we leave it as a fallback only.
	if (X86Base.IsSupported)
	{
		return (int)X86Base.BitScanReverse(value);
	}

	// Fallback contract is 0->0
	return Log2SoftwareFallback(value);
}
```
 
 Like I said before,
 > One important note is that the .NET runtime has incorporated tons of optimizations having undergone strenuous benchmarking effort throughout the over two decades that it has been alive. So usually it should not come as a surprise that common methods \[...\] would be blazingly fast and any homebrew solution would appear terrible compared against it.
 
 I didn't even test the branch misprediction theory, but it should be self-evident that it plays a role. This could be partly mitigated by using a binary search structure in the code, but even then we still leave room for branch mispredictions, and we perform many comparisons, which means more instructions compared to the very simple `OR`, `XOR` and `LZCNT` solution of `Log2` and the lookup table that contains magic numbers to guarantee correctness for every single value.

There is one trick however we can apply to eliminate branch mispredictions. Instead of selectively performing the comparisons, we just perform them all. Only one will be true at a time, so we can OR the result of each comparison individually, something that modern CPUs will happily parallelize despite the long instruction list.

This is the code, tested of course against the baseline implementations for correctness:
```csharp
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

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static uint UIntBool(bool x)
{
	return x ? 1U : 0U;
}
```

| Method               | Value      | Mean      | Error     | StdDev    | Median    | Ratio  | RatioSD |
|--------------------- |----------- |----------:|----------:|----------:|----------:|-------:|--------:|
| DigitCount           | 0          | 0.8189 ns | 0.0055 ns | 0.0049 ns | 0.8189 ns |      ? |       ? |
| DigitCountStl        | 0          | 0.0013 ns | 0.0024 ns | 0.0023 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 0          | 3.1861 ns | 0.0190 ns | 0.0168 ns | 3.1880 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 1          | 0.8167 ns | 0.0044 ns | 0.0037 ns | 0.8173 ns |  14.77 |    8.53 |
| DigitCountStl        | 1          | 0.0678 ns | 0.0239 ns | 0.0275 ns | 0.0628 ns |   1.23 |    0.90 |
| DigitCountCompareAll | 1          | 2.6880 ns | 0.0205 ns | 0.0191 ns | 2.6897 ns |  48.63 |   28.07 |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 8          | 0.8146 ns | 0.0035 ns | 0.0031 ns | 0.8142 ns |      ? |       ? |
| DigitCountStl        | 8          | 0.0011 ns | 0.0027 ns | 0.0024 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 8          | 3.1729 ns | 0.0069 ns | 0.0061 ns | 3.1734 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 9          | 0.8148 ns | 0.0033 ns | 0.0029 ns | 0.8140 ns |      ? |       ? |
| DigitCountStl        | 9          | 0.0025 ns | 0.0044 ns | 0.0042 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 9          | 2.6603 ns | 0.0116 ns | 0.0103 ns | 2.6614 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 10         | 0.8122 ns | 0.0020 ns | 0.0019 ns | 0.8120 ns |      ? |       ? |
| DigitCountStl        | 10         | 0.0012 ns | 0.0023 ns | 0.0020 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 10         | 3.3860 ns | 0.0104 ns | 0.0092 ns | 3.3852 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 30         | 0.8135 ns | 0.0031 ns | 0.0027 ns | 0.8131 ns |      ? |       ? |
| DigitCountStl        | 30         | 0.0037 ns | 0.0064 ns | 0.0060 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 30         | 2.9449 ns | 0.0146 ns | 0.0129 ns | 2.9432 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 98         | 0.7690 ns | 0.0074 ns | 0.0065 ns | 0.7694 ns |      ? |       ? |
| DigitCountStl        | 98         | 0.0010 ns | 0.0035 ns | 0.0031 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 98         | 3.2044 ns | 0.0246 ns | 0.0230 ns | 3.2090 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 99         | 0.7672 ns | 0.0090 ns | 0.0084 ns | 0.7691 ns |      ? |       ? |
| DigitCountStl        | 99         | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 99         | 3.1984 ns | 0.0190 ns | 0.0177 ns | 3.1950 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 100        | 0.7703 ns | 0.0100 ns | 0.0084 ns | 0.7683 ns |      ? |       ? |
| DigitCountStl        | 100        | 0.0044 ns | 0.0062 ns | 0.0058 ns | 0.0009 ns |      ? |       ? |
| DigitCountCompareAll | 100        | 3.3017 ns | 0.0285 ns | 0.0253 ns | 3.2941 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 200        | 0.7738 ns | 0.0085 ns | 0.0079 ns | 0.7746 ns |      ? |       ? |
| DigitCountStl        | 200        | 0.0086 ns | 0.0096 ns | 0.0085 ns | 0.0078 ns |      ? |       ? |
| DigitCountCompareAll | 200        | 2.8702 ns | 0.0237 ns | 0.0222 ns | 2.8659 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 255        | 0.7686 ns | 0.0093 ns | 0.0087 ns | 0.7682 ns |      ? |       ? |
| DigitCountStl        | 255        | 0.0027 ns | 0.0042 ns | 0.0039 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 255        | 2.8678 ns | 0.0272 ns | 0.0254 ns | 2.8648 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 999        | 0.7663 ns | 0.0100 ns | 0.0088 ns | 0.7678 ns |      ? |       ? |
| DigitCountStl        | 999        | 0.0051 ns | 0.0047 ns | 0.0044 ns | 0.0049 ns |      ? |       ? |
| DigitCountCompareAll | 999        | 3.3256 ns | 0.0215 ns | 0.0191 ns | 3.3321 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 1000       | 0.5657 ns | 0.0099 ns | 0.0093 ns | 0.5628 ns |      ? |       ? |
| DigitCountStl        | 1000       | 0.0032 ns | 0.0048 ns | 0.0043 ns | 0.0015 ns |      ? |       ? |
| DigitCountCompareAll | 1000       | 3.3060 ns | 0.0287 ns | 0.0254 ns | 3.2989 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 2000       | 0.5607 ns | 0.0125 ns | 0.0110 ns | 0.5560 ns |  46.83 |   31.26 |
| DigitCountStl        | 2000       | 0.0152 ns | 0.0067 ns | 0.0059 ns | 0.0154 ns |   1.27 |    1.02 |
| DigitCountCompareAll | 2000       | 3.8062 ns | 0.0225 ns | 0.0210 ns | 3.7998 ns | 317.90 |  212.09 |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 10000      | 0.4135 ns | 0.0105 ns | 0.0098 ns | 0.4167 ns |      ? |       ? |
| DigitCountStl        | 10000      | 0.0055 ns | 0.0061 ns | 0.0057 ns | 0.0042 ns |      ? |       ? |
| DigitCountCompareAll | 10000      | 4.0481 ns | 0.0293 ns | 0.0274 ns | 4.0470 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 100000     | 0.4111 ns | 0.0098 ns | 0.0087 ns | 0.4115 ns |      ? |       ? |
| DigitCountStl        | 100000     | 0.0022 ns | 0.0032 ns | 0.0027 ns | 0.0003 ns |      ? |       ? |
| DigitCountCompareAll | 100000     | 4.1284 ns | 0.0225 ns | 0.0210 ns | 4.1326 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 1000000    | 0.4111 ns | 0.0065 ns | 0.0061 ns | 0.4108 ns |      ? |       ? |
| DigitCountStl        | 1000000    | 0.0017 ns | 0.0016 ns | 0.0015 ns | 0.0018 ns |      ? |       ? |
| DigitCountCompareAll | 1000000    | 4.3462 ns | 0.0338 ns | 0.0300 ns | 4.3395 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 4294967295 | 0.1986 ns | 0.0107 ns | 0.0100 ns | 0.1977 ns |      ? |       ? |
| DigitCountStl        | 4294967295 | 0.0051 ns | 0.0057 ns | 0.0053 ns | 0.0030 ns |      ? |       ? |
| DigitCountCompareAll | 4294967295 | 4.3154 ns | 0.0309 ns | 0.0289 ns | 4.3131 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 9999       | 0.5574 ns | 0.0087 ns | 0.0082 ns | 0.5590 ns |      ? |       ? |
| DigitCountStl        | 9999       | 0.0059 ns | 0.0048 ns | 0.0045 ns | 0.0081 ns |      ? |       ? |
| DigitCountCompareAll | 9999       | 3.8211 ns | 0.0396 ns | 0.0371 ns | 3.8298 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 99999      | 0.4107 ns | 0.0059 ns | 0.0055 ns | 0.4101 ns |      ? |       ? |
| DigitCountStl        | 99999      | 0.0041 ns | 0.0063 ns | 0.0059 ns | 0.0009 ns |      ? |       ? |
| DigitCountCompareAll | 99999      | 4.0510 ns | 0.0180 ns | 0.0168 ns | 4.0512 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 999999     | 0.4139 ns | 0.0049 ns | 0.0046 ns | 0.4147 ns |      ? |       ? |
| DigitCountStl        | 999999     | 0.0062 ns | 0.0078 ns | 0.0073 ns | 0.0035 ns |      ? |       ? |
| DigitCountCompareAll | 999999     | 4.4611 ns | 0.0340 ns | 0.0302 ns | 4.4663 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 9999999    | 0.4095 ns | 0.0056 ns | 0.0049 ns | 0.4103 ns |      ? |       ? |
| DigitCountStl        | 9999999    | 0.0038 ns | 0.0060 ns | 0.0053 ns | 0.0001 ns |      ? |       ? |
| DigitCountCompareAll | 9999999    | 4.3385 ns | 0.0261 ns | 0.0232 ns | 4.3306 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 10000000   | 0.4065 ns | 0.0068 ns | 0.0060 ns | 0.4072 ns |      ? |       ? |
| DigitCountStl        | 10000000   | 0.0004 ns | 0.0010 ns | 0.0009 ns | 0.0000 ns |      ? |       ? |
| DigitCountCompareAll | 10000000   | 4.3177 ns | 0.0263 ns | 0.0246 ns | 4.3191 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 99999999   | 0.4053 ns | 0.0073 ns | 0.0068 ns | 0.4075 ns |      ? |       ? |
| DigitCountStl        | 99999999   | 0.0052 ns | 0.0070 ns | 0.0066 ns | 0.0003 ns |      ? |       ? |
| DigitCountCompareAll | 99999999   | 4.3306 ns | 0.0444 ns | 0.0393 ns | 4.3432 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 100000000  | 0.4059 ns | 0.0089 ns | 0.0083 ns | 0.4093 ns |      ? |       ? |
| DigitCountStl        | 100000000  | 0.0043 ns | 0.0059 ns | 0.0055 ns | 0.0023 ns |      ? |       ? |
| DigitCountCompareAll | 100000000  | 4.3426 ns | 0.0327 ns | 0.0306 ns | 4.3448 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 999999999  | 0.4201 ns | 0.0172 ns | 0.0144 ns | 0.4165 ns |      ? |       ? |
| DigitCountStl        | 999999999  | 0.0056 ns | 0.0034 ns | 0.0032 ns | 0.0058 ns |      ? |       ? |
| DigitCountCompareAll | 999999999  | 4.3047 ns | 0.0373 ns | 0.0349 ns | 4.3176 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 1000000000 | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.0000 ns |      ? |       ? |
| DigitCountStl        | 1000000000 | 0.0027 ns | 0.0039 ns | 0.0035 ns | 0.0010 ns |      ? |       ? |
| DigitCountCompareAll | 1000000000 | 4.2953 ns | 0.0270 ns | 0.0253 ns | 4.2983 ns |      ? |       ? |
|                      |            |           |           |           |           |        |         |
| DigitCount           | 2147483647 | 0.2061 ns | 0.0074 ns | 0.0069 ns | 0.2094 ns |      ? |       ? |
| DigitCountStl        | 2147483647 | 0.0026 ns | 0.0027 ns | 0.0026 ns | 0.0020 ns |      ? |       ? |
| DigitCountCompareAll | 2147483647 | 4.3282 ns | 0.0342 ns | 0.0320 ns | 4.3364 ns |      ? |       ? |

The compare all solution is always too much slower than the STL one. However this may be attributed to having used `&&`, which conditionally avoids executing the right hand side of the operand if the left one is `false`. Replacing this shorthand with the bitwise operator `&` we have this code:
```csharp
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
```

| Method                      | Value      |      Mean |     Error |    StdDev |    Median |    Ratio |  RatioSD |
| --------------------------- | ---------- | --------: | --------: | --------: | --------: | -------: | -------: |
| DigitCount                  | 0          | 0.7782 ns | 0.0125 ns | 0.0117 ns | 0.7760 ns |        ? |        ? |
| DigitCountStl               | 0          | 0.0031 ns | 0.0026 ns | 0.0020 ns | 0.0033 ns |        ? |        ? |
| DigitCountCompareAll        | 0          | 2.2910 ns | 0.0212 ns | 0.0165 ns | 2.2928 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 0          | 6.3757 ns | 0.0243 ns | 0.0227 ns | 6.3796 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 1          | 0.7730 ns | 0.0053 ns | 0.0044 ns | 0.7742 ns |        ? |        ? |
| DigitCountStl               | 1          | 0.0088 ns | 0.0067 ns | 0.0059 ns | 0.0075 ns |        ? |        ? |
| DigitCountCompareAll        | 1          | 3.0395 ns | 0.0218 ns | 0.0182 ns | 3.0471 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 1          | 5.9845 ns | 0.0498 ns | 0.0465 ns | 5.9873 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 8          | 0.7740 ns | 0.0076 ns | 0.0071 ns | 0.7742 ns |        ? |        ? |
| DigitCountStl               | 8          | 0.0046 ns | 0.0056 ns | 0.0052 ns | 0.0014 ns |        ? |        ? |
| DigitCountCompareAll        | 8          | 3.0067 ns | 0.0166 ns | 0.0147 ns | 3.0114 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 8          | 6.1902 ns | 0.0380 ns | 0.0337 ns | 6.1831 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 9          | 0.7802 ns | 0.0067 ns | 0.0060 ns | 0.7788 ns |        ? |        ? |
| DigitCountStl               | 9          | 0.0006 ns | 0.0013 ns | 0.0012 ns | 0.0000 ns |        ? |        ? |
| DigitCountCompareAll        | 9          | 3.0082 ns | 0.0166 ns | 0.0155 ns | 3.0059 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 9          | 6.1984 ns | 0.0378 ns | 0.0335 ns | 6.1906 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 10         | 0.7654 ns | 0.0086 ns | 0.0081 ns | 0.7632 ns |        ? |        ? |
| DigitCountStl               | 10         | 0.0083 ns | 0.0079 ns | 0.0074 ns | 0.0102 ns |        ? |        ? |
| DigitCountCompareAll        | 10         | 2.7239 ns | 0.0318 ns | 0.0298 ns | 2.7270 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 10         | 6.1573 ns | 0.0368 ns | 0.0345 ns | 6.1697 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 30         | 0.7666 ns | 0.0128 ns | 0.0114 ns | 0.7653 ns |        ? |        ? |
| DigitCountStl               | 30         | 0.0059 ns | 0.0061 ns | 0.0057 ns | 0.0049 ns |        ? |        ? |
| DigitCountCompareAll        | 30         | 3.0021 ns | 0.0235 ns | 0.0220 ns | 3.0067 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 30         | 5.9558 ns | 0.0251 ns | 0.0210 ns | 5.9583 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 98         | 0.7744 ns | 0.0058 ns | 0.0055 ns | 0.7758 ns |        ? |        ? |
| DigitCountStl               | 98         | 0.0052 ns | 0.0052 ns | 0.0049 ns | 0.0048 ns |        ? |        ? |
| DigitCountCompareAll        | 98         | 3.2024 ns | 0.0251 ns | 0.0235 ns | 3.2062 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 98         | 6.1698 ns | 0.0329 ns | 0.0308 ns | 6.1617 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 99         | 0.7707 ns | 0.0106 ns | 0.0099 ns | 0.7704 ns |        ? |        ? |
| DigitCountStl               | 99         | 0.0058 ns | 0.0062 ns | 0.0058 ns | 0.0055 ns |        ? |        ? |
| DigitCountCompareAll        | 99         | 2.9274 ns | 0.0123 ns | 0.0109 ns | 2.9272 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 99         | 6.1971 ns | 0.0463 ns | 0.0433 ns | 6.2030 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 100        | 0.7877 ns | 0.0225 ns | 0.0210 ns | 0.7909 ns |   108.94 |   133.97 |
| DigitCountStl               | 100        | 0.0136 ns | 0.0081 ns | 0.0076 ns | 0.0126 ns |     1.88 |     2.82 |
| DigitCountCompareAll        | 100        | 3.3658 ns | 0.0445 ns | 0.0416 ns | 3.3600 ns |   465.49 |   572.21 |
| DigitCountCompareAllBitwise | 100        | 6.3763 ns | 0.0510 ns | 0.0478 ns | 6.3690 ns |   881.83 | 1,083.92 |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 200        | 0.7730 ns | 0.0089 ns | 0.0083 ns | 0.7737 ns |        ? |        ? |
| DigitCountStl               | 200        | 0.0070 ns | 0.0083 ns | 0.0077 ns | 0.0039 ns |        ? |        ? |
| DigitCountCompareAll        | 200        | 2.8969 ns | 0.0407 ns | 0.0381 ns | 2.8905 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 200        | 6.3460 ns | 0.0178 ns | 0.0166 ns | 6.3415 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 255        | 0.7705 ns | 0.0081 ns | 0.0076 ns | 0.7709 ns |        ? |        ? |
| DigitCountStl               | 255        | 0.0009 ns | 0.0023 ns | 0.0021 ns | 0.0000 ns |        ? |        ? |
| DigitCountCompareAll        | 255        | 3.3198 ns | 0.0294 ns | 0.0275 ns | 3.3287 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 255        | 6.3073 ns | 0.0380 ns | 0.0356 ns | 6.3114 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 999        | 0.7713 ns | 0.0102 ns | 0.0095 ns | 0.7686 ns |        ? |        ? |
| DigitCountStl               | 999        | 0.0079 ns | 0.0112 ns | 0.0099 ns | 0.0029 ns |        ? |        ? |
| DigitCountCompareAll        | 999        | 3.0681 ns | 0.0217 ns | 0.0203 ns | 3.0716 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 999        | 5.9243 ns | 0.0444 ns | 0.0415 ns | 5.9234 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 1000       | 0.5585 ns | 0.0086 ns | 0.0081 ns | 0.5578 ns |        ? |        ? |
| DigitCountStl               | 1000       | 0.0090 ns | 0.0075 ns | 0.0070 ns | 0.0095 ns |        ? |        ? |
| DigitCountCompareAll        | 1000       | 3.8067 ns | 0.0221 ns | 0.0207 ns | 3.8087 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 1000       | 5.9980 ns | 0.0454 ns | 0.0402 ns | 6.0065 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 2000       | 0.5544 ns | 0.0100 ns | 0.0093 ns | 0.5563 ns |        ? |        ? |
| DigitCountStl               | 2000       | 0.0046 ns | 0.0065 ns | 0.0061 ns | 0.0014 ns |        ? |        ? |
| DigitCountCompareAll        | 2000       | 3.8354 ns | 0.0313 ns | 0.0277 ns | 3.8390 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 2000       | 6.4133 ns | 0.0423 ns | 0.0395 ns | 6.4140 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 9999       | 0.5620 ns | 0.0061 ns | 0.0057 ns | 0.5633 ns |        ? |        ? |
| DigitCountStl               | 9999       | 0.0073 ns | 0.0052 ns | 0.0044 ns | 0.0074 ns |        ? |        ? |
| DigitCountCompareAll        | 9999       | 3.8096 ns | 0.0163 ns | 0.0152 ns | 3.8132 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 9999       | 5.9808 ns | 0.0475 ns | 0.0445 ns | 5.9985 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 10000      | 0.4031 ns | 0.0077 ns | 0.0072 ns | 0.4027 ns |        ? |        ? |
| DigitCountStl               | 10000      | 0.0054 ns | 0.0068 ns | 0.0064 ns | 0.0059 ns |        ? |        ? |
| DigitCountCompareAll        | 10000      | 4.0583 ns | 0.0271 ns | 0.0254 ns | 4.0640 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 10000      | 5.9876 ns | 0.0457 ns | 0.0428 ns | 5.9953 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 99999      | 0.4080 ns | 0.0067 ns | 0.0060 ns | 0.4069 ns |        ? |        ? |
| DigitCountStl               | 99999      | 0.0064 ns | 0.0065 ns | 0.0061 ns | 0.0046 ns |        ? |        ? |
| DigitCountCompareAll        | 99999      | 3.6437 ns | 0.0305 ns | 0.0285 ns | 3.6411 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 99999      | 6.0760 ns | 0.0523 ns | 0.0489 ns | 6.0942 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 100000     | 0.4115 ns | 0.0101 ns | 0.0095 ns | 0.4140 ns |        ? |        ? |
| DigitCountStl               | 100000     | 0.0055 ns | 0.0073 ns | 0.0068 ns | 0.0012 ns |        ? |        ? |
| DigitCountCompareAll        | 100000     | 4.1111 ns | 0.0253 ns | 0.0224 ns | 4.1175 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 100000     | 6.4007 ns | 0.0528 ns | 0.0494 ns | 6.4124 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 999999     | 0.4102 ns | 0.0056 ns | 0.0050 ns | 0.4087 ns |    55.26 |    50.83 |
| DigitCountStl               | 999999     | 0.0118 ns | 0.0069 ns | 0.0061 ns | 0.0121 ns |     1.59 |     1.81 |
| DigitCountCompareAll        | 999999     | 4.1250 ns | 0.0279 ns | 0.0261 ns | 4.1202 ns |   555.60 |   511.00 |
| DigitCountCompareAllBitwise | 999999     | 6.0691 ns | 0.0383 ns | 0.0359 ns | 6.0606 ns |   817.45 |   751.83 |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 1000000    | 0.4102 ns | 0.0060 ns | 0.0056 ns | 0.4107 ns |        ? |        ? |
| DigitCountStl               | 1000000    | 0.0042 ns | 0.0053 ns | 0.0047 ns | 0.0031 ns |        ? |        ? |
| DigitCountCompareAll        | 1000000    | 4.3411 ns | 0.0189 ns | 0.0177 ns | 4.3447 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 1000000    | 6.2638 ns | 0.0409 ns | 0.0382 ns | 6.2690 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 9999999    | 0.4017 ns | 0.0055 ns | 0.0051 ns | 0.4012 ns |        ? |        ? |
| DigitCountStl               | 9999999    | 0.0039 ns | 0.0041 ns | 0.0038 ns | 0.0022 ns |        ? |        ? |
| DigitCountCompareAll        | 9999999    | 4.1237 ns | 0.0257 ns | 0.0240 ns | 4.1178 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 9999999    | 6.1515 ns | 0.0382 ns | 0.0339 ns | 6.1493 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 10000000   | 0.4101 ns | 0.0063 ns | 0.0056 ns | 0.4117 ns |        ? |        ? |
| DigitCountStl               | 10000000   | 0.0045 ns | 0.0035 ns | 0.0029 ns | 0.0047 ns |        ? |        ? |
| DigitCountCompareAll        | 10000000   | 4.1442 ns | 0.0161 ns | 0.0142 ns | 4.1452 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 10000000   | 6.1500 ns | 0.0390 ns | 0.0365 ns | 6.1446 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 99999999   | 0.4053 ns | 0.0040 ns | 0.0036 ns | 0.4055 ns |        ? |        ? |
| DigitCountStl               | 99999999   | 0.0016 ns | 0.0028 ns | 0.0026 ns | 0.0002 ns |        ? |        ? |
| DigitCountCompareAll        | 99999999   | 4.3457 ns | 0.0211 ns | 0.0197 ns | 4.3496 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 99999999   | 5.9744 ns | 0.0417 ns | 0.0390 ns | 5.9805 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 100000000  | 0.4132 ns | 0.0051 ns | 0.0045 ns | 0.4127 ns |        ? |        ? |
| DigitCountStl               | 100000000  | 0.0069 ns | 0.0069 ns | 0.0064 ns | 0.0060 ns |        ? |        ? |
| DigitCountCompareAll        | 100000000  | 4.3296 ns | 0.0327 ns | 0.0306 ns | 4.3352 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 100000000  | 6.3175 ns | 0.0465 ns | 0.0435 ns | 6.3246 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 1000000000 | 0.2005 ns | 0.0089 ns | 0.0083 ns | 0.2039 ns |    59.78 |   119.57 |
| DigitCountStl               | 1000000000 | 0.0119 ns | 0.0088 ns | 0.0074 ns | 0.0127 ns |     3.54 |     8.50 |
| DigitCountCompareAll        | 1000000000 | 4.3350 ns | 0.0447 ns | 0.0396 ns | 4.3332 ns | 1,292.45 | 2,583.38 |
| DigitCountCompareAllBitwise | 1000000000 | 6.1930 ns | 0.0539 ns | 0.0504 ns | 6.2093 ns | 1,846.42 | 3,689.95 |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 2147483647 | 0.2034 ns | 0.0084 ns | 0.0079 ns | 0.2042 ns |        ? |        ? |
| DigitCountStl               | 2147483647 | 0.0036 ns | 0.0047 ns | 0.0044 ns | 0.0019 ns |        ? |        ? |
| DigitCountCompareAll        | 2147483647 | 4.3401 ns | 0.0223 ns | 0.0198 ns | 4.3399 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 2147483647 | 5.9944 ns | 0.0603 ns | 0.0564 ns | 6.0040 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 4294967295 | 0.2002 ns | 0.0101 ns | 0.0094 ns | 0.2038 ns |        ? |        ? |
| DigitCountStl               | 4294967295 | 0.0042 ns | 0.0037 ns | 0.0035 ns | 0.0052 ns |        ? |        ? |
| DigitCountCompareAll        | 4294967295 | 4.3481 ns | 0.0473 ns | 0.0443 ns | 4.3386 ns |        ? |        ? |
| DigitCountCompareAllBitwise | 4294967295 | 6.1392 ns | 0.0473 ns | 0.0443 ns | 6.1391 ns |        ? |        ? |
|                             |            |           |           |           |           |          |          |
| DigitCount                  | 999999999  | 0.4065 ns | 0.0099 ns | 0.0093 ns | 0.4083 ns |     8.61 |     3.21 |
| DigitCountStl               | 999999999  | 0.0529 ns | 0.0184 ns | 0.0173 ns | 0.0513 ns |     1.12 |     0.56 |
| DigitCountCompareAll        | 999999999  | 4.3439 ns | 0.0324 ns | 0.0303 ns | 4.3445 ns |    92.05 |    34.27 |
| DigitCountCompareAllBitwise | 999999999  | 6.3036 ns | 0.0413 ns | 0.0345 ns | 6.3011 ns |   133.57 |    49.75 |

It's just slower. The code gen appears "normal":
```msil
C.DigitCountCompareAllBitwise(UInt32)
    L0000: push ebp
    L0001: mov ebp, esp
    L0003: push ebx
    L0004: cmp ecx, 0xa
    L0007: setae al
    L000a: movzx eax, al
    L000d: cmp ecx, 0x64
    L0010: setb dl
    L0013: movzx edx, dl
    L0016: and eax, edx
    L0018: shl eax, 2
    L001b: cmp ecx, 0xa
    L001e: setb dl
    L0021: movzx edx, dl
    L0024: add edx, edx
    L0026: or eax, edx
    L0028: cmp ecx, 0x64
    L002b: setae dl
    L002e: movzx edx, dl
    L0031: cmp ecx, 0x3e8
    L0037: setb bl
    L003a: movzx ebx, bl
    L003d: and edx, ebx
    L003f: shl edx, 3
    L0042: or eax, edx
    L0044: cmp ecx, 0x3e8
    L004a: setae dl
    L004d: movzx edx, dl
    L0050: cmp ecx, 0x2710
    L0056: setb bl
    L0059: movzx ebx, bl
    L005c: and edx, ebx
    L005e: shl edx, 4
    L0061: or eax, edx
    L0063: cmp ecx, 0x2710
    L0069: setae dl
    L006c: movzx edx, dl
    L006f: cmp ecx, 0x186a0
    L0075: setb bl
    L0078: movzx ebx, bl
    L007b: and edx, ebx
    L007d: shl edx, 5
    L0080: or eax, edx
    L0082: cmp ecx, 0x186a0
    L0088: setae dl
    L008b: movzx edx, dl
    L008e: cmp ecx, 0xf4240
    L0094: setb bl
    L0097: movzx ebx, bl
    L009a: and edx, ebx
    L009c: shl edx, 6
    L009f: or eax, edx
    L00a1: cmp ecx, 0xf4240
    L00a7: setae dl
    L00aa: movzx edx, dl
    L00ad: cmp ecx, 0x989680
    L00b3: setb bl
    L00b6: movzx ebx, bl
    L00b9: and edx, ebx
    L00bb: shl edx, 7
    L00be: or eax, edx
    L00c0: cmp ecx, 0x989680
    L00c6: setae dl
    L00c9: movzx edx, dl
    L00cc: cmp ecx, 0x5f5e100
    L00d2: setb bl
    L00d5: movzx ebx, bl
    L00d8: and edx, ebx
    L00da: shl edx, 8
    L00dd: or eax, edx
    L00df: cmp ecx, 0x5f5e100
    L00e5: setae dl
    L00e8: movzx edx, dl
    L00eb: cmp ecx, 0x3b9aca00
    L00f1: setb bl
    L00f4: movzx ebx, bl
    L00f7: and edx, ebx
    L00f9: shl edx, 9
    L00fc: or eax, edx
    L00fe: cmp ecx, 0x3b9aca00
    L0104: setae dl
    L0107: movzx edx, dl
    L010a: shl edx, 0xa
    L010d: or eax, edx
    L010f: or eax, 1
    L0112: lzcnt eax, eax
    L0116: xor eax, 0x1f
    L0119: pop ebx
    L011a: pop ebp
    L011b: ret
```
The problem is this is a massive function. And while this would be much better in the circuit, writing it in software only makes it unbearably slower. Despite the UIntBool function being inlined, and despite avoiding branch mispredictions, this solution is destined to be slower.

Moving on to the 64-bit version of the algorithm, we'll only compare the original if-based DigitCount implementation against [.NET's STL implementation](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/FormattingHelpers.CountDigits.cs#L15). For brevity, we will omit some magnitudes due to the large value range of 64-bit integers.

To avoid getting our custom implementation too lengthy, that we will simply check if the number is over 10 digits long and get the upper digits' decimal length, looking like this:
```csharp
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
```

The results came back:

| Method        | Value                | Mean      | Error     | StdDev    | Ratio | RatioSD |
|-------------- |--------------------- |----------:|----------:|----------:|------:|--------:|
| DigitCount    | 0                    | 1.0041 ns | 0.0093 ns | 0.0082 ns |  4.34 |    0.20 |
| DigitCountStl | 0                    | 0.2316 ns | 0.0116 ns | 0.0108 ns |  1.00 |    0.06 |
|               |                      |           |           |           |       |         |
| DigitCount    | 1                    | 1.0029 ns | 0.0146 ns | 0.0129 ns |  4.34 |    0.18 |
| DigitCountStl | 1                    | 0.2315 ns | 0.0099 ns | 0.0093 ns |  1.00 |    0.06 |
|               |                      |           |           |           |       |         |
| DigitCount    | 10000000000          | 2.0440 ns | 0.0113 ns | 0.0100 ns |  9.00 |    0.22 |
| DigitCountStl | 10000000000          | 0.2273 ns | 0.0064 ns | 0.0057 ns |  1.00 |    0.03 |
|               |                      |           |           |           |       |         |
| DigitCount    | 1000000000000        | 2.0488 ns | 0.0129 ns | 0.0115 ns |  8.91 |    0.38 |
| DigitCountStl | 1000000000000        | 0.2303 ns | 0.0101 ns | 0.0095 ns |  1.00 |    0.06 |
|               |                      |           |           |           |       |         |
| DigitCount    | 1000000000000000     | 1.6820 ns | 0.0131 ns | 0.0122 ns |  7.30 |    0.40 |
| DigitCountStl | 1000000000000000     | 0.2310 ns | 0.0139 ns | 0.0130 ns |  1.00 |    0.08 |
|               |                      |           |           |           |       |         |
| DigitCount    | 100000000000000000   | 1.4218 ns | 0.0136 ns | 0.0127 ns |  6.16 |    0.22 |
| DigitCountStl | 100000000000000000   | 0.2310 ns | 0.0089 ns | 0.0083 ns |  1.00 |    0.05 |
|               |                      |           |           |           |       |         |
| DigitCount    | 18446744073709551615 | 1.4749 ns | 0.0138 ns | 0.0129 ns |  6.42 |    0.36 |
| DigitCountStl | 18446744073709551615 | 0.2305 ns | 0.0139 ns | 0.0130 ns |  1.00 |    0.08 |
|               |                      |           |           |           |       |         |
| DigitCount    | 4294967295           | 0.3446 ns | 0.0086 ns | 0.0081 ns |  1.47 |    0.06 |
| DigitCountStl | 4294967295           | 0.2345 ns | 0.0094 ns | 0.0088 ns |  1.00 |    0.05 |
|               |                      |           |           |           |       |         |
| DigitCount    | 9                    | 0.9871 ns | 0.0177 ns | 0.0157 ns |  4.26 |    0.16 |
| DigitCountStl | 9                    | 0.2321 ns | 0.0094 ns | 0.0083 ns |  1.00 |    0.05 |
|               |                      |           |           |           |       |         |
| DigitCount    | 10                   | 0.9886 ns | 0.0111 ns | 0.0087 ns |  4.25 |    0.24 |
| DigitCountStl | 10                   | 0.2334 ns | 0.0146 ns | 0.0136 ns |  1.00 |    0.08 |
|               |                      |           |           |           |       |         |
| DigitCount    | 99                   | 0.9888 ns | 0.0056 ns | 0.0050 ns |  4.40 |    0.18 |
| DigitCountStl | 99                   | 0.2251 ns | 0.0101 ns | 0.0094 ns |  1.00 |    0.06 |
|               |                      |           |           |           |       |         |
| DigitCount    | 100                  | 0.9857 ns | 0.0092 ns | 0.0086 ns |  4.29 |    0.12 |
| DigitCountStl | 100                  | 0.2300 ns | 0.0074 ns | 0.0062 ns |  1.00 |    0.04 |
|               |                      |           |           |           |       |         |
| DigitCount    | 200                  | 0.9843 ns | 0.0074 ns | 0.0065 ns |  4.43 |    0.24 |
| DigitCountStl | 200                  | 0.2227 ns | 0.0135 ns | 0.0126 ns |  1.00 |    0.08 |
|               |                      |           |           |           |       |         |
| DigitCount    | 999                  | 0.9811 ns | 0.0122 ns | 0.0114 ns |  4.37 |    0.17 |
| DigitCountStl | 999                  | 0.2250 ns | 0.0096 ns | 0.0085 ns |  1.00 |    0.05 |
|               |                      |           |           |           |       |         |
| DigitCount    | 1000                 | 0.7728 ns | 0.0075 ns | 0.0070 ns |  3.32 |    0.10 |
| DigitCountStl | 1000                 | 0.2330 ns | 0.0070 ns | 0.0066 ns |  1.00 |    0.04 |
|               |                      |           |           |           |       |         |
| DigitCount    | 2000                 | 0.7726 ns | 0.0052 ns | 0.0044 ns |  3.32 |    0.17 |
| DigitCountStl | 2000                 | 0.2335 ns | 0.0139 ns | 0.0130 ns |  1.00 |    0.07 |
|               |                      |           |           |           |       |         |
| DigitCount    | 9999                 | 0.7710 ns | 0.0134 ns | 0.0126 ns |  3.37 |    0.16 |
| DigitCountStl | 9999                 | 0.2292 ns | 0.0113 ns | 0.0106 ns |  1.00 |    0.06 |
|               |                      |           |           |           |       |         |
| DigitCount    | 10000                | 0.7779 ns | 0.0144 ns | 0.0128 ns |  3.29 |    0.14 |
| DigitCountStl | 10000                | 0.2367 ns | 0.0113 ns | 0.0101 ns |  1.00 |    0.06 |
|               |                      |           |           |           |       |         |
| DigitCount    | 99999                | 0.7706 ns | 0.0096 ns | 0.0089 ns |  3.39 |    0.17 |
| DigitCountStl | 99999                | 0.2281 ns | 0.0122 ns | 0.0114 ns |  1.00 |    0.07 |
|               |                      |           |           |           |       |         |
| DigitCount    | 1000000              | 0.5637 ns | 0.0081 ns | 0.0076 ns |  2.41 |    0.10 |
| DigitCountStl | 1000000              | 0.2344 ns | 0.0100 ns | 0.0094 ns |  1.00 |    0.05 |
|               |                      |           |           |           |       |         |
| DigitCount    | 9999999              | 0.5722 ns | 0.0066 ns | 0.0059 ns |  2.51 |    0.11 |
| DigitCountStl | 9999999              | 0.2285 ns | 0.0108 ns | 0.0101 ns |  1.00 |    0.06 |
|               |                      |           |           |           |       |         |
| DigitCount    | 100000000            | 0.3471 ns | 0.0084 ns | 0.0079 ns |  1.51 |    0.06 |
| DigitCountStl | 100000000            | 0.2299 ns | 0.0089 ns | 0.0084 ns |  1.00 |    0.05 |
|               |                      |           |           |           |       |         |
| DigitCount    | 999999999            | 0.3695 ns | 0.0216 ns | 0.0192 ns |  1.56 |    0.09 |
| DigitCountStl | 999999999            | 0.2377 ns | 0.0067 ns | 0.0063 ns |  1.00 |    0.04 |
|               |                      |           |           |           |       |         |
| DigitCount    | 1000000000           | 0.3506 ns | 0.0034 ns | 0.0030 ns |  1.51 |    0.05 |
| DigitCountStl | 1000000000           | 0.2317 ns | 0.0079 ns | 0.0074 ns |  1.00 |    0.04 |
|               |                      |           |           |           |       |         |
| DigitCount    | 2147483647           | 0.3192 ns | 0.0082 ns | 0.0073 ns |  1.40 |    0.05 |
| DigitCountStl | 2147483647           | 0.2287 ns | 0.0076 ns | 0.0059 ns |  1.00 |    0.04 |
|               |                      |           |           |           |       |         |
| DigitCount    | 9999999999           | 0.3400 ns | 0.0075 ns | 0.0070 ns |  1.09 |    0.06 |
| DigitCountStl | 9999999999           | 0.3124 ns | 0.0175 ns | 0.0164 ns |  1.00 |    0.07 |
|               |                      |           |           |           |       |         |
| DigitCount    | 99999999999          | 2.2406 ns | 0.0137 ns | 0.0121 ns |  9.84 |    0.54 |
| DigitCountStl | 99999999999          | 0.2283 ns | 0.0141 ns | 0.0131 ns |  1.00 |    0.08 |
|               |                      |           |           |           |       |         |
| DigitCount    | 9999999999999        | 2.2773 ns | 0.0204 ns | 0.0191 ns | 10.18 |    0.44 |
| DigitCountStl | 9999999999999        | 0.2240 ns | 0.0103 ns | 0.0096 ns |  1.00 |    0.06 |
|               |                      |           |           |           |       |         |
| DigitCount    | 9999999999999999     | 1.6846 ns | 0.0136 ns | 0.0127 ns |  7.29 |    0.16 |
| DigitCountStl | 9999999999999999     | 0.2312 ns | 0.0058 ns | 0.0051 ns |  1.00 |    0.03 |
|               |                      |           |           |           |       |         |
| DigitCount    | 999999999999999999   | 1.4165 ns | 0.0174 ns | 0.0162 ns |  6.06 |    0.36 |
| DigitCountStl | 999999999999999999   | 0.2344 ns | 0.0150 ns | 0.0140 ns |  1.00 |    0.08 |

It's undeniable that the STL knows what it's doing. Once again the custom implementation ate dirt and it's clear that we should step out of the way of integers. With execution times as short as ~0.23 ns (basically in-cycle execution), the STL solution is the best (so far) software solution to use. Therefore the only problem is that we do not have access to this method without browsing the source directly. This is a very low-cost and 100% correct set of methods to calculate the digit count of a number.

And we have not even tested against branch mispredictions yet. This is assuming the happy path is always followed after every execution; the CPU will keep expecting the same execution path until the `Value` changes, where its prediction will fail, maybe a few times until it decides on another execution path to predict for the rest of the benchmark on the same `Value`.

To test for branch misprediction impact, we will construct a scenario where there's many differently-sized integers being processed, to throw off the branch predictor enough that it will hesitate which path it should definitely take. Here is the benchmark:

```csharp
[IterationTime(250)]
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
```

We do not care about the absolute results here, but the relative impact of branch mispredictions. While there is some overhead attached to this process, we can only extrapolate that the performance impact will be worse than what is displayed in the run results. Therefore, using delegates (almost equivalent to function pointers) is acceptable here, and so is looping this scenario 4 times and adding the results to a sum. This summation was added to achieve two things:
- guarantee that the digit count functions are executed serially, without branching off independently to one-another, by depending the state of `sum` to the result of each individual execution
- avoid Benchmark.NET or the .NET JIT itself trimming out the unused results of the invocations
That summation is not an unlikely scenario though; if we wanted to concatenate all those numbers into a string, we would be continually increasing the concatenated string's total length to the digit count of each of those numbers, and potentially other content in-between.

| Method        | Mean     | Error   | StdDev  | Ratio | RatioSD |
|-------------- |---------:|--------:|--------:|------:|--------:|
| DigitCount    | 165.4 ns | 1.63 ns | 1.52 ns |  1.65 |    0.02 |
| DigitCountStl | 100.4 ns | 0.98 ns | 0.86 ns |  1.00 |    0.01 |

The result here shows that our implementation is 65% worse than the STL one. Based on the previous benchmarks with the fixed `Value` parameter, we had an average of ~180% worse performance than the STL solution. This improved performance ratio is thanks to the aforementioned overhead of adding and looping, where we have eliminated the isolation of each function's execution.

It is though evident that even in a simple simulation of pre-calculating the buffer length of the concatenation of multiple numbers such as the ones above, the STL solution is much better than this homebrewed digit count solution.

And that concludes the story for integers. Let's move on to floating-point numbers.

One good reason why this research even began is because I need to represent floating-point numbers supporting massive exponents, rolling a custom scientific notation data type. This type will use a 64-bit floating-point for the mantissa and a 64-bit integer for the exponent. While not the most efficient memory-wise, it supports "a good chunk" of the number range I need to cover[^1].

[^1]:In reality, I want to support a much more complex data structure representing power towers and exponential notation. This data type was simplified for the purposes of this article. The real type will use a f64 mantissa, u32 exponent and u32 power tower height, resulting in a very machine-friendly 16-byte struct. More on that in another article.

Since we intend to store the exponent in the u64 field of our data type, we want to keep the number in a normalized form. Let's define the data type to represent numbers in the form M x 10\^E, where M is the f64 mantissa in the range \[1, 10\), and E is the exponent in the range \[0, 2\^64\).

> In this theoretical type, let's assume that if E = 0, we will allow the mantissa to be in \[0, 1\) to support smaller values, but we won't represent too small values with E accepting negative values. This detail will not matter moving forward in the article.

When performing operations with such numbers, almost always the number will have to be normalized, since the exponent will have been probably changed. For example, 8e6 + 8e6 = 16e6, which normalizes to 1.6e7. To do this we need a fast way to determine the logarithm of the mantissa. In heavier operations like exponentiation, the mantissa may rise to much higher numbers, like for example 8e6 ^ 8 = 16,777,216e(6 x 8) = 16,777,216e48, which should be normalized to ~1.68e55.

I could not find any online sources about known fast approaches. So the best bet is to repurpose the above technique with the integers into floating-point numbers. For reference we will compare against the traditional `Math.Log10` function that we are given, as it was used for integers.

Here is the code that will be used:
```csharp
public static int ILog(double value)
{
	const double fastThreshold = 1e9;
	return value is 0 ? int.MinValue
		: value >= fastThreshold ? MathILog(value)
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

public static int ILogSwitch(double value)
{
	const double fastThreshold = 1e9;
	return value switch
	{
		0 => int.MinValue,
        >= fastThreshold => MathILog(value),

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

public static int MathILog(double value)
{
    return Math.Max(0, (int)Math.Log10(value));
}
```

> It is at this point in the writing that I realized I can express numbers in scientific notation. In C#, scientific notation literals are allowed, but they default to f64. The `f` suffix allows the constant value to be typed as f32. They can also be used for integer constants by casting them to the corresponding type like `(ulong)1e9`. Literal precision is not guaranteed nor warned about in this language, so for integers it's still best being explicit about the digits in the literal.

The first version includes a very ugly multi-level series of ternary operations to avoid explicit if statements. The second version is the same code as above, only with a C# switch expression to look cleaner. It is functionally identical, and the code gen should be very close if not identical. The "arbitrary" threshold of `1e9` was chosen to break the barrier of hard-coded cases and falling back to the Math.Log10 implementation.

Notice how this implementation returns the non-negative integral base-10 logarithm. We would have to compare against negative exponents if we wanted to support them, and return those negative exponents like `Math.Log10` correctly does. This will be done later. We also choose to ignore the sign of the number for the time being.

The benchmark we will run is this:
```csharp
[IterationTime(150)]
public class ILog10Benchmarks
{
    [Params(
	    // lots of values
    )]
    public double Value { get; set; }

    [Benchmark]
    public int CustomILog()
    {
        return Log10.ILog(Value);
    }

    [Benchmark]
    [Obsolete(ReasonStrings.Obsoletion.MarkedToAvoidWarnings)]
    public int CustomILogSwitch()
    {
        return Log10.ILogSwitch(Value);
    }

    [Benchmark]
    public int MathILog()
    {
        return Log10.MathILog(Value);
    }

    [Benchmark(Baseline = true)]
    public double MathLog10Baseline()
    {
        return Math.Log10(Value);
    }
}
```

For comparison fairness, we also include a benchmark testing `Math.Log10` alone as seen above. Here are some results:

| Method            | Value      | Mean      | Error     | StdDev    | Ratio | RatioSD |
|------------------ |----------- |----------:|----------:|----------:|------:|--------:|
| CustomILog        | 0.0001     | 0.3818 ns | 0.0110 ns | 0.0102 ns |  0.11 |    0.00 |
| CustomILogSwitch  | 0.0001     | 0.4566 ns | 0.0228 ns | 0.0202 ns |  0.14 |    0.01 |
| MathILog          | 0.0001     | 4.8741 ns | 0.0515 ns | 0.0456 ns |  1.46 |    0.02 |
| MathLog10Baseline | 0.0001     | 3.3280 ns | 0.0482 ns | 0.0403 ns |  1.00 |    0.02 |
|                   |            |           |           |           |       |         |
| CustomILog        | 0.001      | 0.3841 ns | 0.0122 ns | 0.0108 ns |  0.12 |    0.00 |
| CustomILogSwitch  | 0.001      | 0.4459 ns | 0.0095 ns | 0.0089 ns |  0.14 |    0.00 |
| MathILog          | 0.001      | 5.0889 ns | 0.0535 ns | 0.0500 ns |  1.56 |    0.02 |
| MathLog10Baseline | 0.001      | 3.2630 ns | 0.0286 ns | 0.0254 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 0.01       | 0.3846 ns | 0.0102 ns | 0.0095 ns |  0.11 |    0.00 |
| CustomILogSwitch  | 0.01       | 0.4508 ns | 0.0113 ns | 0.0106 ns |  0.13 |    0.00 |
| MathILog          | 0.01       | 4.8660 ns | 0.0354 ns | 0.0314 ns |  1.42 |    0.03 |
| MathLog10Baseline | 0.01       | 3.4221 ns | 0.0847 ns | 0.0751 ns |  1.00 |    0.03 |
|                   |            |           |           |           |       |         |
| CustomILog        | 0.1        | 0.3841 ns | 0.0134 ns | 0.0126 ns |  0.12 |    0.00 |
| CustomILogSwitch  | 0.1        | 0.4457 ns | 0.0131 ns | 0.0116 ns |  0.14 |    0.00 |
| MathILog          | 0.1        | 5.1025 ns | 0.0645 ns | 0.0572 ns |  1.58 |    0.02 |
| MathLog10Baseline | 0.1        | 3.2309 ns | 0.0332 ns | 0.0311 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 1          | 0.3805 ns | 0.0106 ns | 0.0094 ns |  0.09 |    0.00 |
| CustomILogSwitch  | 1          | 0.4729 ns | 0.0134 ns | 0.0126 ns |  0.12 |    0.00 |
| MathILog          | 1          | 6.4730 ns | 0.0592 ns | 0.0554 ns |  1.62 |    0.01 |
| MathLog10Baseline | 1          | 4.0074 ns | 0.0140 ns | 0.0124 ns |  1.00 |    0.00 |
|                   |            |           |           |           |       |         |
| CustomILog        | 9          | 0.3852 ns | 0.0104 ns | 0.0097 ns |  0.12 |    0.00 |
| CustomILogSwitch  | 9          | 0.4629 ns | 0.0231 ns | 0.0216 ns |  0.14 |    0.01 |
| MathILog          | 9          | 4.8993 ns | 0.0685 ns | 0.0607 ns |  1.47 |    0.02 |
| MathLog10Baseline | 9          | 3.3411 ns | 0.0222 ns | 0.0208 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 10         | 0.6043 ns | 0.0079 ns | 0.0070 ns |  0.18 |    0.00 |
| CustomILogSwitch  | 10         | 0.5970 ns | 0.0079 ns | 0.0070 ns |  0.18 |    0.00 |
| MathILog          | 10         | 5.0928 ns | 0.0442 ns | 0.0369 ns |  1.55 |    0.02 |
| MathLog10Baseline | 10         | 3.2845 ns | 0.0309 ns | 0.0289 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 10.01      | 0.5956 ns | 0.0038 ns | 0.0036 ns |  0.18 |    0.00 |
| CustomILogSwitch  | 10.01      | 0.6020 ns | 0.0060 ns | 0.0056 ns |  0.18 |    0.00 |
| MathILog          | 10.01      | 4.9070 ns | 0.0320 ns | 0.0299 ns |  1.50 |    0.01 |
| MathLog10Baseline | 10.01      | 3.2801 ns | 0.0217 ns | 0.0192 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 19         | 0.5857 ns | 0.0059 ns | 0.0052 ns |  0.18 |    0.00 |
| CustomILogSwitch  | 19         | 0.5974 ns | 0.0075 ns | 0.0070 ns |  0.18 |    0.00 |
| MathILog          | 19         | 5.1618 ns | 0.0535 ns | 0.0474 ns |  1.59 |    0.02 |
| MathLog10Baseline | 19         | 3.2463 ns | 0.0348 ns | 0.0326 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 99         | 0.6126 ns | 0.0294 ns | 0.0275 ns |  0.19 |    0.01 |
| CustomILogSwitch  | 99         | 0.5924 ns | 0.0083 ns | 0.0074 ns |  0.18 |    0.00 |
| MathILog          | 99         | 5.1195 ns | 0.0569 ns | 0.0532 ns |  1.55 |    0.02 |
| MathLog10Baseline | 99         | 3.3009 ns | 0.0362 ns | 0.0338 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 100        | 0.8125 ns | 0.0087 ns | 0.0072 ns |  0.22 |    0.00 |
| CustomILogSwitch  | 100        | 0.8201 ns | 0.0114 ns | 0.0107 ns |  0.22 |    0.00 |
| MathILog          | 100        | 5.2956 ns | 0.1065 ns | 0.0996 ns |  1.40 |    0.03 |
| MathLog10Baseline | 100        | 3.7775 ns | 0.0351 ns | 0.0311 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 999        | 0.8442 ns | 0.0074 ns | 0.0069 ns |  0.25 |    0.00 |
| CustomILogSwitch  | 999        | 0.8191 ns | 0.0115 ns | 0.0096 ns |  0.24 |    0.00 |
| MathILog          | 999        | 5.7339 ns | 0.0366 ns | 0.0324 ns |  1.67 |    0.02 |
| MathLog10Baseline | 999        | 3.4297 ns | 0.0401 ns | 0.0375 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 1000       | 0.8065 ns | 0.0176 ns | 0.0156 ns |  0.24 |    0.01 |
| CustomILogSwitch  | 1000       | 0.8139 ns | 0.0095 ns | 0.0089 ns |  0.24 |    0.01 |
| MathILog          | 1000       | 4.7733 ns | 0.0472 ns | 0.0418 ns |  1.40 |    0.04 |
| MathLog10Baseline | 1000       | 3.4159 ns | 0.0824 ns | 0.0846 ns |  1.00 |    0.03 |
|                   |            |           |           |           |       |         |
| CustomILog        | 9.999999   | 0.4030 ns | 0.0117 ns | 0.0109 ns |  0.12 |    0.00 |
| CustomILogSwitch  | 9.999999   | 0.4314 ns | 0.0099 ns | 0.0082 ns |  0.13 |    0.00 |
| MathILog          | 9.999999   | 5.1253 ns | 0.0616 ns | 0.0576 ns |  1.55 |    0.02 |
| MathLog10Baseline | 9.999999   | 3.3050 ns | 0.0402 ns | 0.0376 ns |  1.00 |    0.02 |
|                   |            |           |           |           |       |         |
| CustomILog        | 99.999     | 0.5985 ns | 0.0049 ns | 0.0046 ns |  0.18 |    0.00 |
| CustomILogSwitch  | 99.999     | 0.5975 ns | 0.0098 ns | 0.0092 ns |  0.18 |    0.00 |
| MathILog          | 99.999     | 5.0399 ns | 0.0377 ns | 0.0353 ns |  1.55 |    0.02 |
| MathLog10Baseline | 99.999     | 3.2592 ns | 0.0321 ns | 0.0300 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 9999       | 0.8497 ns | 0.0046 ns | 0.0038 ns |  0.26 |    0.00 |
| CustomILogSwitch  | 9999       | 0.8154 ns | 0.0287 ns | 0.0254 ns |  0.25 |    0.01 |
| MathILog          | 9999       | 5.0158 ns | 0.0557 ns | 0.0494 ns |  1.53 |    0.02 |
| MathLog10Baseline | 9999       | 3.2837 ns | 0.0262 ns | 0.0233 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 10000      | 1.0121 ns | 0.0116 ns | 0.0109 ns |  0.30 |    0.00 |
| CustomILogSwitch  | 10000      | 1.0146 ns | 0.0147 ns | 0.0130 ns |  0.31 |    0.01 |
| MathILog          | 10000      | 5.2426 ns | 0.0901 ns | 0.0843 ns |  1.58 |    0.03 |
| MathLog10Baseline | 10000      | 3.3216 ns | 0.0456 ns | 0.0427 ns |  1.00 |    0.02 |
|                   |            |           |           |           |       |         |
| CustomILog        | 99999      | 1.0305 ns | 0.0183 ns | 0.0162 ns |  0.31 |    0.01 |
| CustomILogSwitch  | 99999      | 1.0172 ns | 0.0140 ns | 0.0131 ns |  0.31 |    0.01 |
| MathILog          | 99999      | 5.1481 ns | 0.0634 ns | 0.0529 ns |  1.57 |    0.03 |
| MathLog10Baseline | 99999      | 3.2806 ns | 0.0560 ns | 0.0497 ns |  1.00 |    0.02 |
|                   |            |           |           |           |       |         |
| CustomILog        | 100000     | 1.2305 ns | 0.0314 ns | 0.0262 ns |  0.38 |    0.01 |
| CustomILogSwitch  | 100000     | 1.2740 ns | 0.0244 ns | 0.0204 ns |  0.39 |    0.01 |
| MathILog          | 100000     | 5.1597 ns | 0.0546 ns | 0.0484 ns |  1.58 |    0.02 |
| MathLog10Baseline | 100000     | 3.2749 ns | 0.0331 ns | 0.0276 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 999999     | 1.2284 ns | 0.0092 ns | 0.0081 ns |  0.37 |    0.01 |
| CustomILogSwitch  | 999999     | 1.2128 ns | 0.0118 ns | 0.0104 ns |  0.37 |    0.01 |
| MathILog          | 999999     | 5.1514 ns | 0.0454 ns | 0.0379 ns |  1.55 |    0.03 |
| MathLog10Baseline | 999999     | 3.3141 ns | 0.0604 ns | 0.0536 ns |  1.00 |    0.02 |
|                   |            |           |           |           |       |         |
| CustomILog        | 1000000    | 1.2393 ns | 0.0166 ns | 0.0147 ns |  0.39 |    0.01 |
| CustomILogSwitch  | 1000000    | 1.2458 ns | 0.0090 ns | 0.0075 ns |  0.39 |    0.01 |
| MathILog          | 1000000    | 5.1467 ns | 0.0740 ns | 0.0692 ns |  1.60 |    0.04 |
| MathLog10Baseline | 1000000    | 3.2116 ns | 0.0897 ns | 0.0795 ns |  1.00 |    0.03 |
|                   |            |           |           |           |       |         |
| CustomILog        | 9999999    | 1.2146 ns | 0.0154 ns | 0.0144 ns |  0.37 |    0.00 |
| CustomILogSwitch  | 9999999    | 1.3174 ns | 0.0168 ns | 0.0149 ns |  0.40 |    0.00 |
| MathILog          | 9999999    | 4.8626 ns | 0.0817 ns | 0.0724 ns |  1.48 |    0.02 |
| MathLog10Baseline | 9999999    | 3.2950 ns | 0.0197 ns | 0.0184 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 10000000   | 1.4266 ns | 0.0205 ns | 0.0171 ns |  0.44 |    0.01 |
| CustomILogSwitch  | 10000000   | 1.4239 ns | 0.0242 ns | 0.0226 ns |  0.44 |    0.01 |
| MathILog          | 10000000   | 5.0075 ns | 0.0440 ns | 0.0411 ns |  1.53 |    0.02 |
| MathLog10Baseline | 10000000   | 3.2702 ns | 0.0292 ns | 0.0273 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 99999999   | 1.4490 ns | 0.0134 ns | 0.0112 ns |  0.44 |    0.00 |
| CustomILogSwitch  | 99999999   | 1.4765 ns | 0.0374 ns | 0.0350 ns |  0.45 |    0.01 |
| MathILog          | 99999999   | 5.0754 ns | 0.0378 ns | 0.0315 ns |  1.55 |    0.02 |
| MathLog10Baseline | 99999999   | 3.2797 ns | 0.0278 ns | 0.0260 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 100000000  | 1.4745 ns | 0.0256 ns | 0.0227 ns |  0.44 |    0.01 |
| CustomILogSwitch  | 100000000  | 1.4362 ns | 0.0141 ns | 0.0132 ns |  0.43 |    0.00 |
| MathILog          | 100000000  | 5.0980 ns | 0.0310 ns | 0.0290 ns |  1.53 |    0.01 |
| MathLog10Baseline | 100000000  | 3.3273 ns | 0.0248 ns | 0.0220 ns |  1.00 |    0.01 |
|                   |            |           |           |           |       |         |
| CustomILog        | 999999999  | 1.4478 ns | 0.0111 ns | 0.0104 ns |  0.44 |    0.01 |
| CustomILogSwitch  | 999999999  | 1.4401 ns | 0.0113 ns | 0.0100 ns |  0.44 |    0.01 |
| MathILog          | 999999999  | 5.1248 ns | 0.0506 ns | 0.0448 ns |  1.56 |    0.02 |
| MathLog10Baseline | 999999999  | 3.2810 ns | 0.0410 ns | 0.0364 ns |  1.00 |    0.02 |
|                   |            |           |           |           |       |         |
| CustomILog        | 1000000000 | 5.2348 ns | 0.0210 ns | 0.0186 ns |  1.58 |    0.03 |
| CustomILogSwitch  | 1000000000 | 5.5635 ns | 0.0587 ns | 0.0549 ns |  1.68 |    0.03 |
| MathILog          | 1000000000 | 5.1127 ns | 0.0316 ns | 0.0296 ns |  1.54 |    0.03 |
| MathLog10Baseline | 1000000000 | 3.3131 ns | 0.0581 ns | 0.0544 ns |  1.00 |    0.02 |
|                   |            |           |           |           |       |         |
| CustomILog        | 9999999999 | 5.4485 ns | 0.1290 ns | 0.1206 ns |  1.65 |    0.04 |
| CustomILogSwitch  | 9999999999 | 5.2886 ns | 0.0384 ns | 0.0359 ns |  1.60 |    0.02 |
| MathILog          | 9999999999 | 5.6105 ns | 0.0894 ns | 0.0836 ns |  1.70 |    0.03 |
| MathLog10Baseline | 9999999999 | 3.3035 ns | 0.0253 ns | 0.0236 ns |  1.00 |    0.01 |

From these results it is evident that the ternary hell and the switch expression are almost identical in performance, if the difference of 0.05 ns can even count as a difference in the cases that it appears. And `Math.Log10` is always slower whenever invoked. The good thing about this hybrid approach is that it also calls the `MathILog` implementation as early as possible after 0, so the overhead of the comparison is tiny, as seen in the comparative results.

The above implementation and benchmark used f64 values, since `Math.Log10` accepts a f64 and we want to avoid an unnecessary extension of f32 to f64. There is also `MathF.Log10`, the f32 version of the base-10 logarithm provided by the STL. Copy-pasting the above code for floats with the appropriate modifications yields these benchmark results:

| Method            | Value     | Mean      | Error     | StdDev    | Ratio | RatioSD |
|------------------ |---------- |----------:|----------:|----------:|------:|--------:|
| CustomILog        | 0.0001    | 0.4177 ns | 0.0079 ns | 0.0074 ns |  0.16 |    0.00 |
| CustomILogSwitch  | 0.0001    | 0.4317 ns | 0.0101 ns | 0.0094 ns |  0.16 |    0.00 |
| MathILog          | 0.0001    | 4.4189 ns | 0.0401 ns | 0.0356 ns |  1.68 |    0.04 |
| MathLog10Baseline | 0.0001    | 2.6380 ns | 0.0631 ns | 0.0559 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 0.001     | 0.4171 ns | 0.0094 ns | 0.0088 ns |  0.16 |    0.00 |
| CustomILogSwitch  | 0.001     | 0.4192 ns | 0.0092 ns | 0.0077 ns |  0.16 |    0.00 |
| MathILog          | 0.001     | 4.4129 ns | 0.0454 ns | 0.0425 ns |  1.68 |    0.04 |
| MathLog10Baseline | 0.001     | 2.6247 ns | 0.0546 ns | 0.0511 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 0.01      | 0.4209 ns | 0.0066 ns | 0.0058 ns |  0.17 |    0.00 |
| CustomILogSwitch  | 0.01      | 0.4253 ns | 0.0090 ns | 0.0075 ns |  0.17 |    0.00 |
| MathILog          | 0.01      | 4.3923 ns | 0.0519 ns | 0.0485 ns |  1.72 |    0.03 |
| MathLog10Baseline | 0.01      | 2.5481 ns | 0.0492 ns | 0.0436 ns |  1.00 |    0.02 |
|                   |           |           |           |           |       |         |
| CustomILog        | 0.1       | 0.4259 ns | 0.0114 ns | 0.0089 ns |  0.17 |    0.00 |
| CustomILogSwitch  | 0.1       | 0.4169 ns | 0.0108 ns | 0.0101 ns |  0.17 |    0.01 |
| MathILog          | 0.1       | 4.8548 ns | 0.0350 ns | 0.0327 ns |  1.99 |    0.04 |
| MathLog10Baseline | 0.1       | 2.4466 ns | 0.0551 ns | 0.0515 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 1         | 0.4151 ns | 0.0105 ns | 0.0098 ns |  0.15 |    0.00 |
| CustomILogSwitch  | 1         | 0.4119 ns | 0.0126 ns | 0.0111 ns |  0.15 |    0.00 |
| MathILog          | 1         | 4.8400 ns | 0.0291 ns | 0.0227 ns |  1.72 |    0.03 |
| MathLog10Baseline | 1         | 2.8157 ns | 0.0476 ns | 0.0445 ns |  1.00 |    0.02 |
|                   |           |           |           |           |       |         |
| CustomILog        | 9         | 0.4279 ns | 0.0056 ns | 0.0049 ns |  0.16 |    0.00 |
| CustomILogSwitch  | 9         | 0.4211 ns | 0.0090 ns | 0.0084 ns |  0.16 |    0.00 |
| MathILog          | 9         | 4.3906 ns | 0.0306 ns | 0.0286 ns |  1.69 |    0.03 |
| MathLog10Baseline | 9         | 2.5940 ns | 0.0433 ns | 0.0384 ns |  1.00 |    0.02 |
|                   |           |           |           |           |       |         |
| CustomILog        | 9.999999  | 0.4190 ns | 0.0075 ns | 0.0070 ns |  0.16 |    0.00 |
| CustomILogSwitch  | 9.999999  | 0.4155 ns | 0.0107 ns | 0.0100 ns |  0.16 |    0.00 |
| MathILog          | 9.999999  | 4.3940 ns | 0.0381 ns | 0.0338 ns |  1.71 |    0.02 |
| MathLog10Baseline | 9.999999  | 2.5646 ns | 0.0390 ns | 0.0325 ns |  1.00 |    0.02 |
|                   |           |           |           |           |       |         |
| CustomILog        | 10        | 0.5797 ns | 0.0089 ns | 0.0083 ns |  0.23 |    0.01 |
| CustomILogSwitch  | 10        | 0.5860 ns | 0.0090 ns | 0.0080 ns |  0.23 |    0.01 |
| MathILog          | 10        | 4.3362 ns | 0.0283 ns | 0.0264 ns |  1.68 |    0.03 |
| MathLog10Baseline | 10        | 2.5773 ns | 0.0535 ns | 0.0501 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 10.01     | 0.5809 ns | 0.0117 ns | 0.0097 ns |  0.22 |    0.01 |
| CustomILogSwitch  | 10.01     | 0.5894 ns | 0.0114 ns | 0.0101 ns |  0.22 |    0.01 |
| MathILog          | 10.01     | 4.0444 ns | 0.0532 ns | 0.0471 ns |  1.53 |    0.04 |
| MathLog10Baseline | 10.01     | 2.6368 ns | 0.0698 ns | 0.0653 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 19        | 0.5851 ns | 0.0053 ns | 0.0050 ns |  0.22 |    0.00 |
| CustomILogSwitch  | 19        | 0.5848 ns | 0.0106 ns | 0.0094 ns |  0.22 |    0.01 |
| MathILog          | 19        | 4.3898 ns | 0.0501 ns | 0.0468 ns |  1.67 |    0.04 |
| MathLog10Baseline | 19        | 2.6355 ns | 0.0538 ns | 0.0503 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 99        | 0.5832 ns | 0.0107 ns | 0.0100 ns |  0.22 |    0.01 |
| CustomILogSwitch  | 99        | 0.5770 ns | 0.0114 ns | 0.0106 ns |  0.22 |    0.01 |
| MathILog          | 99        | 4.3744 ns | 0.0518 ns | 0.0485 ns |  1.68 |    0.04 |
| MathLog10Baseline | 99        | 2.6006 ns | 0.0667 ns | 0.0624 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 99.999    | 0.5887 ns | 0.0128 ns | 0.0120 ns |  0.21 |    0.01 |
| CustomILogSwitch  | 99.999    | 0.4225 ns | 0.0099 ns | 0.0077 ns |  0.15 |    0.00 |
| MathILog          | 99.999    | 4.4015 ns | 0.0288 ns | 0.0240 ns |  1.59 |    0.02 |
| MathLog10Baseline | 99.999    | 2.7734 ns | 0.0425 ns | 0.0377 ns |  1.00 |    0.02 |
|                   |           |           |           |           |       |         |
| CustomILog        | 100       | 0.7925 ns | 0.0162 ns | 0.0152 ns |  0.30 |    0.01 |
| CustomILogSwitch  | 100       | 0.7881 ns | 0.0178 ns | 0.0166 ns |  0.30 |    0.01 |
| MathILog          | 100       | 4.3885 ns | 0.0395 ns | 0.0370 ns |  1.68 |    0.04 |
| MathLog10Baseline | 100       | 2.6200 ns | 0.0661 ns | 0.0618 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 999       | 0.7900 ns | 0.0077 ns | 0.0072 ns |  0.30 |    0.01 |
| CustomILogSwitch  | 999       | 0.7884 ns | 0.0094 ns | 0.0088 ns |  0.30 |    0.01 |
| MathILog          | 999       | 4.4066 ns | 0.0492 ns | 0.0436 ns |  1.69 |    0.04 |
| MathLog10Baseline | 999       | 2.6143 ns | 0.0577 ns | 0.0540 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 1000      | 0.7913 ns | 0.0085 ns | 0.0075 ns |  0.31 |    0.01 |
| CustomILogSwitch  | 1000      | 0.7923 ns | 0.0052 ns | 0.0049 ns |  0.31 |    0.01 |
| MathILog          | 1000      | 4.4207 ns | 0.0349 ns | 0.0326 ns |  1.71 |    0.04 |
| MathLog10Baseline | 1000      | 2.5859 ns | 0.0554 ns | 0.0518 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 9999      | 0.7969 ns | 0.0090 ns | 0.0084 ns |  0.30 |    0.01 |
| CustomILogSwitch  | 9999      | 0.7923 ns | 0.0135 ns | 0.0126 ns |  0.30 |    0.01 |
| MathILog          | 9999      | 4.3353 ns | 0.0712 ns | 0.0666 ns |  1.66 |    0.04 |
| MathLog10Baseline | 9999      | 2.6197 ns | 0.0531 ns | 0.0497 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 10000     | 1.0071 ns | 0.0181 ns | 0.0169 ns |  0.39 |    0.01 |
| CustomILogSwitch  | 10000     | 0.9938 ns | 0.0125 ns | 0.0117 ns |  0.38 |    0.01 |
| MathILog          | 10000     | 4.3799 ns | 0.0343 ns | 0.0304 ns |  1.69 |    0.03 |
| MathLog10Baseline | 10000     | 2.5939 ns | 0.0529 ns | 0.0495 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 99999     | 1.0105 ns | 0.0153 ns | 0.0143 ns |  0.39 |    0.01 |
| CustomILogSwitch  | 99999     | 0.9988 ns | 0.0144 ns | 0.0135 ns |  0.38 |    0.01 |
| MathILog          | 99999     | 4.3223 ns | 0.0415 ns | 0.0388 ns |  1.65 |    0.03 |
| MathLog10Baseline | 99999     | 2.6128 ns | 0.0482 ns | 0.0451 ns |  1.00 |    0.02 |
|                   |           |           |           |           |       |         |
| CustomILog        | 100000    | 1.2101 ns | 0.0166 ns | 0.0155 ns |  0.47 |    0.01 |
| CustomILogSwitch  | 100000    | 1.2096 ns | 0.0155 ns | 0.0145 ns |  0.47 |    0.01 |
| MathILog          | 100000    | 4.3726 ns | 0.0530 ns | 0.0496 ns |  1.69 |    0.05 |
| MathLog10Baseline | 100000    | 2.5952 ns | 0.0701 ns | 0.0655 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 999999    | 1.2191 ns | 0.0106 ns | 0.0094 ns |  0.46 |    0.00 |
| CustomILogSwitch  | 999999    | 1.0410 ns | 0.0078 ns | 0.0073 ns |  0.40 |    0.00 |
| MathILog          | 999999    | 4.4191 ns | 0.0377 ns | 0.0334 ns |  1.68 |    0.02 |
| MathLog10Baseline | 999999    | 2.6313 ns | 0.0235 ns | 0.0208 ns |  1.00 |    0.01 |
|                   |           |           |           |           |       |         |
| CustomILog        | 1000000   | 1.2115 ns | 0.0127 ns | 0.0119 ns |  0.46 |    0.01 |
| CustomILogSwitch  | 1000000   | 1.2103 ns | 0.0171 ns | 0.0160 ns |  0.46 |    0.01 |
| MathILog          | 1000000   | 4.2019 ns | 0.0580 ns | 0.0542 ns |  1.60 |    0.03 |
| MathLog10Baseline | 1000000   | 2.6315 ns | 0.0493 ns | 0.0461 ns |  1.00 |    0.02 |
|                   |           |           |           |           |       |         |
| CustomILog        | 9999999   | 1.2130 ns | 0.0122 ns | 0.0114 ns |  0.46 |    0.01 |
| CustomILogSwitch  | 9999999   | 1.2156 ns | 0.0112 ns | 0.0105 ns |  0.46 |    0.01 |
| MathILog          | 9999999   | 4.3966 ns | 0.0615 ns | 0.0576 ns |  1.67 |    0.05 |
| MathLog10Baseline | 9999999   | 2.6325 ns | 0.0741 ns | 0.0693 ns |  1.00 |    0.04 |
|                   |           |           |           |           |       |         |
| CustomILog        | 10000000  | 1.4355 ns | 0.0135 ns | 0.0120 ns |  0.55 |    0.01 |
| CustomILogSwitch  | 10000000  | 1.4251 ns | 0.0103 ns | 0.0097 ns |  0.55 |    0.01 |
| MathILog          | 10000000  | 4.3885 ns | 0.0352 ns | 0.0312 ns |  1.68 |    0.03 |
| MathLog10Baseline | 10000000  | 2.6101 ns | 0.0412 ns | 0.0366 ns |  1.00 |    0.02 |
|                   |           |           |           |           |       |         |
| CustomILog        | 100000000 | 1.4292 ns | 0.0108 ns | 0.0101 ns |  0.54 |    0.01 |
| CustomILog        | 100000000 | 1.4239 ns | 0.0130 ns | 0.0121 ns |  0.54 |    0.01 |
| CustomILogSwitch  | 100000000 | 1.4210 ns | 0.0065 ns | 0.0061 ns |  0.54 |    0.01 |
| CustomILogSwitch  | 100000000 | 1.4294 ns | 0.0058 ns | 0.0055 ns |  0.54 |    0.01 |
| MathILog          | 100000000 | 4.7549 ns | 0.0590 ns | 0.0552 ns |  1.81 |    0.05 |
| MathILog          | 100000000 | 4.3987 ns | 0.0360 ns | 0.0337 ns |  1.67 |    0.04 |
| MathLog10Baseline | 100000000 | 2.6295 ns | 0.0648 ns | 0.0607 ns |  1.00 |    0.03 |
| MathLog10Baseline | 100000000 | 2.6224 ns | 0.0500 ns | 0.0467 ns |  1.00 |    0.03 |
|                   |           |           |           |           |       |         |
| CustomILog        | 1E+09     | 4.6831 ns | 0.0679 ns | 0.0635 ns |  1.80 |    0.05 |
| CustomILog        | 1E+09     | 4.9007 ns | 0.0702 ns | 0.0657 ns |  1.88 |    0.05 |
| CustomILogSwitch  | 1E+09     | 4.8617 ns | 0.0451 ns | 0.0422 ns |  1.87 |    0.05 |
| CustomILogSwitch  | 1E+09     | 4.8315 ns | 0.0332 ns | 0.0277 ns |  1.86 |    0.05 |
| MathILog          | 1E+09     | 4.3892 ns | 0.0266 ns | 0.0236 ns |  1.69 |    0.04 |
| MathILog          | 1E+09     | 4.4906 ns | 0.0443 ns | 0.0370 ns |  1.73 |    0.05 |
| MathLog10Baseline | 1E+09     | 2.6026 ns | 0.0734 ns | 0.0651 ns |  1.00 |    0.04 |
| MathLog10Baseline | 1E+09     | 2.5701 ns | 0.0731 ns | 0.1001 ns |  0.99 |    0.05 |
|                   |           |           |           |           |       |         |
| CustomILog        | 1E+10     | 4.8689 ns | 0.0547 ns | 0.0512 ns |  1.90 |    0.04 |
| CustomILogSwitch  | 1E+10     | 4.8355 ns | 0.0443 ns | 0.0415 ns |  1.89 |    0.04 |
| MathILog          | 1E+10     | 4.3857 ns | 0.0562 ns | 0.0526 ns |  1.71 |    0.04 |
| MathLog10Baseline | 1E+10     | 2.5642 ns | 0.0592 ns | 0.0524 ns |  1.00 |    0.03 |

Some values appear twice because the 9.9999...eN literals are rounded up to 1e(N+1) due to f32's lower precision around that exponent. Despite that, we can see that the custom ILog10 implementation still holds good performance, only a little worse than MathF.Log10 with conversion to int.

Now, after implementing support for numbers 0 < x < 1, and comparing against a non-clamped result of `Math.Log10`, the results look like this:

| Method              | Value      | Mean      | Error     | StdDev    | Ratio | RatioSD |
|-------------------- |----------- |----------:|----------:|----------:|------:|--------:|
| ILog                | 1          | 1.6410 ns | 0.0173 ns | 0.0153 ns |  0.41 |    0.00 |
| NonNegativeILog     | 1          | 0.3948 ns | 0.0217 ns | 0.0203 ns |  0.10 |    0.00 |
| MathNonNegativeILog | 1          | 6.4981 ns | 0.0538 ns | 0.0503 ns |  1.61 |    0.02 |
| MathILog            | 1          | 5.8298 ns | 0.0307 ns | 0.0288 ns |  1.45 |    0.01 |
| MathLog10Baseline   | 1          | 4.0327 ns | 0.0263 ns | 0.0246 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 10         | 1.8629 ns | 0.0136 ns | 0.0120 ns |  0.57 |    0.01 |
| NonNegativeILog     | 10         | 0.5886 ns | 0.0102 ns | 0.0095 ns |  0.18 |    0.00 |
| MathNonNegativeILog | 10         | 4.8480 ns | 0.0365 ns | 0.0341 ns |  1.49 |    0.02 |
| MathILog            | 10         | 4.8722 ns | 0.1137 ns | 0.1117 ns |  1.50 |    0.04 |
| MathLog10Baseline   | 10         | 3.2524 ns | 0.0383 ns | 0.0358 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 19         | 1.8866 ns | 0.0267 ns | 0.0250 ns |  0.57 |    0.01 |
| NonNegativeILog     | 19         | 0.6002 ns | 0.0096 ns | 0.0085 ns |  0.18 |    0.00 |
| MathNonNegativeILog | 19         | 4.9345 ns | 0.0564 ns | 0.0528 ns |  1.49 |    0.02 |
| MathILog            | 19         | 4.7593 ns | 0.0330 ns | 0.0276 ns |  1.44 |    0.02 |
| MathLog10Baseline   | 19         | 3.3029 ns | 0.0338 ns | 0.0316 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 100        | 2.0606 ns | 0.0244 ns | 0.0228 ns |  0.62 |    0.01 |
| NonNegativeILog     | 100        | 0.8104 ns | 0.0051 ns | 0.0043 ns |  0.24 |    0.00 |
| MathNonNegativeILog | 100        | 5.1323 ns | 0.0501 ns | 0.0444 ns |  1.55 |    0.02 |
| MathILog            | 100        | 4.8070 ns | 0.0410 ns | 0.0342 ns |  1.45 |    0.01 |
| MathLog10Baseline   | 100        | 3.3130 ns | 0.0214 ns | 0.0200 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 1000       | 2.1968 ns | 0.0291 ns | 0.0258 ns |  0.67 |    0.01 |
| NonNegativeILog     | 1000       | 0.8027 ns | 0.0134 ns | 0.0126 ns |  0.24 |    0.00 |
| MathNonNegativeILog | 1000       | 5.1405 ns | 0.0892 ns | 0.0834 ns |  1.57 |    0.03 |
| MathILog            | 1000       | 4.7635 ns | 0.0381 ns | 0.0338 ns |  1.45 |    0.02 |
| MathLog10Baseline   | 1000       | 3.2818 ns | 0.0364 ns | 0.0340 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 10000      | 2.3441 ns | 0.0195 ns | 0.0173 ns |  0.70 |    0.01 |
| NonNegativeILog     | 10000      | 1.0200 ns | 0.0154 ns | 0.0144 ns |  0.30 |    0.01 |
| MathNonNegativeILog | 10000      | 5.0943 ns | 0.0460 ns | 0.0408 ns |  1.52 |    0.02 |
| MathILog            | 10000      | 4.5625 ns | 0.0517 ns | 0.0484 ns |  1.36 |    0.02 |
| MathLog10Baseline   | 10000      | 3.3495 ns | 0.0386 ns | 0.0361 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 10000000   | 3.0702 ns | 0.0158 ns | 0.0140 ns |  0.93 |    0.02 |
| NonNegativeILog     | 10000000   | 1.4531 ns | 0.0143 ns | 0.0134 ns |  0.44 |    0.01 |
| MathNonNegativeILog | 10000000   | 4.9622 ns | 0.0567 ns | 0.0531 ns |  1.51 |    0.03 |
| MathILog            | 10000000   | 4.7018 ns | 0.0519 ns | 0.0485 ns |  1.43 |    0.03 |
| MathLog10Baseline   | 10000000   | 3.2895 ns | 0.0650 ns | 0.0608 ns |  1.00 |    0.03 |
|                     |            |           |           |           |       |         |
| ILog                | 100000000  | 3.0447 ns | 0.0238 ns | 0.0223 ns |  0.93 |    0.01 |
| NonNegativeILog     | 100000000  | 1.4380 ns | 0.0223 ns | 0.0197 ns |  0.44 |    0.01 |
| MathNonNegativeILog | 100000000  | 5.1542 ns | 0.0419 ns | 0.0371 ns |  1.57 |    0.01 |
| MathILog            | 100000000  | 4.8451 ns | 0.0275 ns | 0.0244 ns |  1.48 |    0.01 |
| MathLog10Baseline   | 100000000  | 3.2829 ns | 0.0263 ns | 0.0220 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 1E-09      | 5.4122 ns | 0.0586 ns | 0.0520 ns |  1.65 |    0.02 |
| NonNegativeILog     | 1E-09      | 0.3889 ns | 0.0126 ns | 0.0105 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 1E-09      | 5.1105 ns | 0.0743 ns | 0.0659 ns |  1.56 |    0.02 |
| MathILog            | 1E-09      | 4.7777 ns | 0.0482 ns | 0.0427 ns |  1.46 |    0.02 |
| MathLog10Baseline   | 1E-09      | 3.2762 ns | 0.0324 ns | 0.0303 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 1E-08      | 0.5925 ns | 0.0064 ns | 0.0056 ns |  0.18 |    0.00 |
| NonNegativeILog     | 1E-08      | 0.3890 ns | 0.0056 ns | 0.0053 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 1E-08      | 4.8245 ns | 0.0729 ns | 0.0646 ns |  1.47 |    0.02 |
| MathILog            | 1E-08      | 4.7081 ns | 0.0358 ns | 0.0334 ns |  1.43 |    0.01 |
| MathLog10Baseline   | 1E-08      | 3.2900 ns | 0.0267 ns | 0.0250 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 1E-07      | 0.8042 ns | 0.0130 ns | 0.0115 ns |  0.24 |    0.00 |
| NonNegativeILog     | 1E-07      | 0.3930 ns | 0.0059 ns | 0.0049 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 1E-07      | 5.1386 ns | 0.0592 ns | 0.0525 ns |  1.56 |    0.02 |
| MathILog            | 1E-07      | 4.7283 ns | 0.0375 ns | 0.0351 ns |  1.44 |    0.01 |
| MathLog10Baseline   | 1E-07      | 3.2878 ns | 0.0251 ns | 0.0223 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 1E-06      | 0.7978 ns | 0.0128 ns | 0.0119 ns |  0.24 |    0.00 |
| NonNegativeILog     | 1E-06      | 0.3907 ns | 0.0103 ns | 0.0096 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 1E-06      | 5.0511 ns | 0.0697 ns | 0.0652 ns |  1.55 |    0.03 |
| MathILog            | 1E-06      | 4.7393 ns | 0.0384 ns | 0.0321 ns |  1.45 |    0.02 |
| MathLog10Baseline   | 1E-06      | 3.2599 ns | 0.0439 ns | 0.0410 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 1E-05      | 1.0119 ns | 0.0157 ns | 0.0139 ns |  0.31 |    0.01 |
| NonNegativeILog     | 1E-05      | 0.3765 ns | 0.0089 ns | 0.0083 ns |  0.11 |    0.00 |
| MathNonNegativeILog | 1E-05      | 5.1514 ns | 0.0628 ns | 0.0556 ns |  1.57 |    0.03 |
| MathILog            | 1E-05      | 4.7460 ns | 0.0650 ns | 0.0576 ns |  1.45 |    0.03 |
| MathLog10Baseline   | 1E-05      | 3.2795 ns | 0.0498 ns | 0.0466 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 0.0001     | 1.2190 ns | 0.0157 ns | 0.0147 ns |  0.36 |    0.01 |
| NonNegativeILog     | 0.0001     | 0.3840 ns | 0.0079 ns | 0.0074 ns |  0.11 |    0.00 |
| MathNonNegativeILog | 0.0001     | 5.1268 ns | 0.0439 ns | 0.0411 ns |  1.53 |    0.03 |
| MathILog            | 0.0001     | 4.8041 ns | 0.0448 ns | 0.0374 ns |  1.43 |    0.03 |
| MathLog10Baseline   | 0.0001     | 3.3590 ns | 0.0782 ns | 0.0731 ns |  1.00 |    0.03 |
|                     |            |           |           |           |       |         |
| ILog                | 0.001      | 1.2255 ns | 0.0226 ns | 0.0200 ns |  0.37 |    0.01 |
| NonNegativeILog     | 0.001      | 0.3904 ns | 0.0096 ns | 0.0090 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 0.001      | 5.1345 ns | 0.0773 ns | 0.0723 ns |  1.56 |    0.02 |
| MathILog            | 0.001      | 4.7539 ns | 0.0668 ns | 0.0558 ns |  1.45 |    0.02 |
| MathLog10Baseline   | 0.001      | 3.2815 ns | 0.0300 ns | 0.0281 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 0.01       | 1.4380 ns | 0.0116 ns | 0.0108 ns |  0.44 |    0.00 |
| NonNegativeILog     | 0.01       | 0.3778 ns | 0.0097 ns | 0.0091 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 0.01       | 4.9916 ns | 0.0364 ns | 0.0341 ns |  1.52 |    0.01 |
| MathILog            | 0.01       | 4.7419 ns | 0.0381 ns | 0.0357 ns |  1.45 |    0.01 |
| MathLog10Baseline   | 0.01       | 3.2784 ns | 0.0260 ns | 0.0243 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 0.1        | 1.6374 ns | 0.0181 ns | 0.0151 ns |  0.50 |    0.01 |
| NonNegativeILog     | 0.1        | 0.3848 ns | 0.0103 ns | 0.0097 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 0.1        | 4.9850 ns | 0.0484 ns | 0.0404 ns |  1.52 |    0.02 |
| MathILog            | 0.1        | 4.6468 ns | 0.0403 ns | 0.0377 ns |  1.42 |    0.02 |
| MathLog10Baseline   | 0.1        | 3.2701 ns | 0.0288 ns | 0.0269 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 9          | 1.6442 ns | 0.0156 ns | 0.0146 ns |  0.50 |    0.01 |
| NonNegativeILog     | 9          | 0.3862 ns | 0.0097 ns | 0.0090 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 9          | 5.2664 ns | 0.0596 ns | 0.0558 ns |  1.59 |    0.03 |
| MathILog            | 9          | 4.5575 ns | 0.0630 ns | 0.0589 ns |  1.37 |    0.03 |
| MathLog10Baseline   | 9          | 3.3174 ns | 0.0523 ns | 0.0489 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 9.999999   | 1.6304 ns | 0.0162 ns | 0.0151 ns |  0.50 |    0.01 |
| NonNegativeILog     | 9.999999   | 0.3905 ns | 0.0088 ns | 0.0082 ns |  0.12 |    0.00 |
| MathNonNegativeILog | 9.999999   | 5.1413 ns | 0.0620 ns | 0.0550 ns |  1.57 |    0.03 |
| MathILog            | 9.999999   | 4.6745 ns | 0.0289 ns | 0.0270 ns |  1.43 |    0.02 |
| MathLog10Baseline   | 9.999999   | 3.2761 ns | 0.0596 ns | 0.0528 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 99.999     | 1.8778 ns | 0.0170 ns | 0.0151 ns |  0.57 |    0.01 |
| NonNegativeILog     | 99.999     | 0.5957 ns | 0.0106 ns | 0.0094 ns |  0.18 |    0.00 |
| MathNonNegativeILog | 99.999     | 4.9485 ns | 0.0384 ns | 0.0341 ns |  1.50 |    0.02 |
| MathILog            | 99.999     | 4.7533 ns | 0.0309 ns | 0.0289 ns |  1.44 |    0.02 |
| MathLog10Baseline   | 99.999     | 3.3062 ns | 0.0385 ns | 0.0360 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 999        | 2.0793 ns | 0.0171 ns | 0.0152 ns |  0.63 |    0.01 |
| NonNegativeILog     | 999        | 0.8136 ns | 0.0236 ns | 0.0209 ns |  0.25 |    0.01 |
| MathNonNegativeILog | 999        | 4.9005 ns | 0.0439 ns | 0.0366 ns |  1.49 |    0.02 |
| MathILog            | 999        | 4.7804 ns | 0.0305 ns | 0.0270 ns |  1.45 |    0.02 |
| MathLog10Baseline   | 999        | 3.2950 ns | 0.0393 ns | 0.0368 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 9999       | 2.2302 ns | 0.0373 ns | 0.0349 ns |  0.59 |    0.01 |
| NonNegativeILog     | 9999       | 0.8207 ns | 0.0189 ns | 0.0177 ns |  0.22 |    0.00 |
| MathNonNegativeILog | 9999       | 5.0784 ns | 0.0707 ns | 0.0661 ns |  1.34 |    0.02 |
| MathILog            | 9999       | 4.5804 ns | 0.0522 ns | 0.0436 ns |  1.20 |    0.01 |
| MathLog10Baseline   | 9999       | 3.8027 ns | 0.0348 ns | 0.0326 ns |  1.00 |    0.01 |
|                     |            |           |           |           |       |         |
| ILog                | 99999      | 2.3471 ns | 0.0237 ns | 0.0210 ns |  0.72 |    0.01 |
| NonNegativeILog     | 99999      | 1.0200 ns | 0.0232 ns | 0.0217 ns |  0.31 |    0.01 |
| MathNonNegativeILog | 99999      | 5.1244 ns | 0.0650 ns | 0.0608 ns |  1.58 |    0.03 |
| MathILog            | 99999      | 4.7248 ns | 0.0362 ns | 0.0338 ns |  1.45 |    0.02 |
| MathLog10Baseline   | 99999      | 3.2490 ns | 0.0509 ns | 0.0476 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 999999     | 2.6192 ns | 0.0159 ns | 0.0133 ns |  0.78 |    0.01 |
| NonNegativeILog     | 999999     | 1.2192 ns | 0.0100 ns | 0.0094 ns |  0.36 |    0.01 |
| MathNonNegativeILog | 999999     | 4.8863 ns | 0.0455 ns | 0.0425 ns |  1.46 |    0.03 |
| MathILog            | 999999     | 4.7484 ns | 0.0478 ns | 0.0447 ns |  1.42 |    0.03 |
| MathLog10Baseline   | 999999     | 3.3528 ns | 0.0598 ns | 0.0560 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 9999999    | 2.8062 ns | 0.0284 ns | 0.0265 ns |  0.85 |    0.01 |
| NonNegativeILog     | 9999999    | 1.2275 ns | 0.0109 ns | 0.0102 ns |  0.37 |    0.01 |
| MathNonNegativeILog | 9999999    | 4.9910 ns | 0.0917 ns | 0.0857 ns |  1.51 |    0.03 |
| MathILog            | 9999999    | 4.7788 ns | 0.0663 ns | 0.0620 ns |  1.45 |    0.02 |
| MathLog10Baseline   | 9999999    | 3.2992 ns | 0.0476 ns | 0.0398 ns |  1.00 |    0.02 |
|                     |            |           |           |           |       |         |
| ILog                | 9999999999 | 5.3478 ns | 0.0728 ns | 0.0681 ns |  1.64 |    0.02 |
| NonNegativeILog     | 9999999999 | 5.5832 ns | 0.0239 ns | 0.0199 ns |  1.71 |    0.01 |
| MathNonNegativeILog | 9999999999 | 5.0179 ns | 0.0492 ns | 0.0436 ns |  1.54 |    0.01 |
| MathILog            | 9999999999 | 4.7398 ns | 0.0180 ns | 0.0160 ns |  1.45 |    0.01 |
| MathLog10Baseline   | 9999999999 | 3.2639 ns | 0.0168 ns | 0.0148 ns |  1.00 |    0.01 |

For all exponents of -8 <= e <= 8, `ILog` is faster than using `Math.Log`, with varying success at that. For the above benchmark, this order was used:
```csharp
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
```

Just a single comparison against 1 at the start can help speed things up drastically for almost all cases. Let's implement that. Before you see the code below, please pretend that this is normal-looking code:

```csharp
public static int ILogSplit(double value)
{
	const double fastThreshold = 1e9;
	return value is 0 ? int.MinValue
		: value < 1
			? (value >= fastThreshold ? MathILog(value)
				: value < 1e1 ? 0
				: value < 1e2 ? 1
				: value < 1e3 ? 2
				: value < 1e4 ? 3
				: value < 1e5 ? 4
				: value < 1e6 ? 5
				: value < 1e7 ? 6
				: value < 1e8 ? 7
				: 8)

			: (value < 1e-8 ? MathILog(value)
				: value < 1e-7 ? -8
				: value < 1e-6 ? -7
				: value < 1e-5 ? -6
				: value < 1e-4 ? -5
				: value < 1e-3 ? -4
				: value < 1e-2 ? -3
				: value < 1e-1 ? -2
				: -1)
		;
}
```

Indeed the results speak for themselves:

| Method    | Value      | Mean      | Error     | StdDev    | Ratio | RatioSD |
|---------- |----------- |----------:|----------:|----------:|------:|--------:|
| ILog      | 0.0001     | 1.2115 ns | 0.0236 ns | 0.0221 ns |  1.00 |    0.02 |
| ILogSplit | 0.0001     | 1.2027 ns | 0.0117 ns | 0.0104 ns |  0.99 |    0.02 |
|           |            |           |           |           |       |         |
| ILog      | 0.001      | 1.2469 ns | 0.0182 ns | 0.0170 ns |  1.00 |    0.02 |
| ILogSplit | 0.001      | 1.4365 ns | 0.0221 ns | 0.0196 ns |  1.15 |    0.02 |
|           |            |           |           |           |       |         |
| ILog      | 0.01       | 1.4296 ns | 0.0151 ns | 0.0141 ns |  1.00 |    0.01 |
| ILogSplit | 0.01       | 1.4362 ns | 0.0247 ns | 0.0231 ns |  1.00 |    0.02 |
|           |            |           |           |           |       |         |
| ILog      | 0.1        | 1.6327 ns | 0.0203 ns | 0.0190 ns |  1.00 |    0.02 |
| ILogSplit | 0.1        | 1.4338 ns | 0.0186 ns | 0.0174 ns |  0.88 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 1          | 1.6522 ns | 0.0264 ns | 0.0234 ns |  1.00 |    0.02 |
| ILogSplit | 1          | 0.5742 ns | 0.0100 ns | 0.0078 ns |  0.35 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 9          | 1.6297 ns | 0.0171 ns | 0.0160 ns |  1.00 |    0.01 |
| ILogSplit | 9          | 0.5755 ns | 0.0097 ns | 0.0091 ns |  0.35 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 10         | 1.8733 ns | 0.0151 ns | 0.0134 ns |  1.00 |    0.01 |
| ILogSplit | 10         | 0.7903 ns | 0.0158 ns | 0.0140 ns |  0.42 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 19         | 1.8638 ns | 0.0213 ns | 0.0189 ns |  1.00 |    0.01 |
| ILogSplit | 19         | 0.8000 ns | 0.0166 ns | 0.0155 ns |  0.43 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 100        | 2.0685 ns | 0.0170 ns | 0.0133 ns |  1.00 |    0.01 |
| ILogSplit | 100        | 1.0091 ns | 0.0227 ns | 0.0212 ns |  0.49 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 999        | 2.0726 ns | 0.0222 ns | 0.0196 ns |  1.00 |    0.01 |
| ILogSplit | 999        | 1.0003 ns | 0.0149 ns | 0.0139 ns |  0.48 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 1000       | 2.2209 ns | 0.0401 ns | 0.0355 ns |  1.00 |    0.02 |
| ILogSplit | 1000       | 0.9968 ns | 0.0174 ns | 0.0163 ns |  0.45 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 9999       | 2.0200 ns | 0.0308 ns | 0.0288 ns |  1.00 |    0.02 |
| ILogSplit | 9999       | 1.0028 ns | 0.0169 ns | 0.0158 ns |  0.50 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 10000      | 2.3330 ns | 0.0368 ns | 0.0344 ns |  1.00 |    0.02 |
| ILogSplit | 10000      | 1.2119 ns | 0.0123 ns | 0.0109 ns |  0.52 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 99999      | 2.3399 ns | 0.0280 ns | 0.0248 ns |  1.00 |    0.01 |
| ILogSplit | 99999      | 1.2110 ns | 0.0273 ns | 0.0255 ns |  0.52 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 999999     | 2.6076 ns | 0.0249 ns | 0.0233 ns |  1.00 |    0.01 |
| ILogSplit | 999999     | 1.4231 ns | 0.0180 ns | 0.0168 ns |  0.55 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 9999999    | 2.8200 ns | 0.0255 ns | 0.0226 ns |  1.00 |    0.01 |
| ILogSplit | 9999999    | 1.4472 ns | 0.0266 ns | 0.0249 ns |  0.51 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 10000000   | 3.0759 ns | 0.0191 ns | 0.0169 ns |  1.00 |    0.01 |
| ILogSplit | 10000000   | 1.6364 ns | 0.0175 ns | 0.0146 ns |  0.53 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 100000000  | 3.0548 ns | 0.0299 ns | 0.0279 ns |  1.00 |    0.01 |
| ILogSplit | 100000000  | 1.6500 ns | 0.0206 ns | 0.0192 ns |  0.54 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 1E-09      | 5.4816 ns | 0.0573 ns | 0.0508 ns |  1.00 |    0.01 |
| ILogSplit | 1E-09      | 5.1606 ns | 0.0462 ns | 0.0433 ns |  0.94 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 1E-08      | 0.5793 ns | 0.0110 ns | 0.0103 ns |  1.00 |    0.02 |
| ILogSplit | 1E-08      | 0.5904 ns | 0.0129 ns | 0.0121 ns |  1.02 |    0.03 |
|           |            |           |           |           |       |         |
| ILog      | 1E-07      | 0.7914 ns | 0.0090 ns | 0.0084 ns |  1.00 |    0.01 |
| ILogSplit | 1E-07      | 0.7954 ns | 0.0059 ns | 0.0050 ns |  1.01 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 1E-06      | 0.7926 ns | 0.0117 ns | 0.0098 ns |  1.00 |    0.02 |
| ILogSplit | 1E-06      | 1.0010 ns | 0.0070 ns | 0.0062 ns |  1.26 |    0.02 |
|           |            |           |           |           |       |         |
| ILog      | 1E-05      | 1.0044 ns | 0.0170 ns | 0.0159 ns |  1.00 |    0.02 |
| ILogSplit | 1E-05      | 1.0036 ns | 0.0164 ns | 0.0153 ns |  1.00 |    0.02 |
|           |            |           |           |           |       |         |
| ILog      | 9.999999   | 1.6867 ns | 0.0163 ns | 0.0145 ns |  1.00 |    0.01 |
| ILogSplit | 9.999999   | 0.5753 ns | 0.0083 ns | 0.0078 ns |  0.34 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 99.999     | 1.8476 ns | 0.0264 ns | 0.0247 ns |  1.00 |    0.02 |
| ILogSplit | 99.999     | 0.7834 ns | 0.0144 ns | 0.0135 ns |  0.42 |    0.01 |
|           |            |           |           |           |       |         |
| ILog      | 9999999999 | 4.9146 ns | 0.0408 ns | 0.0382 ns |  1.00 |    0.01 |
| ILogSplit | 9999999999 | 5.6706 ns | 0.0367 ns | 0.0343 ns |  1.15 |    0.01 |

For almost every single case, branching from a comparison with 1 is much faster. The only real outlier is 1e-6, which is *vastly* faster in ILog by 0.2 full nanoseconds. Nevertheless, we can trim off exponents that make this approach slower than using `Math.Log10` with more elaborate testing, which falls out of scope of this article.

And this concludes the basic research on implementing `ILog10` on f32 and f64. It's quite possible that we can implement a magic-number-based solution like it was done with integers as shown earlier. And it's quite more possible that it would be faster, again.

## Conclusion

For integers of the standard sizes of 8, 16, 32 and 64 bits, we found that the .NET STL implements commonly-known implementations derived from C, which is still a software solution. Its performance is possibly the fastest without dedicated hardware instructions for this common operation. For floating-points of sizes 32 and 64 bits no well-known implementations were found that quickly compute the base-10 logarithm of any number, like the notable example of [Fast Inverse Square Root](https://en.wikipedia.org/wiki/Fast_inverse_square_root). For the purposes of this article a very trivial implementation with a cluster of ternary operations was used to show that there is room for improvement again with just software implementations. No existing hardware instructions in x86-64 or ARM could help with this.

Similar lookup-based tricks can be employed for the floating-point types too. The binary exponent is easily retrievable, and from that point it's possible to create a lookup table mapping the possible values of the exponent and the magic numbers to transform the mantissa into the base-10 logarithm of the value. It should not take too long to discover, hopefully.