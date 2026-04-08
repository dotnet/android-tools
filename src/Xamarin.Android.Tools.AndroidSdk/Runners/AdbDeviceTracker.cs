// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools;

/// <summary>
/// Monitors ADB device connections in real-time via the <c>host:track-devices-l</c> socket protocol.
/// Pushes device list updates through a callback whenever devices connect, disconnect, or change state.
/// </summary>
public sealed class AdbDeviceTracker : IDisposable
{
	readonly int port;
	readonly Action<TraceLevel, string> logger;
	readonly string? adbPath;
	readonly IDictionary<string, string>? environmentVariables;
	IReadOnlyList<AdbDeviceInfo> currentDevices = Array.Empty<AdbDeviceInfo> ();
	CancellationTokenSource? trackingCts;
	bool disposed;

	/// <summary>
	/// Creates a new AdbDeviceTracker.
	/// </summary>
	/// <param name="adbPath">Optional path to the adb executable for starting the server if needed.</param>
	/// <param name="port">ADB daemon port (default 5037).</param>
	/// <param name="environmentVariables">Optional environment variables for adb processes.</param>
	/// <param name="logger">Optional logger callback.</param>
	public AdbDeviceTracker (string? adbPath = null, int port = 5037,
		IDictionary<string, string>? environmentVariables = null,
		Action<TraceLevel, string>? logger = null)
	{
		if (port <= 0 || port > 65535)
			throw new ArgumentOutOfRangeException (nameof (port), "Port must be between 1 and 65535.");
		this.adbPath = adbPath;
		this.port = port;
		this.environmentVariables = environmentVariables;
		this.logger = logger ?? RunnerDefaults.NullLogger;
	}

	/// <summary>
	/// Current snapshot of tracked devices.
	/// </summary>
	public IReadOnlyList<AdbDeviceInfo> CurrentDevices => currentDevices;

	/// <summary>
	/// Starts tracking device changes. Calls <paramref name="onDevicesChanged"/> whenever
	/// the device list changes. Blocks until cancelled or disposed.
	/// Automatically reconnects on connection drops with exponential backoff.
	/// </summary>
	/// <param name="onDevicesChanged">Callback invoked with the updated device list on each change.</param>
	/// <param name="cancellationToken">Token to stop tracking.</param>
	public async Task StartAsync (
		Action<IReadOnlyList<AdbDeviceInfo>> onDevicesChanged,
		CancellationToken cancellationToken = default)
	{
		if (onDevicesChanged == null)
			throw new ArgumentNullException (nameof (onDevicesChanged));
		if (disposed)
			throw new ObjectDisposedException (nameof (AdbDeviceTracker));

		trackingCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		var token = trackingCts.Token;
		var backoffMs = InitialBackoffMs;

		while (!token.IsCancellationRequested) {
			try {
				await TrackDevicesAsync (onDevicesChanged, token).ConfigureAwait (false);
			} catch (OperationCanceledException) when (token.IsCancellationRequested) {
				break;
			} catch (Exception ex) {
				logger.Invoke (TraceLevel.Warning, $"ADB tracking connection lost: {ex.Message}. Reconnecting in {backoffMs}ms...");
				try {
					await Task.Delay (backoffMs, token).ConfigureAwait (false);
				} catch (OperationCanceledException) {
					break;
				}
				backoffMs = Math.Min (backoffMs * 2, MaxBackoffMs);
				continue;
			}
			// Reset backoff on clean connection
			backoffMs = InitialBackoffMs;
		}
	}

	const int InitialBackoffMs = 500;
	const int MaxBackoffMs = 16000;

	async Task TrackDevicesAsync (
		Action<IReadOnlyList<AdbDeviceInfo>> onDevicesChanged,
		CancellationToken cancellationToken)
	{
		using var client = new TcpClient ();
#if NET5_0_OR_GREATER
		await client.ConnectAsync ("127.0.0.1", port, cancellationToken).ConfigureAwait (false);
#else
		await client.ConnectAsync ("127.0.0.1", port).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
#endif

		var stream = client.GetStream ();
		logger.Invoke (TraceLevel.Verbose, "Connected to ADB daemon, sending track-devices-l command");

		// Send: <4-digit hex length><command>
		var command = "host:track-devices-l";
		var header = command.Length.ToString ("x4") + command;
		var headerBytes = Encoding.ASCII.GetBytes (header);
		await stream.WriteAsync (headerBytes, 0, headerBytes.Length, cancellationToken).ConfigureAwait (false);
		await stream.FlushAsync (cancellationToken).ConfigureAwait (false);

		// Read response status (OKAY or FAIL)
		var status = await ReadExactAsync (stream, 4, cancellationToken).ConfigureAwait (false);
		if (status != "OKAY") {
			var failMsg = await TryReadLengthPrefixedAsync (stream, cancellationToken).ConfigureAwait (false);
			throw new InvalidOperationException ($"ADB daemon rejected track-devices: {status} {failMsg}");
		}

		logger.Invoke (TraceLevel.Verbose, "ADB tracking active");

		// Read length-prefixed device list updates
		while (!cancellationToken.IsCancellationRequested) {
			var payload = await TryReadLengthPrefixedAsync (stream, cancellationToken).ConfigureAwait (false);
			if (payload == null)
				throw new IOException ("ADB daemon closed the connection.");

			var lines = payload.Split ('\n');
			var devices = AdbRunner.ParseAdbDevicesOutput (lines);
			currentDevices = devices;
			onDevicesChanged (devices);
		}
	}

	internal static async Task<string?> TryReadLengthPrefixedAsync (Stream stream, CancellationToken cancellationToken)
	{
		// Length is a 4-digit hex string
		var lengthHex = await ReadExactOrNullAsync (stream, 4, cancellationToken).ConfigureAwait (false);
		if (lengthHex == null)
			return null;

		if (!int.TryParse (lengthHex, System.Globalization.NumberStyles.HexNumber, null, out var length))
			throw new FormatException ($"Invalid ADB length prefix: '{lengthHex}'");

		if (length == 0)
			return string.Empty;

		return await ReadExactAsync (stream, length, cancellationToken).ConfigureAwait (false);
	}

	static async Task<string> ReadExactAsync (Stream stream, int count, CancellationToken cancellationToken)
	{
		var result = await ReadExactOrNullAsync (stream, count, cancellationToken).ConfigureAwait (false);
		return result ?? throw new IOException ($"Unexpected end of stream (expected {count} bytes).");
	}

	static async Task<string?> ReadExactOrNullAsync (Stream stream, int count, CancellationToken cancellationToken)
	{
		var buffer = new byte [count];
		var totalRead = 0;
		while (totalRead < count) {
			cancellationToken.ThrowIfCancellationRequested ();
#if NET5_0_OR_GREATER
			var read = await stream.ReadAsync (buffer.AsMemory (totalRead, count - totalRead), cancellationToken).ConfigureAwait (false);
#else
			var read = await stream.ReadAsync (buffer, totalRead, count - totalRead, cancellationToken).ConfigureAwait (false);
#endif
			if (read == 0)
				return totalRead == 0 ? null : throw new IOException ($"Unexpected end of stream (read {totalRead} of {count} bytes).");
			totalRead += read;
		}
		return Encoding.ASCII.GetString (buffer, 0, count);
	}

	public void Dispose ()
	{
		if (disposed)
			return;
		disposed = true;
		trackingCts?.Cancel ();
		trackingCts?.Dispose ();
	}
}
