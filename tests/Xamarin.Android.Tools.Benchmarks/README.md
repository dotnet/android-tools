```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
Intel Core i9-14900KF, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.626.17701), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.6 (10.0.626.17701), X64 RyuJIT AVX2


```
| Method         | Mean        | Error     | StdDev    | Allocated |
|--------------- |------------:|----------:|----------:|----------:|
| HashBytes      |   485.90 μs |  5.514 μs |  5.158 μs |     232 B |
| HashStream     |   471.47 μs |  3.284 μs |  3.072 μs |     232 B |
| HashFile       |    16.11 μs |  0.104 μs |  0.098 μs |     472 B |
| HasFileChanged | 1,477.77 μs | 18.336 μs | 17.152 μs |     945 B |
