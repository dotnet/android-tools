# android-tools
[![Build Status](https://dev.azure.com/dnceng-public/public/_apis/build/status%2Fdotnet%2Fandroid-tools?branchName=main)](https://dev.azure.com/dnceng-public/public/_build/latest?definitionId=279&branchName=main)

**android-tools** is a library for interacting with the Android SDK, providing APIs for:
- Android SDK detection and management (`AndroidSdkInfo`)
- SDK component installation and bootstrapping (`SdkManager`)
- JDK detection and installation (`JdkInfo`, `JdkInstaller`)
- AVD (Android Virtual Device) management (`AvdManagerRunner`)
- ADB device interaction (`AdbRunner`)
- Emulator management (`EmulatorRunner`)

This code is shared between [dotnet/android][android], .NET MAUI tooling,
and IDE extensions (e.g., Visual Studio), without requiring consumers to
submodule the entire **android** repo.

[android]: https://github.com/dotnet/android

# Build Requirements

**android-tools** requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) or later.

# Build

To build **android-tools**:

	dotnet build Xamarin.Android.Tools.sln

Alternatively run `make` (on Unix-like systems):

	make

## Build Configuration

The default `make all` target accepts the following optional
**make**(1) variables:

  * `$(CONFIGURATION)`: The configuration to build.
    Possible values include `Debug` and `Release`.
    The default value is `Debug`.
  * `$(V)`: Controls build verbosity. When set to a non-zero value,
    builds are performed with `/v:diag` logging.

# Tests

To run the unit tests:

	dotnet test tests/Xamarin.Android.Tools.AndroidSdk-Tests/

# Build Output Directory Structure

There are two configurations, `Debug` and `Release`, controlled by the
`$(Configuration)` MSBuild property or the `$(CONFIGURATION)` make variable.

* `bin\$(Configuration)`: redistributable build artifacts.
* `bin\Test$(Configuration)`: Unit tests and related files.

# Multi-targeting

The library multi-targets `netstandard2.0` and `$(DotNetTargetFramework)` (currently `net9.0`).

- **`netstandard2.0`** is required for .NET Framework consumers (e.g., Visual Studio on Windows).
- The modern TFM enables newer runtime features behind `#if NET5_0_OR_GREATER` guards.

Multi-targeting can be disabled via `$(AndroidToolsDisableMultiTargeting)=true`.

# Distribution

Package versioning follows [Semantic Versioning 2.0.0](https://semver.org/).
The major version in the `nuget.version` file should be updated when a breaking change is introduced.
The minor version should be updated when new functionality is added.
The patch version is automatically determined by the number of commits since the last version change.

NuGet packages are produced for every build on [Azure Pipelines](https://dev.azure.com/dnceng-public/public/_build?definitionId=279).
To download a package, navigate to the build and click on the `Artifacts` button.

# Reporting Bugs

We use [GitHub Issues](https://github.com/dotnet/android-tools/issues) to track issues.
