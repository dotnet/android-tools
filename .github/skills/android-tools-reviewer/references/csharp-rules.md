# C# Review Rules

General C# and .NET guidance for code reviews. Applicable to any .NET repository.

---

## 1. Target Framework Compatibility

When a library multi-targets (e.g., `netstandard2.0` + a modern TFM), every API call must work on all targets.

| Check | What to look for |
|-------|-----------------|
| **netstandard2.0 API surface** | Methods/overloads that only exist on net5+. Common traps: `HttpContent.ReadAsStringAsync(CancellationToken)`, `ProcessStartInfo.ArgumentList`, `Environment.IsPrivilegedProcess`, `ArrayPool<T>` (needs `System.Buffers` package on ns2.0). When unsure, check MS Learn docs. |
| **C# language features** | `init` accessors, `required` keyword, file-scoped types, raw string literals — may need polyfills or `#if` guards. |
| **Conditional compilation** | New API usage should be behind `#if NET5_0_OR_GREATER` (or similar) with a fallback for netstandard2.0. |

---

## 2. Async & Cancellation Patterns

| Check | What to look for |
|-------|-----------------|
| **CancellationToken propagation** | Every `async` method that accepts a `CancellationToken` must pass it to ALL downstream async calls (`GetAsync`, `ReadAsStreamAsync`, `SendAsync`, etc.). A token that's accepted but never used is a broken contract. |
| **OperationCanceledException** | Catch-all blocks (`catch (Exception)`) must NOT swallow `OperationCanceledException`. Either catch it explicitly first and rethrow, or use a type filter. |
| **GetStringAsync** | On netstandard2.0, `GetStringAsync(url)` doesn't accept a `CancellationToken`. Use `GetAsync(url, ct)` + `ReadAsStringAsync()` instead. |
| **Thread safety of shared state** | If a new field or property can be accessed from multiple threads (e.g., static caches, event handlers), verify thread-safe access: `ConcurrentDictionary`, `Interlocked`, or explicit locks. A `Dictionary<K,V>` read concurrently with a write is undefined behavior. |

---

## 3. Resource Management

| Check | What to look for |
|-------|-----------------|
| **HttpClient must be static** | `HttpClient` instances should be `static readonly` fields, not per-instance. Creating/disposing `HttpClient` leads to socket exhaustion via `TIME_WAIT` accumulation. See [Microsoft guidelines](https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines). |
| **ArrayPool for large buffers** | Buffers ≥ 1KB (especially 80KB+ download buffers) should use `ArrayPool<byte>.Shared.Rent()` with `try/finally` return. Large allocations go to the LOH and are expensive to GC. |
| **IDisposable** | Classes that own unmanaged resources or expensive managed resources must implement `IDisposable` with a dispose guard (`ThrowIfDisposed`). |

---

## 4. Error Handling

| Check | What to look for |
|-------|-----------------|
| **No empty catch blocks** | Every `catch` must capture the `Exception` and log it (or rethrow). No silent swallowing. Even "expected" exceptions should be logged for diagnostics. |
| **Validate parameters** | Enum parameters and string-typed "mode" values must be validated — throw `ArgumentException` or `NotSupportedException` for unexpected values. Don't silently accept garbage. |
| **Fail fast on critical ops** | If an operation like `chmod` or checksum verification fails, throw immediately. Silently continuing leads to confusing downstream failures. |
| **Mandatory verification** | Checksum/hash verification must NOT be optional. If the checksum can't be fetched, the operation must fail — not proceed unverified. |
| **Include actionable details in exceptions** | Use `nameof` for parameter names. Include the unsupported value or unexpected type. Never throw empty exceptions. |
| **Use `ThrowIf` helpers where available** | In .NET 7+ projects, prefer `ArgumentOutOfRangeException.ThrowIfNegative`, `ArgumentNullException.ThrowIfNull`, etc. over manual if-then-throw patterns. In `netstandard2.0` projects where these helpers are unavailable, use explicit checks. |
| **Challenge exception swallowing** | When a PR adds `catch { continue; }` or `catch { return null; }`, question whether the exception is truly expected or masking a deeper problem. The default should be to let unexpected exceptions propagate. |

