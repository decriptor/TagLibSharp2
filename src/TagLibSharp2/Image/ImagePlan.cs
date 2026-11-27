// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Image;

/// <summary>
/// Ordered new bytes and ranges of the original file. Rendering and writing are separate.
/// </summary>
sealed class ImagePlan
{
	readonly List<Piece> _pieces = [];

	/// <summary>Gets the number of bytes the plan produces.</summary>
	public long Length { get; private set; }

	/// <summary>One range covering the whole original file.</summary>
	/// <param name="length">Length of the original file.</param>
	public static ImagePlan Whole (int length)
	{
		var plan = new ImagePlan ();
		plan.Copy (0, length);
		return plan;
	}

	/// <summary>Appends bytes that are not in the original file.</summary>
	/// <param name="bytes">The new bytes. Empty appends nothing.</param>
	public void Add (ReadOnlySpan<byte> bytes)
	{
		if (bytes.IsEmpty)
			return;

		Add (bytes.ToArray ());
	}

	/// <summary>Appends bytes that are not in the original file.</summary>
	/// <param name="bytes">The new bytes. Empty appends nothing. The array is kept.</param>
	public void Add (byte[] bytes)
	{
		if (bytes.Length == 0)
			return;

		AddLiteral (_pieces, bytes);
		Length += bytes.Length;
	}

	/// <summary>Appends a range of the original file. Adjacent ranges become one.</summary>
	/// <param name="offset">Offset in the original file.</param>
	/// <param name="count">Number of bytes. Zero appends nothing.</param>
	public void Copy (long offset, long count)
	{
		if (count == 0)
			return;

#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (count < 0)
			throw new ArgumentOutOfRangeException (nameof (count));
#else
		ArgumentOutOfRangeException.ThrowIfNegative (count);
#endif
		AddRange (_pieces, offset, count);
		Length += count;
	}

	/// <summary>Appends another plan. Its ranges keep their original-file offsets.</summary>
	/// <param name="other">Plan to append.</param>
	public void Append (ImagePlan other)
	{
		foreach (var piece in other._pieces) {
			if (piece.IsRange)
				Copy (piece.Offset, piece.Length);
			else
				Add (piece.Bytes!);
		}
	}

	/// <summary>Copies the plan into one buffer.</summary>
	/// <param name="source">The original file.</param>
	/// <returns>The bytes the plan produces.</returns>
	public byte[] Materialize (ReadOnlySpan<byte> source)
	{
		var output = new byte[checked ((int)Length)];
		var dest = 0;
		foreach (var piece in _pieces) {
			var count = checked ((int)piece.Length);
			CopyPiece (piece, 0, count, source, output.AsSpan (dest, count));
			dest += count;
		}

		return output;
	}

	/// <summary>
	/// Returns <paramref name="source"/> when the plan is that whole file; otherwise <see cref="Materialize"/>.
	/// </summary>
	/// <param name="source">The original file.</param>
	public ReadOnlyMemory<byte> MaterializeShared (byte[] source)
	{
		if (_pieces.Count == 1 && _pieces[0].IsRange && _pieces[0].Offset == 0 && _pieces[0].Length == source.Length)
			return source;

		return Materialize (source);
	}

	/// <summary>Copies one span of the output.</summary>
	/// <param name="start">Offset in the output.</param>
	/// <param name="count">Number of bytes.</param>
	/// <param name="source">The original file, for ranges.</param>
	public byte[] MaterializeRange (long start, long count, ReadOnlySpan<byte> source)
	{
		if (start < 0 || count < 0 || start > Length || count > Length - start)
			throw new ArgumentOutOfRangeException (nameof (start));

		var output = new byte[checked ((int)count)];
		if (count == 0)
			return output;

		var filled = 0;
		var skip = start;
		foreach (var piece in _pieces) {
			if (skip >= piece.Length) {
				skip -= piece.Length;
				continue;
			}

			var take = (int)Math.Min (piece.Length - skip, count - filled);
			CopyPiece (piece, skip, take, source, output.AsSpan (filled, take));
			filled += take;
			skip = 0;
			if (filled == count)
				return output;
		}

		throw new ArgumentOutOfRangeException (nameof (count));
	}

	/// <summary>Removes an output span. Ranges on either side keep their original-file offsets.</summary>
	/// <param name="start">Offset in the output.</param>
	/// <param name="count">Number of bytes removed. Zero removes nothing.</param>
	public void Cut (long start, long count)
	{
		if (count == 0)
			return;

		if (start < 0 || count < 0 || start > Length || count > Length - start)
			throw new ArgumentOutOfRangeException (nameof (start));

		ReplaceSpan (start, start + count, null);
		Length -= count;
	}

