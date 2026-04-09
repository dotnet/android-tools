// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Xamarin.Android.Tools.Tests;

[TestFixture]
public class AdbDeviceTrackerTests
{
	[Test]
	public void Constructor_InvalidPort_ThrowsArgumentOutOfRangeException ()
	{
		Assert.Throws<ArgumentOutOfRangeException> (() => new AdbDeviceTracker (port: 0));
		Assert.Throws<ArgumentOutOfRangeException> (() => new AdbDeviceTracker (port: -1));
		Assert.Throws<ArgumentOutOfRangeException> (() => new AdbDeviceTracker (port: 70000));
	}

	[Test]
	public void Constructor_ValidPort_Succeeds ()
	{
		using var tracker = new AdbDeviceTracker (port: 5037);
		Assert.IsNotNull (tracker);
		Assert.AreEqual (0, tracker.CurrentDevices.Count);
	}

	[Test]
	public void StartAsync_NullCallback_ThrowsArgumentNullException ()
	{
		using var tracker = new AdbDeviceTracker ();
		Assert.ThrowsAsync<ArgumentNullException> (() => tracker.StartAsync (null!));
	}

	[Test]
	public void StartAsync_AfterDispose_ThrowsObjectDisposedException ()
	{
		var tracker = new AdbDeviceTracker ();
		tracker.Dispose ();
		Assert.ThrowsAsync<ObjectDisposedException> (() => tracker.StartAsync (_ => { }));
	}

	[Test]
	public async Task StartAsync_CalledTwice_ThrowsInvalidOperationException ()
	{
		// Use a port where nothing is listening so ConnectAsync yields quickly
		using var tracker = new AdbDeviceTracker (port: 59999);
		using var cts = new CancellationTokenSource ();

		// First call sets isTracking synchronously before the first await
		var trackingTask = tracker.StartAsync (_ => { }, cts.Token);

		// Second call should throw because tracking is already active
		Assert.ThrowsAsync<InvalidOperationException> (
			() => tracker.StartAsync (_ => { }, cts.Token));

		cts.Cancel ();
		try { await trackingTask.ConfigureAwait (false); } catch (OperationCanceledException) { }
	}

	[Test]
	public void Dispose_MultipleTimes_DoesNotThrow ()
	{
		var tracker = new AdbDeviceTracker ();
		tracker.Dispose ();
		Assert.DoesNotThrow (() => tracker.Dispose ());
	}

	// --- AdbClient protocol tests ---

	[Test]
	public async Task ReadLengthPrefixedStringFromStreamAsync_ValidPayload ()
	{
		var payload = "emulator-5554\tdevice\n";
		var hex = payload.Length.ToString ("x4");
		var data = Encoding.ASCII.GetBytes (hex + payload);
		using var stream = new MemoryStream (data);

		var result = await AdbClient.ReadLengthPrefixedStringFromStreamAsync (stream, CancellationToken.None);
		Assert.AreEqual (payload, result);
	}

	[Test]
	public async Task ReadLengthPrefixedStringFromStreamAsync_EmptyPayload ()
	{
		var data = Encoding.ASCII.GetBytes ("0000");
		using var stream = new MemoryStream (data);

		var result = await AdbClient.ReadLengthPrefixedStringFromStreamAsync (stream, CancellationToken.None);
		Assert.AreEqual (string.Empty, result);
	}

	[Test]
	public async Task ReadLengthPrefixedStringFromStreamAsync_EndOfStream_ReturnsNull ()
	{
		using var stream = new MemoryStream (Array.Empty<byte> ());

		var result = await AdbClient.ReadLengthPrefixedStringFromStreamAsync (stream, CancellationToken.None);
		Assert.IsNull (result);
	}

	[Test]
	public async Task ReadLengthPrefixedStringFromStreamAsync_MultipleDevices ()
	{
		var payload =
			"0A041FDD400327\tdevice product:redfin model:Pixel_5 device:redfin transport_id:2\n" +
			"emulator-5554\tdevice product:sdk_gphone64_x86_64 model:sdk_gphone64_x86_64 device:emu64xa transport_id:1\n";
		var hex = payload.Length.ToString ("x4");
		var data = Encoding.ASCII.GetBytes (hex + payload);
		using var stream = new MemoryStream (data);

		var result = await AdbClient.ReadLengthPrefixedStringFromStreamAsync (stream, CancellationToken.None);
		Assert.IsNotNull (result);

		var devices = AdbRunner.ParseAdbDevicesOutput (result!.Split ('\n'));
		Assert.AreEqual (2, devices.Count);
		Assert.AreEqual ("0A041FDD400327", devices [0].Serial);
		Assert.AreEqual ("emulator-5554", devices [1].Serial);
	}

	[Test]
	public void ReadLengthPrefixedStringFromStreamAsync_InvalidHex_ThrowsFormatException ()
	{
		var data = Encoding.ASCII.GetBytes ("ZZZZ");
		using var stream = new MemoryStream (data);

		Assert.ThrowsAsync<FormatException> (
			() => AdbClient.ReadLengthPrefixedStringFromStreamAsync (stream, CancellationToken.None));
	}

	[Test]
	public void ReadLengthPrefixedStringFromStreamAsync_TruncatedPayload_ThrowsIOException ()
	{
		// Header says 100 bytes but only 5 are present
		var data = Encoding.ASCII.GetBytes ("0064hello");
		using var stream = new MemoryStream (data);

		Assert.ThrowsAsync<IOException> (
			() => AdbClient.ReadLengthPrefixedStringFromStreamAsync (stream, CancellationToken.None));
	}
}
