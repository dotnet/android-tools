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
/// One instance can be reused across reconnections via <see cref="ReconnectAsync"/>.
/// Dispose closes the socket.
/// </summary>
internal sealed class AdbClient : IDisposable
{
	TcpClient? client;
	NetworkStream? stream;
	bool disposed;

	/// <summary>
	/// Connects to the ADB daemon at 127.0.0.1 on the specified port.
	/// </summary>
	public async Task ConnectAsync (int port, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed ();
		var tcp = new TcpClient ();
		client = tcp;
#if NET5_0_OR_GREATER
		await tcp.ConnectAsync ("127.0.0.1", port, cancellationToken).ConfigureAwait (false);
#else
		await tcp.ConnectAsync ("127.0.0.1", port).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
#endif
		stream = tcp.GetStream ();
	}

	/// <summary>
	/// Closes the current connection and establishes a new one.
	/// </summary>
	public async Task ReconnectAsync (int port, CancellationToken cancellationToken = default)
	{
		CloseConnection ();
		await ConnectAsync (port, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Sends a length-prefixed command to the ADB daemon.
	/// Wire format: &lt;4-digit hex byte length&gt;&lt;command bytes&gt;
	/// </summary>
	public async Task SendCommandAsync (string command, CancellationToken cancellationToken = default)
	{
		var s = GetStream ();
		// Combine length prefix + command into a single write to minimize syscalls
		var commandBytes = Encoding.ASCII.GetBytes (command);
		var lengthPrefix = commandBytes.Length.ToString ("x4");
		var packet = new byte [4 + commandBytes.Length];
		Encoding.ASCII.GetBytes (lengthPrefix, 0, 4, packet, 0);
		Buffer.BlockCopy (commandBytes, 0, packet, 4, commandBytes.Length);
#if NET5_0_OR_GREATER
		await s.WriteAsync (packet.AsMemory (), cancellationToken).ConfigureAwait (false);
#else
		await s.WriteAsync (packet, 0, packet.Length, cancellationToken).ConfigureAwait (false);
#endif
		await s.FlushAsync (cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Reads the 4-byte status response from the ADB daemon.
	/// </summary>
	public async Task<AdbResponseStatus> ReadStatusAsync (CancellationToken cancellationToken = default)
	{
		var s = GetStream ();
		var statusBytes = await ReadExactBytesIntoNewArrayAsync (s, 4, cancellationToken).ConfigureAwait (false);
		// Status is always 4 ASCII chars
		if (statusBytes [0] == (byte) 'O' && statusBytes [1] == (byte) 'K' &&
			statusBytes [2] == (byte) 'A' && statusBytes [3] == (byte) 'Y')
			return AdbResponseStatus.Okay;
		if (statusBytes [0] == (byte) 'F' && statusBytes [1] == (byte) 'A' &&
			statusBytes [2] == (byte) 'I' && statusBytes [3] == (byte) 'L')
			return AdbResponseStatus.Fail;

		var status = Encoding.ASCII.GetString (statusBytes, 0, 4);
		throw new InvalidOperationException ($"Unexpected ADB status: '{status}'");
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
		var s = GetStream ();
		return await ReadLengthPrefixedStringCoreAsync (s, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Reads a length-prefixed byte payload from the daemon.
	/// Returns null if the connection is closed cleanly before the length prefix.
	/// </summary>
	public async Task<byte[]?> ReadLengthPrefixedBytesAsync (CancellationToken cancellationToken = default)
	{
		var s = GetStream ();
		return await ReadLengthPrefixedBytesCoreAsync (s, cancellationToken).ConfigureAwait (false);
	}

	/// <summary>
	/// Forcibly closes the underlying socket, unblocking any pending reads.
	/// </summary>
	public void Close ()
	{
		CloseConnection ();
	}

	public void Dispose ()
	{
		if (disposed)
			return;
		disposed = true;
		CloseConnection ();
	}

	void CloseConnection ()
	{
		stream = null;
		var tcp = client;
		client = null;
		if (tcp != null) {
			tcp.Close ();
			tcp.Dispose ();
		}
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

	// --- Shared core implementations (used by both instance and static methods) ---

	/// <summary>
	/// Reads a length-prefixed ASCII string from a raw stream.
	/// Shared core for both instance and static access patterns.
	/// </summary>
	internal static async Task<string?> ReadLengthPrefixedStringFromStreamAsync (Stream stream, CancellationToken cancellationToken)
	{
		return await ReadLengthPrefixedStringCoreAsync (stream, cancellationToken).ConfigureAwait (false);
	}

	static async Task<string?> ReadLengthPrefixedStringCoreAsync (Stream stream, CancellationToken cancellationToken)
	{
		var bytes = await ReadLengthPrefixedBytesCoreAsync (stream, cancellationToken).ConfigureAwait (false);
		if (bytes == null)
			return null;
		return Encoding.ASCII.GetString (bytes, 0, bytes.Length);
	}

	static async Task<byte[]?> ReadLengthPrefixedBytesCoreAsync (Stream stream, CancellationToken cancellationToken)
	{
		var lengthBytes = await ReadExactBytesOrNullAsync (stream, 4, cancellationToken).ConfigureAwait (false);
		if (lengthBytes == null)
			return null;

		var lengthHex = Encoding.ASCII.GetString (lengthBytes, 0, 4);
		if (!int.TryParse (lengthHex, System.Globalization.NumberStyles.HexNumber, null, out var length))
			throw new FormatException ($"Invalid ADB length prefix: '{lengthHex}'");

		if (length == 0)
			return Array.Empty<byte> ();

		return await ReadExactBytesIntoNewArrayAsync (stream, length, cancellationToken).ConfigureAwait (false);
	}

	static async Task<byte[]> ReadExactBytesIntoNewArrayAsync (Stream stream, int count, CancellationToken cancellationToken)
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
