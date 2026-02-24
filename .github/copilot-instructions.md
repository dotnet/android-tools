# C# Development

## Project-Specific Context

This is the **Xamarin.Android.Tools** library - tools for interacting with the Android SDK:
- Multi-targets: `netstandard2.0` and `$(DotNetTargetFramework)` (currently `net9.0`)
- **`netstandard2.0` is required** — this package is consumed by .NET Framework hosts (e.g., Visual Studio on Windows). All public API and core logic **must** compile against netstandard2.0. Use `#if NET5_0_OR_GREATER` guards for newer runtime features (e.g., `ArrayPool`, `Process.Kill(true)`)
- C# Language Version: `LangVersion=latest` — but **feature usage is constrained by `netstandard2.0`**; use only C# features that compile for all targets, with conditional compilation for newer features where needed
- Nullable reference types are **enabled**
- Uses SDK-style project format
- Built with MSBuild

## C# Instructions
- Use modern C# language features **where they are compatible with `netstandard2.0`**; use conditional compilation for target-specific enhancements
- Use **block-scoped namespaces** (`namespace Xamarin.Android.Tools { }`) to match existing codebase convention
- For JSON parsing, follow the existing patterns used in this repository (if `System.Text.Json` source generators are added in this project, prefer them)
- Write clear and concise comments for each function, especially for public APIs

## General Instructions
- Make only high confidence suggestions when reviewing code changes.
- Write code with good maintainability practices, including comments on why certain design decisions were made.
- Handle edge cases and write clear exception handling.

## Formatting

- Apply code-formatting style defined in `.editorconfig`
- Use **tabs** for indentation in C# files (as per `.editorconfig`)
- Use **block-scoped namespaces** (`namespace Xamarin.Android.Tools { }`) to match existing codebase convention
- Insert a newline before the opening brace of **methods and types only** (per `.editorconfig`: `csharp_new_line_before_open_brace = methods,types`)
- Ensure that the final return statement of a method is on its own line
- Use pattern matching and switch expressions wherever possible
- Use `nameof` instead of string literals when referring to member names
- Ensure that XML doc comments are created for any public APIs. When applicable, include `<example>` and `<code>` documentation in the comments

## File Organization

- **One type per file**: Each public type (class, struct, enum, interface, record) should be in its own file
- **File naming**: File name should match the type name (e.g., `SdkManager.cs` for `class SdkManager`)
- **Nested types exception**: Small, tightly-coupled nested types can remain in the parent type's file
- **File size guideline**: Keep files under ~500 lines when practical; consider splitting larger files by responsibility

## Project Setup and Structure

- This project uses `Directory.Build.props` and `Directory.Build.targets` for shared configuration
- Output paths are customized: `$(ToolOutputFullPath)`, `$(BuildToolOutputFullPath)`, `$(TestOutputFullPath)`
- Multi-targeting is supported via `$(AndroidToolsDisableMultiTargeting)` flag
- Tests follow the pattern: `[ProjectName]-Tests` (e.g., `Xamarin.Android.Tools.AndroidSdk-Tests`)
- Uses Azure Pipelines for CI/CD (see `azure-pipelines.yaml`)

## Nullable Reference Types

- Nullable reference types are **ENABLED** in this project (`<Nullable>enable</Nullable>`)
- Declare variables non-nullable, and check for `null` at entry points
- Always use `is null` or `is not null` instead of `== null` or `!= null`
- Trust the C# null annotations and don't add null checks when the type system says a value cannot be null
- For netstandard2.0 target, uses `INTERNAL_NULLABLE_ATTRIBUTES` to polyfill nullable annotations
- For nullable warnings with `string` in netstandard2.0, use the null-coalescing operator with `string.Empty`:
  ```csharp
  string nonNullValue = possiblyNullValue ?? string.Empty;
  ```

## Data Access Patterns

- This is a **library project** for Android SDK interaction - not a typical data access layer
- Focus on file system operations, process execution, and XML/JSON parsing
- No Entity Framework Core - uses file-based data (XML manifests, JSON feeds)

## Build and Test Commands

- **Build**: `dotnet build Xamarin.Android.Tools.sln`
- **Test**: `dotnet test tests/Xamarin.Android.Tools.AndroidSdk-Tests/`
- **Full build with Makefile**: `make all` (on Unix-like systems)
- Tests use **NUnit** framework

## Validation and Error Handling

- Validate inputs at public API boundaries using guard clauses
- Use `ArgumentNullException.ThrowIfNull(param)` on net6.0+; use `if (param is null) throw new ArgumentNullException(nameof(param));` for netstandard2.0
- Use `string.IsNullOrEmpty()` or `string.IsNullOrWhiteSpace()` for string validation
- Use specific exception types (`ArgumentException`, `InvalidOperationException`, `FileNotFoundException`)
- Don't swallow exceptions silently - log and rethrow or let them propagate

## API Design and Documentation

- This is a **public library** - all public APIs must have XML documentation comments
- Use `<summary>`, `<param>`, `<returns>`, `<exception>` tags
- Include `<example>` sections for complex APIs
- Keep API surface minimal - prefer `internal` unless explicitly needed as public
- Assembly is **strongly named** (uses `product.snk`)

## Logging and Monitoring

- Use `Action<TraceLevel, string>` delegates for logging (standard pattern in this project)
- Default logger: `AndroidSdkInfo.DefaultConsoleLogger`
- Keep logging opt-in via constructor parameters
- Avoid direct `Console.WriteLine` - use logging delegates
- Log important operations like process execution, file I/O, and SDK detection

## Testing

- Always include test cases for critical paths
- Follow the Arrange-Act-Assert (AAA) pattern, but avoid explicit "Arrange", "Act", or "Assert" comments; use structure and test method names instead
- Copy existing style in nearby files for test method names and capitalization
- Tests use **NUnit** framework — assertions use `Assert.That` style

## Performance

- Use asynchronous programming patterns for I/O-bound operations (process execution, file I/O, network)
- Use `ArrayPool<byte>` for large buffers (behind `#if NET5_0_OR_GREATER` when needed)
- Supports trimming and AOT for non-netstandard2.0 targets (`<IsTrimmable>true</IsTrimmable>`)

## Deployment

- This is a **NuGet library package** - not a deployable application
- Builds produce NuGet packages for consumption by other projects
- Uses Azure Pipelines for CI/CD (see `azure-pipelines.yaml`)
- Multi-targets for compatibility: netstandard2.0 (.NET Framework / Visual Studio hosts) + latest .NET (performance, modern APIs)
- Supports trimming and AOT for non-netstandard2.0 targets
