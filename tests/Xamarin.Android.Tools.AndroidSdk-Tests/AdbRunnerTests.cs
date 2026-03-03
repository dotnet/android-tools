// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using NUnit.Framework;

namespace Xamarin.Android.Tools.Tests;

[TestFixture]
public class AdbRunnerTests
{
	[Test]
	public void ParseDeviceListOutput_MultipleDevices ()
	{
		var output =
			"List of devices attached\n" +
			"emulator-5554          device product:sdk_gphone64_arm64 model:sdk_gphone64_arm64 device:emu64a transport_id:1\n" +
			"R5CR20XXXXX            device usb:1-1 product:starqltesq model:SM_G960U device:starqltesq transport_id:2\n";

		var devices = AdbRunner.ParseDeviceListOutput (output);

		Assert.AreEqual (2, devices.Count);

		Assert.AreEqual ("emulator-5554", devices [0].Serial);
		Assert.AreEqual ("device", devices [0].State);
		Assert.AreEqual ("sdk_gphone64_arm64", devices [0].Model);
		Assert.AreEqual ("emu64a", devices [0].Device);
		Assert.IsTrue (devices [0].IsEmulator);

		Assert.AreEqual ("R5CR20XXXXX", devices [1].Serial);
		Assert.AreEqual ("device", devices [1].State);
		Assert.AreEqual ("SM_G960U", devices [1].Model);
		Assert.AreEqual ("starqltesq", devices [1].Device);
		Assert.IsFalse (devices [1].IsEmulator);
	}

	[Test]
	public void ParseDeviceListOutput_EmptyList ()
	{
		var output = "List of devices attached\n\n";

		var devices = AdbRunner.ParseDeviceListOutput (output);
		Assert.AreEqual (0, devices.Count);
	}

	[Test]
	public void ParseDeviceListOutput_WindowsNewlines ()
	{
		var output =
			"List of devices attached\r\n" +
			"emulator-5554          device transport_id:1\r\n" +
			"\r\n";

		var devices = AdbRunner.ParseDeviceListOutput (output);

		Assert.AreEqual (1, devices.Count);
		Assert.AreEqual ("emulator-5554", devices [0].Serial);
		Assert.AreEqual ("device", devices [0].State);
		Assert.IsTrue (devices [0].IsEmulator);
	}

	[Test]
	public void ParseDeviceListOutput_OfflineDevice ()
	{
		var output =
			"List of devices attached\n" +
			"emulator-5554          offline\n" +
			"R5CR20XXXXX            device model:SM_G960U device:starqltesq\n";

		var devices = AdbRunner.ParseDeviceListOutput (output);

		Assert.AreEqual (2, devices.Count);
		Assert.AreEqual ("offline", devices [0].State);
		Assert.AreEqual ("device", devices [1].State);
	}

	[Test]
	public void ParseDeviceListOutput_UnauthorizedDevice ()
	{
		var output =
			"List of devices attached\n" +
			"R5CR20XXXXX            unauthorized transport_id:1\n";

		var devices = AdbRunner.ParseDeviceListOutput (output);

		Assert.AreEqual (1, devices.Count);
		Assert.AreEqual ("unauthorized", devices [0].State);
		Assert.IsFalse (devices [0].IsEmulator);
	}

	[Test]
	public void AdbPath_FindsInSdk ()
	{
		var tempDir = Path.Combine (Path.GetTempPath (), $"adb-test-{Path.GetRandomFileName ()}");
		var platformTools = Path.Combine (tempDir, "platform-tools");
		Directory.CreateDirectory (platformTools);

		try {
			var adbName = OS.IsWindows ? "adb.exe" : "adb";
			File.WriteAllText (Path.Combine (platformTools, adbName), "");

			var runner = new AdbRunner (() => tempDir);

			Assert.IsNotNull (runner.AdbPath);
			Assert.IsTrue (runner.IsAvailable);
			Assert.IsTrue (runner.AdbPath!.Contains ("platform-tools"));
		} finally {
			Directory.Delete (tempDir, true);
		}
	}

	[Test]
	public void AdbPath_NullSdkPath_StillSearchesPath ()
	{
		var runner = new AdbRunner (() => null);
		// Should not throw — falls back to PATH search
		_ = runner.AdbPath;
	}
}
