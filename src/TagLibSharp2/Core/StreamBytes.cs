// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Reads a bounded number of bytes from a stream. A short read is the end of the stream.
/// </summary>
internal static class StreamBytes
{
	public static byte[] ReadAtMost (Stream stream, int count)
	{
		if (count <= 0)
			return [];

		var buffer = new byte[count];
		var read = Read (stream, buffer, 0, count);
		return read == count ? buffer : Trim (buffer, read);
	}

	public static async Task<byte[]> ReadAtMostAsync (Stream stream, int count, CancellationToken cancellationToken)
	{
		if (count <= 0)
			return [];

		var buffer = new byte[count];
		var read = await ReadAsync (stream, buffer, 0, count, cancellationToken).ConfigureAwait (false);
		return read == count ? buffer : Trim (buffer, read);
	}

	public static byte[] ReadToEnd (Stream stream)
	{
		if (stream.CanSeek) {
			var remaining = stream.Length - stream.Position;
			if (remaining <= 0)
				return [];
			if (remaining > int.MaxValue)
				throw new IOException ("File is too large to read into memory");
			return ReadAtMost (stream, (int)remaining);
		}

		using var copy = new MemoryStream ();
		stream.CopyTo (copy);
		return copy.ToArray ();
	}

	public static async Task<byte[]> ReadToEndAsync (Stream stream, CancellationToken cancellationToken)
	{
		if (stream.CanSeek) {
			var remaining = stream.Length - stream.Position;
			if (remaining <= 0)
				return [];
			if (remaining > int.MaxValue)
				throw new IOException ("File is too large to read into memory");
			return await ReadAtMostAsync (stream, (int)remaining, cancellationToken).ConfigureAwait (false);
		}

		using var copy = new MemoryStream ();
		await stream.CopyToAsync (copy, 81920, cancellationToken).ConfigureAwait (false);
		return copy.ToArray ();
	}

	static int Read (Stream stream, byte[] buffer, int offset, int count)
	{
		var read = 0;
		while (read < count) {
			var n = stream.Read (buffer, offset + read, count - read);
			if (n == 0)
				break;
			read += n;
		}

		return read;
	}

	static async Task<int> ReadAsync (Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		var read = 0;
		while (read < count) {
#if NETSTANDARD2_0
			var n = await stream.ReadAsync (buffer, offset + read, count - read, cancellationToken).ConfigureAwait (false);
#else
			var n = await stream.ReadAsync (buffer.AsMemory (offset + read, count - read), cancellationToken).ConfigureAwait (false);
#endif
			if (n == 0)
				break;
			read += n;
		}

		return read;
	}

	static byte[] Trim (byte[] buffer, int length)
	{
		if (length == 0)
			return [];
		var exact = new byte[length];
		Array.Copy (buffer, exact, length);
		return exact;
	}
}
