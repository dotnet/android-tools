// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using NUnit.Framework;

namespace Xamarin.Android.Tools.Tests;

[TestFixture]
public class EmulatorRunnerTests
{
	[Test]
	public void ParseListAvdsOutput_MultipleAvds ()
	{
		var output = "Pixel_7_API_35\nMAUI_Emulator\nNexus_5X\n";

		var avds = EmulatorRunner.ParseListAvdsOutput (output);

		Assert.AreEqual (3, avds.Count);
		Assert.AreEqual ("Pixel_7_API_35", avds [0]);
		Assert.AreEqual ("MAUI_Emulator", avds [1]);
		Assert.AreEqual ("Nexus_5X", avds [2]);
	}

	[Test]
	public void ParseListAvdsOutput_EmptyOutput ()
	{
		var avds = EmulatorRunner.ParseListAvdsOutput ("");
		Assert.AreEqual (0, avds.Count);
	}

	[Test]
	public void ParseListAvdsOutput_WindowsNewlines ()
	{
		var output = "Pixel_7_API_35\r\nMAUI_Emulator\r\n";

		var avds = EmulatorRunner.ParseListAvdsOutput (output);

		Assert.AreEqual (2, avds.Count);
		Assert.AreEqual ("Pixel_7_API_35", avds [0]);
		Assert.AreEqual ("MAUI_Emulator", avds [1]);
	}

	[Test]
	public void ParseListAvdsOutput_BlankLines ()
	{
		var output = "\nPixel_7_API_35\n\n\nMAUI_Emulator\n\n";

		var avds = EmulatorRunner.ParseListAvdsOutput (output);

		Assert.AreEqual (2, avds.Count);
	}

	[Test]
	public void EmulatorPath_FindsInSdk ()
	{
		var tempDir = Path.Combine (Path.GetTempPath (), $"emu-test-{Path.GetRandomFileName ()}");
		var emulatorDir = Path.Combine (tempDir, "emulator");
		Directory.CreateDirectory (emulatorDir);

		try {
			var emuName = OS.IsWindows ? "emulator.exe" : "emulator";
			File.WriteAllText (Path.Combine (emulatorDir, emuName), "");

			var runner = new EmulatorRunner (() => tempDir);

			Assert.IsNotNull (runner.EmulatorPath);
			Assert.IsTrue (runner.IsAvailable);
		} finally {
			Directory.Delete (tempDir, true);
		}
	}

	[Test]
	public void EmulatorPath_MissingSdk_ReturnsNull ()
	{
		var runner = new EmulatorRunner (() => "/nonexistent/path");
		Assert.IsNull (runner.EmulatorPath);
		Assert.IsFalse (runner.IsAvailable);
	}

	[Test]
	public void EmulatorPath_NullSdk_ReturnsNull ()
	{
		var runner = new EmulatorRunner (() => null);
		Assert.IsNull (runner.EmulatorPath);
		Assert.IsFalse (runner.IsAvailable);
	}
}
