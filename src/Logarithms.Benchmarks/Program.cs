﻿using BenchmarkDotNet.Running;
using Logarithms.Benchmarks;

#if DEBUG
#error WHAT'YOU GONNA RUN MY BOI?
#endif

BenchmarkRunner.Run<UInt64RandomDigitCountBenchmarks>();
