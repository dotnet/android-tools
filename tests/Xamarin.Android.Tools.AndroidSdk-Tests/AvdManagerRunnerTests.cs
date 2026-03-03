// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NUnit.Framework;

namespace Xamarin.Android.Tools.Tests;

[TestFixture]
public class AvdManagerRunnerTests
{
	[Test]
	public void ParseAvdListOutput_MultipleAvds ()
	{
		var output =
			"Available Android Virtual Devices:\n" +
			"    Name: Pixel_7_API_35\n" +
			"    Device: pixel_7 (Google)\n" +
			"  Path: /Users/test/.android/avd/Pixel_7_API_35.avd\n" +
			"  Target: Google APIs (Google Inc.)\n" +
			"          Based on: Android 15 Tag/ABI: google_apis/arm64-v8a\n" +
			"---------\n" +
			"    Name: MAUI_Emulator\n" +
			"    Device: pixel_6 (Google)\n" +
			"  Path: /Users/test/.android/avd/MAUI_Emulator.avd\n" +
			"  Target: Google APIs (Google Inc.)\n" +
			"          Based on: Android 14 Tag/ABI: google_apis/x86_64\n";

		var avds = AvdManagerRunner.ParseAvdListOutput (output);

		Assert.AreEqual (2, avds.Count);

		Assert.AreEqual ("Pixel_7_API_35", avds [0].Name);
		Assert.AreEqual ("pixel_7 (Google)", avds [0].DeviceProfile);
		Assert.AreEqual ("/Users/test/.android/avd/Pixel_7_API_35.avd", avds [0].Path);

		Assert.AreEqual ("MAUI_Emulator", avds [1].Name);
		Assert.AreEqual ("pixel_6 (Google)", avds [1].DeviceProfile);
		Assert.AreEqual ("/Users/test/.android/avd/MAUI_Emulator.avd", avds [1].Path);
	}

	[Test]
	public void ParseAvdListOutput_WindowsNewlines ()
	{
		var output =
			"Available Android Virtual Devices:\r\n" +
			"    Name: Test_AVD\r\n" +
			"    Device: Nexus 5X (Google)\r\n" +
			"  Path: C:\\Users\\test\\.android\\avd\\Test_AVD.avd\r\n" +
			"  Target: Google APIs (Google Inc.)\r\n";

		var avds = AvdManagerRunner.ParseAvdListOutput (output);

		Assert.AreEqual (1, avds.Count);
		Assert.AreEqual ("Test_AVD", avds [0].Name);
		Assert.AreEqual ("Nexus 5X (Google)", avds [0].DeviceProfile);
		Assert.AreEqual ("C:\\Users\\test\\.android\\avd\\Test_AVD.avd", avds [0].Path);
	}

	[Test]
	public void ParseAvdListOutput_EmptyOutput ()
	{
		var avds = AvdManagerRunner.ParseAvdListOutput ("");
		Assert.AreEqual (0, avds.Count);
	}

	[Test]
	public void ParseAvdListOutput_NoAvds ()
	{
		var output = "Available Android Virtual Devices:\n";
		var avds = AvdManagerRunner.ParseAvdListOutput (output);
		Assert.AreEqual (0, avds.Count);
	}

	[Test]
	public void ParseAvdListOutput_SingleAvdNoDevice ()
	{
		var output =
			"    Name: Minimal_AVD\n" +
			"  Path: /home/user/.android/avd/Minimal_AVD.avd\n";

		var avds = AvdManagerRunner.ParseAvdListOutput (output);

		Assert.AreEqual (1, avds.Count);
		Assert.AreEqual ("Minimal_AVD", avds [0].Name);
		Assert.IsNull (avds [0].DeviceProfile);
		Assert.AreEqual ("/home/user/.android/avd/Minimal_AVD.avd", avds [0].Path);
	}
}
