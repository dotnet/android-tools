// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools;

/// <summary>
/// Low-level ADB daemon socket protocol client.
/// Encapsulates a single TCP connection to the ADB server and exposes
/// the wire protocol operations (send command, read status, read length-prefixed payloads).
/// One instance = one connection. Dispose closes the socket.
/// </summary>
internal sealed class AdbClient : IDisposable
{
	readonly TcpClient client;
	NetworkStream? stream;
	bool disposed;

	public AdbClient ()
	{
		client = new TcpClient ();
	}

	/// <summary>
	/// Connects to the ADB daemon at 127.0.0.1 on the specified port.
	/// </summary>
	public async Task ConnectAsync (int port, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed ();
#if NET5_0_OR_GREATER
		await client.ConnectAsync ("127.0.0.1", port, cancellationToken).ConfigureAwait (false);
#else
		await client.ConnectAsync ("127.0.0.1", port).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
#endif
		stream = client.GetStream ();
	}

	/// <summary>
	/// Sends a length-prefixed command to the ADB daemon.
	/// Wire format: &lt;4-digit hex byte length&gt;&lt;command bytes&gt;
	/// </summary>
	public async Task SendCommandAsync (string command, CancellationToken cancellationToken = default)
	{
		var s = GetStream ();
		var commandBytes = Encoding.ASCII.GetBytes (command);
		var header = commandBytes.Length.ToString ("x4");
		var headerBytes = Encoding.ASCII.GetBytes (header);

		await s.WriteAsync (headerBytes, 0, headerBytes.Length, cancellationToken).ConfigureAwait (false);
		await s.WriteAsync (commandBytes, 0, commandBytes.Length, cancellationToken).ConfigureAwait (false);
		await s.FlushAsync (cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Reads the 4-byte status response from the ADB daemon.
	/// Returns <see cref="AdbResponseStatus.Okay"/> or <see cref="AdbResponseStatus.Fail"/>.
	/// </summary>
	public async Task<AdbResponseStatus> ReadStatusAsync (CancellationToken cancellationToken = default)
	{
		var s = GetStream ();
		var statusBytes = await ReadExactBytesAsync (s, 4, cancellationToken).ConfigureAwait (false);
		var status = Encoding.ASCII.GetString (statusBytes, 0, 4);
		return status switch {
			"OKAY" => AdbResponseStatus.Okay,
			"FAIL" => AdbResponseStatus.Fail,
			_ => throw new InvalidOperationException ($"Unexpected ADB status: '{status}'"),
		};
	}

	/// <summary>
	/// Reads the failure message after a FAIL status.
	/// </summary>
	public async Task<string> ReadFailMessageAsync (CancellationToken cancellationToken = default)
	{
		return await ReadLengthPrefixedStringAsync (cancellationToken).ConfigureAwait (false) ?? string.Empty;
	}

	/// <summary>
	/// Ensures the last status was OKAY; throws with the FAIL message otherwise.
	/// </summary>
	public async Task EnsureOkayAsync (CancellationToken cancellationToken = default)
	{
		var status = await ReadStatusAsync (cancellationToken).ConfigureAwait (false);
		if (status == AdbResponseStatus.Fail) {
			var message = await ReadFailMessageAsync (cancellationToken).ConfigureAwait (false);
			throw new InvalidOperationException ($"ADB command failed: {message}");
		}
	}

	/// <summary>
	/// Reads a length-prefixed ASCII string payload from the daemon.
	/// Returns null if the connection is closed cleanly before the length prefix.
	/// </summary>
	public async Task<string?> ReadLengthPrefixedStringAsync (CancellationToken cancellationToken = default)
	{
		var bytes = await ReadLengthPrefixedBytesAsync (cancellationToken).ConfigureAwait (false);
		if (bytes == null)
			return null;
		return Encoding.ASCII.GetString (bytes, 0, bytes.Length);
	}

	/// <summary>
	/// Reads a length-prefixed byte payload from the daemon.
	/// Returns null if the connection is closed cleanly before the length prefix.
	/// </summary>
	public async Task<byte[]?> ReadLengthPrefixedBytesAsync (CancellationToken cancellationToken = default)
	{
		var s = GetStream ();
		var lengthBytes = await ReadExactBytesOrNullAsync (s, 4, cancellationToken).ConfigureAwait (false);
		if (lengthBytes == null)
			return null;

		var lengthHex = Encoding.ASCII.GetString (lengthBytes, 0, 4);
		if (!int.TryParse (lengthHex, System.Globalization.NumberStyles.HexNumber, null, out var length))
			throw new FormatException ($"Invalid ADB length prefix: '{lengthHex}'");

		if (length == 0)
			return Array.Empty<byte> ();

		return await ReadExactBytesAsync (s, length, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Forcibly closes the underlying socket, unblocking any pending reads.
	/// </summary>
	public void Close ()
	{
		client.Close ();
	}

	public void Dispose ()
	{
		if (disposed)
			return;
		disposed = true;
		client.Close ();
		client.Dispose ();
	}

	NetworkStream GetStream ()
	{
		ThrowIfDisposed ();
		if (stream == null)
			throw new InvalidOperationException ("Not connected. Call ConnectAsync first.");
		return stream;
	}

	void ThrowIfDisposed ()
	{
		if (disposed)
			throw new ObjectDisposedException (nameof (AdbClient));
	}

	/// <summary>
	/// Reads a length-prefixed ASCII string from a raw stream.
	/// Useful for testing and for callers that already have a stream.
	/// </summary>
	internal static async Task<string?> ReadLengthPrefixedStringFromStreamAsync (Stream stream, CancellationToken cancellationToken)
	{
		var lengthBytes = await ReadExactBytesOrNullAsync (stream, 4, cancellationToken).ConfigureAwait (false);
		if (lengthBytes == null)
			return null;

		var lengthHex = Encoding.ASCII.GetString (lengthBytes, 0, 4);
		if (!int.TryParse (lengthHex, System.Globalization.NumberStyles.HexNumber, null, out var length))
			throw new FormatException ($"Invalid ADB length prefix: '{lengthHex}'");

		if (length == 0)
			return string.Empty;

		var payload = await ReadExactBytesAsync (stream, length, cancellationToken).ConfigureAwait (false);
		return Encoding.ASCII.GetString (payload, 0, payload.Length);
	}

	static async Task<byte[]> ReadExactBytesAsync (Stream stream, int count, CancellationToken cancellationToken)
	{
		var result = await ReadExactBytesOrNullAsync (stream, count, cancellationToken).ConfigureAwait (false);
		if (result == null)
			throw new IOException ($"Unexpected end of stream (expected {count} bytes).");
		return result;
	}

	static async Task<byte[]?> ReadExactBytesOrNullAsync (Stream stream, int count, CancellationToken cancellationToken)
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
			if (read == 0) {
				if (totalRead == 0)
					return null;
				throw new IOException ($"Unexpected end of stream (read {totalRead} of {count} bytes).");
			}
			totalRead += read;
		}
		return buffer;
	}
}
