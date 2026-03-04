// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace Xamarin.Android.Tools.Tests;

/// <summary>
/// Integration tests that verify AdbRunner works against real Android SDK tools.
///
/// These tests only run on CI (TF_BUILD=True or CI=true) where hosted
/// images have JDK (JAVA_HOME) and Android SDK (ANDROID_HOME) pre-installed.
/// Tests are skipped on local developer machines.
/// </summary>
[TestFixture]
[Category ("Integration")]
public class RunnerIntegrationTests
{
	static string sdkPath;
	static string jdkPath;
	static SdkManager sdkManager;
	static string bootstrappedSdkPath;

	static void Log (TraceLevel level, string message)
	{
		TestContext.Progress.WriteLine ($"[{level}] {message}");
	}

	static void RequireCi ()
	{
		if (Environment.GetEnvironmentVariable ("TF_BUILD") is null &&
		    Environment.GetEnvironmentVariable ("CI") is null) {
			Assert.Ignore ("Integration tests only run on CI (TF_BUILD or CI env var must be set).");
		}
	}

	/// <summary>
	/// One-time setup: use pre-installed JDK/SDK on CI agents, bootstrap
	/// cmdline-tools only if needed. Azure Pipelines hosted images have
	/// JAVA_HOME and ANDROID_HOME already configured.
	/// </summary>
	[OneTimeSetUp]
	public async Task OneTimeSetUp ()
	{
		RequireCi ();

		// Use pre-installed JDK from JAVA_HOME (always available on CI agents)
		jdkPath = Environment.GetEnvironmentVariable (EnvironmentVariableNames.JavaHome);
		if (string.IsNullOrEmpty (jdkPath) || !Directory.Exists (jdkPath)) {
			Assert.Ignore ("JAVA_HOME not set or invalid — cannot run integration tests.");
			return;
		}
		TestContext.Progress.WriteLine ($"Using JDK from JAVA_HOME: {jdkPath}");

		// Use pre-installed Android SDK from ANDROID_HOME (available on CI agents)
		sdkPath = Environment.GetEnvironmentVariable (EnvironmentVariableNames.AndroidHome);
		if (string.IsNullOrEmpty (sdkPath) || !Directory.Exists (sdkPath)) {
			// Fall back to bootstrapping our own SDK
			TestContext.Progress.WriteLine ("ANDROID_HOME not set — bootstrapping SDK...");
			try {
				bootstrappedSdkPath = Path.Combine (Path.GetTempPath (), $"runner-integration-{Guid.NewGuid ():N}", "android-sdk");
				sdkManager = new SdkManager (Log);
				sdkManager.JavaSdkPath = jdkPath;
				using var cts = new CancellationTokenSource (TimeSpan.FromMinutes (10));
				await sdkManager.BootstrapAsync (bootstrappedSdkPath, cancellationToken: cts.Token);
				sdkPath = bootstrappedSdkPath;
				sdkManager.AndroidSdkPath = sdkPath;

				// Install platform-tools (provides adb)
				await sdkManager.InstallAsync (new [] { "platform-tools" }, acceptLicenses: true, cancellationToken: cts.Token);
				TestContext.Progress.WriteLine ($"SDK bootstrapped to: {sdkPath}");
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException) {
				Assert.Ignore ($"SDK bootstrap failed: {ex.Message}");
				return;
			}
		}
		else {
			TestContext.Progress.WriteLine ($"Using SDK from ANDROID_HOME: {sdkPath}");
			sdkManager = new SdkManager (Log);
			sdkManager.JavaSdkPath = jdkPath;
			sdkManager.AndroidSdkPath = sdkPath;
		}
	}

	[OneTimeTearDown]
	public void OneTimeTearDown ()
	{
		sdkManager?.Dispose ();

		// Only clean up if we bootstrapped our own SDK
		if (bootstrappedSdkPath != null) {
			var basePath = Path.GetDirectoryName (bootstrappedSdkPath);
			if (basePath != null && Directory.Exists (basePath)) {
				try {
					Directory.Delete (basePath, recursive: true);
				}
				catch {
					// Best-effort cleanup on CI
				}
			}
		}
	}

	// ── AdbRunner integration ──────────────────────────────────────

	[Test]
	public void AdbRunner_IsAvailable_WithSdk ()
	{
		var runner = new AdbRunner (() => sdkPath);

		Assert.IsTrue (runner.IsAvailable, "AdbRunner should find adb in SDK");
		Assert.IsNotNull (runner.AdbPath);
		Assert.IsTrue (File.Exists (runner.AdbPath), $"adb binary should exist at {runner.AdbPath}");
	}

	[Test]
	public async Task AdbRunner_ListDevicesAsync_ReturnsWithoutError ()
	{
		var runner = new AdbRunner (() => sdkPath);

		// On CI there are no physical devices or emulators, but the command
		// should succeed and return an empty (or non-null) list.
		var devices = await runner.ListDevicesAsync ();

		Assert.IsNotNull (devices);
		TestContext.Progress.WriteLine ($"ListDevicesAsync returned {devices.Count} device(s)");
	}

	[Test]
	public void AdbRunner_WaitForDeviceAsync_TimesOut_WhenNoDevice ()
	{
		var runner = new AdbRunner (() => sdkPath);

		// With no devices connected, wait-for-device should time out
		var ex = Assert.ThrowsAsync<TimeoutException> (async () =>
			await runner.WaitForDeviceAsync (timeout: TimeSpan.FromSeconds (5)));

		Assert.IsNotNull (ex);
		TestContext.Progress.WriteLine ($"WaitForDeviceAsync timed out as expected: {ex!.Message}");
	}

	// ── Cross-runner: verify tools exist ───────────────────────────

	[Test]
	public void AllRunners_ToolDiscovery_ConsistentWithSdk ()
	{
		var adb = new AdbRunner (() => sdkPath);

		Assert.IsTrue (adb.IsAvailable, "adb should be available");

		// adb path should be under the SDK
		StringAssert.StartsWith (sdkPath, adb.AdbPath!);
	}
}
