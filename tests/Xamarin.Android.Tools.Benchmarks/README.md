```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
Intel Core i9-14900KF, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.626.17701), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.6 (10.0.626.17701), X64 RyuJIT AVX2


```
| Method         | Mean       | Error    | StdDev   | Allocated |
|--------------- |-----------:|---------:|---------:|----------:|
| HashBytes      |   475.2 μs |  2.50 μs |  2.21 μs |     232 B |
| HashStream     |   458.9 μs |  0.85 μs |  0.71 μs |     232 B |
| HashFile       |   732.6 μs | 14.20 μs | 15.20 μs |     472 B |
| HasFileChanged | 1,434.4 μs |  5.51 μs |  4.60 μs |     945 B |
