// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers;

namespace TagLibSharp2.Core;

/// <summary>
/// Copies a byte range between streams in fixed chunks.
/// </summary>
/// <remarks>
/// Each chunk is 64 KB, under the 85 KB large-object threshold.
/// <see cref="ArrayPool{T}.Rent"/> may return a longer array; only the first
/// <see cref="ChunkSize"/> bytes are used. The copy never reads
/// <see cref="Stream.Length"/>, because that is a stat on every call.
/// </remarks>
internal static class StreamCopy
{
	/// <summary>
	/// Bytes read or written in one call. Equal to this size is allowed.
	/// </summary>
	public const int ChunkSize = 64 * 1024;

	/// <summary>
	/// Rents a buffer of at least <see cref="ChunkSize"/> bytes.
	/// </summary>
	/// <returns>A pooled array. Return it with <see cref="Return"/>.</returns>
	public static byte[] Rent () => ArrayPool<byte>.Shared.Rent (ChunkSize);

	/// <summary>
	/// Returns a buffer from <see cref="Rent"/>.
	/// </summary>
	/// <param name="chunk">The rented buffer.</param>
	public static void Return (byte[] chunk) => ArrayPool<byte>.Shared.Return (chunk);

	/// <summary>
	/// Copies <paramref name="count"/> bytes from the source's current position.
	/// </summary>
	/// <param name="source">Stream positioned at the first byte to copy.</param>
	/// <param name="destination">Stream that receives those bytes.</param>
	/// <param name="count">Number of bytes to copy. Zero copies nothing.</param>
	/// <param name="chunk">Buffer from <see cref="Rent"/>.</param>
	public static void Copy (Stream source, Stream destination, long count, byte[] chunk)
	{
		var left = count;
		while (left > 0) {
			var want = (int)Math.Min (ChunkSize, left);
			var read = source.Read (chunk, 0, want);
			if (read <= 0)
				throw new EndOfStreamException ("The source ended before the requested bytes were copied");

			destination.Write (chunk, 0, read);
			left -= read;
		}
	}

	/// <summary>
	/// Reads <paramref name="count"/> bytes into <paramref name="destination"/>.
	/// </summary>
	/// <param name="source">Stream positioned at the first byte to read.</param>
	/// <param name="destination">Buffer that receives the bytes.</param>
	/// <param name="destinationOffset">Index in <paramref name="destination"/> for the first byte.</param>
	/// <param name="count">Number of bytes to read. Zero reads nothing.</param>
	/// <param name="chunk">Buffer from <see cref="Rent"/>.</param>
	public static void Read (Stream source, byte[] destination, int destinationOffset, int count, byte[] chunk)
	{
		var left = count;
		var dest = destinationOffset;
		while (left > 0) {
			var want = Math.Min (ChunkSize, left);
			var read = source.Read (chunk, 0, want);
			if (read <= 0)
				throw new EndOfStreamException ("The source ended before the requested bytes were copied");

			chunk.AsSpan (0, read).CopyTo (destination.AsSpan (dest));
			dest += read;
			left -= read;
		}
	}

	/// <summary>
	/// Writes <paramref name="data"/> in <see cref="ChunkSize"/> pieces.
	/// </summary>
	/// <param name="destination">Stream that receives the bytes.</param>
	/// <param name="data">Bytes to write. Empty writes nothing.</param>
	/// <param name="chunk">Buffer from <see cref="Rent"/>.</param>
	public static void Write (Stream destination, ReadOnlySpan<byte> data, byte[] chunk)
	{
		var offset = 0;
		while (offset < data.Length) {
			var count = Math.Min (ChunkSize, data.Length - offset);
			data.Slice (offset, count).CopyTo (chunk);
			destination.Write (chunk, 0, count);
			offset += count;
		}
	}
}