	/// <summary>Replaces an output span with <paramref name="replacement"/> of the same length.</summary>
	/// <param name="start">Offset in the output.</param>
	/// <param name="count">Number of bytes replaced.</param>
	/// <param name="replacement">The new bytes. Its length is <paramref name="count"/>.</param>
	public void Splice (long start, long count, byte[] replacement)
	{
		if (count == 0)
			return;

		if (start < 0 || count < 0 || start > Length || count > Length - start || replacement.Length != count)
			throw new ArgumentOutOfRangeException (nameof (start));

		ReplaceSpan (start, start + count, replacement);
	}

	// insert null removes the span. Otherwise the span is replaced by those bytes.
	void ReplaceSpan (long start, long end, byte[]? insert)
	{
		var next = new List<Piece> (_pieces.Count + 1);
		long at = 0;
		var inserted = insert is null;
		foreach (var piece in _pieces) {
			var pieceEnd = at + piece.Length;
			if (pieceEnd <= start || at >= end) {
				AddExisting (next, piece);
			} else {
				var before = start > at ? start - at : 0;
				if (before > 0)
					AddSlice (next, piece, 0, before);
				if (!inserted) {
					AddLiteral (next, insert!);
					inserted = true;
				}

				var after = pieceEnd > end ? pieceEnd - end : 0;
				if (after > 0)
					AddSlice (next, piece, piece.Length - after, after);
			}

			at = pieceEnd;
		}

		if (!inserted)
			throw new ArgumentOutOfRangeException (nameof (start));

		_pieces.Clear ();
		_pieces.AddRange (next);
	}

	/// <summary>
	/// Writes the plan. Ranges come from <paramref name="source"/> when it is set, and from
	/// <paramref name="held"/> when it is not. New bytes are written as they are.
	/// </summary>
	/// <param name="source">Original file, or null to read ranges from <paramref name="held"/>.</param>
	/// <param name="held">Original bytes used when <paramref name="source"/> is null.</param>
	/// <param name="destination">Stream that receives the output.</param>
	/// <param name="chunk">Buffer from <see cref="StreamCopy.Rent"/>.</param>
	public void Write (Stream? source, ReadOnlySpan<byte> held, Stream destination, byte[] chunk)
	{
		foreach (var piece in _pieces) {
			if (!piece.IsRange) {
				StreamCopy.Write (destination, piece.Bytes!, chunk);
				continue;
			}

			if (source is not null) {
				if (source.Position != piece.Offset)
					source.Position = piece.Offset;
				StreamCopy.Copy (source, destination, piece.Length, chunk);
				continue;
			}

			var offset = checked ((int)piece.Offset);
			var count = checked ((int)piece.Length);
			StreamCopy.Write (destination, held.Slice (offset, count), chunk);
		}
	}

	static void CopyPiece (Piece piece, long skip, int take, ReadOnlySpan<byte> source, Span<byte> destination)
	{
		if (piece.IsRange)
			source.Slice (checked ((int)(piece.Offset + skip)), take).CopyTo (destination);
		else
			piece.Bytes!.AsSpan (checked ((int)skip), take).CopyTo (destination);
	}

	static void AddExisting (List<Piece> pieces, Piece piece)
	{
		if (piece.IsRange)
			AddRange (pieces, piece.Offset, piece.Length);
		else
			AddLiteral (pieces, piece.Bytes!);
	}

	static void AddSlice (List<Piece> pieces, Piece piece, long skip, long count)
	{
		if (count == 0)
			return;

		if (piece.IsRange) {
			AddRange (pieces, piece.Offset + skip, count);
			return;
		}

		var slice = new byte[checked ((int)count)];
		piece.Bytes!.AsSpan (checked ((int)skip), slice.Length).CopyTo (slice);
		AddLiteral (pieces, slice);
	}

	static void AddLiteral (List<Piece> pieces, byte[] bytes)
	{
		if (bytes.Length == 0)
			return;

		if (pieces.Count > 0 && !pieces[^1].IsRange) {
			var last = pieces[^1].Bytes!;
			var combined = new byte[last.Length + bytes.Length];
			last.CopyTo (combined, 0);
			bytes.CopyTo (combined, last.Length);
			pieces[^1] = new Piece (false, 0, combined.Length, combined);
			return;
		}

		pieces.Add (new Piece (false, 0, bytes.Length, bytes));
	}

	static void AddRange (List<Piece> pieces, long offset, long count)
	{
		if (pieces.Count > 0) {
			var last = pieces[^1];
			if (last.IsRange && last.Offset + last.Length == offset) {
				pieces[^1] = new Piece (true, last.Offset, last.Length + count, null);
				return;
			}
		}

		pieces.Add (new Piece (true, offset, count, null));
	}

	readonly record struct Piece (bool IsRange, long Offset, long Length, byte[]? Bytes);
}