---

## 5. Performance

| Check | What to look for |
|-------|-----------------|
| **XmlReader over LINQ XML** | For forward-only XML parsing (manifests, config files), use `XmlReader` — it's streaming and allocation-free. `XElement`/`XDocument` builds a full DOM tree. |
| **p/invoke over process spawn** | For single syscalls like `chmod`, use `[DllImport("libc")]` instead of spawning a child process. Process creation is orders of magnitude more expensive. |
| **Avoid intermediate collections** | Don't create two lists and `AddRange()` one to the other. Build a single list, or use LINQ to chain. |
| **Cache reusable arrays** | Char arrays for `string.Split()` (like whitespace chars) should be `static readonly` fields, not allocated on each call. |
| **Pre-allocate collections when size is known** | Use `new List<T>(capacity)` or `new Dictionary<TK, TV>(count)` when the size is known or estimable. Repeated resizing is O(n) allocation waste. |
| **Avoid closures in hot paths** | Lambdas that capture local variables allocate a closure object on every call. In loops or frequently-called methods, extract the lambda to a static method or cache the delegate. |
| **Place cheap checks before expensive ones** | In validation chains, test simple conditions (null checks, boolean flags) before allocating strings or doing I/O. Short-circuit with `&&`/`||`. |
| **Watch for O(n²)** | Nested loops over the same or related collections, repeated `.Contains()` on a `List<T>`, or LINQ `.Where()` inside a loop are O(n²). Switch to `HashSet<T>` or `Dictionary<TK, TV>` for lookups. |

---

## 6. Code Organization

| Check | What to look for |
|-------|-----------------|
| **File-scoped namespaces** | New files should use `namespace Foo;` (not `namespace Foo { ... }`). Don't reformat existing files. |
| **Use `record` for data types** | Immutable data-carrier types (progress, version info, license info) should be `record` types. They get value equality, `ToString()`, and deconstruction for free. |
| **Remove unused code** | Dead methods, speculative helpers, and code "for later" should be removed. Ship only what's needed. |
| **No commented-out code** | Commented-out code should be deleted — Git has history. |
| **Reduce indentation with early returns** | Invert conditions with `continue` or early `return` to reduce nesting. `foreach (var x in items ?? Array.Empty<T>())` eliminates null-check nesting. |
| **Don't initialize fields to default values** | `bool flag = false;` and `int count = 0;` are noise. The CLR zero-initializes all fields. Only assign when the initial value is non-default. |

---

## 7. API Design

| Check | What to look for |
|-------|-----------------|
| **Return `IReadOnlyList<T>` not `List<T>`** | Public methods should return `IReadOnlyList<T>` (or `IReadOnlyCollection<T>`) instead of mutable `List<T>`. Prevents callers from mutating internal state. |
| **New helpers default to `internal`** | New utility methods should be `internal` unless a confirmed external consumer needs them. Use `InternalsVisibleTo` for test access. |
| **Structured args, not string interpolation** | Additional arguments to processes should be `IEnumerable<string>`, not a single `string` that gets interpolated. |
| **Honor `CancellationToken`** | If a method accepts a `CancellationToken`, it MUST observe it — register a callback to kill processes, check `IsCancellationRequested` in loops, pass it to downstream async calls. Don't just accept it for API completeness. |
| **Add overloads to reduce caller ceremony** | If every caller performs the same conversion before calling a method, the method should have an overload that accepts the unconverted type directly. |
| **Prefer C# pattern matching** | Use `is`, `switch` expressions, and property patterns instead of `if`/`else` type-check chains. Pattern matching is more concise, avoids casts, and enables exhaustiveness checks. |
