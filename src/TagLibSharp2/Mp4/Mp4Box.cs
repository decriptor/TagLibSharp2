// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Represents an MP4 box (atom) in the ISO base media file format.
/// </summary>
/// <remarks>
/// <para>
/// MP4 files are composed of nested boxes (also called atoms).
/// Each box has a 4-byte type code and contains either data or child boxes.
/// </para>
/// <para>
/// Box structure:
/// </para>
/// <code>
/// [size:4 (uint32 BE)] [type:4 (4-char code)] [data...] (or [child boxes...])
/// </code>
/// <para>
/// If size == 1, the next 8 bytes contain a 64-bit size (largesize).
/// If size == 0, the box extends to the end of the file.
/// </para>
/// <para>
/// Reference: ISO 14496-12 Section 4.2 (Object Structure).
/// </para>
/// </remarks>
public class Mp4Box
{
	/// <summary>
	/// Size of the standard box header (size + type).
	/// </summary>
	public const int HeaderSize = 8;

	/// <summary>
	/// Size of the extended box header (size + type + largesize).
	/// </summary>
	public const int ExtendedHeaderSize = 16;

	readonly List<Mp4Box> _children = new(4);

	internal Mp4Box(string type, BinaryData data, IReadOnlyList<Mp4Box>? children = null, bool usesExtendedSize = false)
	{
		Type = type;
		Data = data;
		UsesExtendedSize = usesExtendedSize;

		if (children is not null) {
			foreach (var child in children)
				_children.Add(child);
		}

		TotalSize = CalculateTotalSize();
	}

	/// <summary>
	/// Gets the 4-character box type code (e.g., "ftyp", "moov", "ilst").
	/// </summary>
	public string Type { get; }

	/// <summary>
	/// Gets the total size of the box including header and data.
	/// </summary>
	/// <remarks>
	/// A value of 0 means the box extends to the end of the file.
	/// </remarks>
	public long TotalSize { get; private set; }

	/// <summary>
	/// Gets a value indicating whether this box uses the extended (64-bit) size field.
	/// </summary>
	public bool UsesExtendedSize { get; }

	/// <summary>
	/// Gets a value indicating whether this is a container box with children.
	/// </summary>
	public bool IsContainer => _children.Count > 0;

	/// <summary>
	/// Gets the box data (excluding the header).
	/// </summary>
	/// <remarks>
	/// For container boxes (like moov, trak), this contains the raw child box data.
	/// For leaf boxes (like ftyp, meta data), this contains the actual box content.
	/// </remarks>
	public BinaryData Data { get; }

	/// <summary>
	/// Gets the child boxes if this is a container box.
	/// </summary>
	public IReadOnlyList<Mp4Box> Children => _children;

	/// <summary>
	/// Finds the first direct child box with the specified type.
	/// </summary>
	/// <param name="type">The 4-character box type to find.</param>
	/// <returns>The matching box, or null if not found.</returns>
	public Mp4Box? FindChild(string type)
	{
		for (int i = 0; i < _children.Count; i++) {
			if (_children[i].Type == type)
				return _children[i];
		}
		return null;
	}

	/// <summary>
	/// Finds a descendant box by traversing the specified path.
	/// </summary>
	/// <param name="path">The path of box types to traverse (e.g., "moov", "udta", "meta").</param>
	/// <returns>The matching box at the end of the path, or null if not found.</returns>
	/// <remarks>
	/// Example: FindDescendant("moov", "udta", "meta", "ilst") navigates to the iTunes-style metadata list.
	/// </remarks>
	public Mp4Box? FindDescendant(params string[] path)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		Polyfills.ThrowIfNull(path, nameof(path));
#else
		ArgumentNullException.ThrowIfNull(path);
#endif

		if (path.Length == 0)
			return this;

		var current = this;
		for (int i = 0; i < path.Length; i++) {
			current = current.FindChild(path[i]);
			if (current is null)
				return null;
		}
		return current;
	}

	/// <summary>
	/// Serializes this box to its binary representation.
	/// </summary>
	/// <returns>The complete box data including header.</returns>
	public BinaryData Render()
	{
		// For container boxes, render children first
		BinaryData contentData;
		if (IsContainer) {
			using var childBuilder = new BinaryDataBuilder();
			for (int i = 0; i < _children.Count; i++)
				childBuilder.Add(_children[i].Render());
			contentData = childBuilder.ToBinaryData();
		} else {
			contentData = Data;
		}

		var totalSize = (UsesExtendedSize ? ExtendedHeaderSize : HeaderSize) + contentData.Length;

		using var result = new BinaryDataBuilder();

		if (UsesExtendedSize) {
			// Extended size: size=1, type, then 64-bit size
			result.AddUInt32BE(1);
			result.AddStringLatin1(Type);
			result.AddUInt64BE((ulong)totalSize);
		} else {
			// Standard size
			result.AddUInt32BE((uint)totalSize);
			result.AddStringLatin1(Type);
		}

		result.Add(contentData);
		return result.ToBinaryData();
	}

	/// <summary>
	/// Calculates the total size of this box including header.
	/// </summary>
	long CalculateTotalSize()
	{
		var headerSize = UsesExtendedSize ? ExtendedHeaderSize : HeaderSize;
		return headerSize + Data.Length;
	}

	/// <inheritdoc/>
	public override string ToString() => $"{Type} ({TotalSize} bytes{(IsContainer ? $", {Children.Count} children" : "")})";

	/// <summary>
	/// Parses a single MP4 box from binary data.
	/// </summary>
	/// <param name="data">The data to parse.</param>
	/// <returns>The result of parsing.</returns>
	public static Mp4BoxReadResult Parse(ReadOnlySpan<byte> data) => Mp4BoxParser.ParseBox(data);

	/// <summary>
	/// Navigates to a nested box using slash-separated path.
	/// </summary>
	/// <param name="path">The path (e.g., "moov/udta/meta/ilst").</param>
	/// <returns>The box at the path, or null if not found.</returns>
	public Mp4Box? Navigate(string path)
	{
		if (string.IsNullOrEmpty(path))
			return this;

		var parts = path.Split('/');
		var current = this;

		foreach (var part in parts) {
			current = current.FindChild(part);
			if (current is null)
				return null;
		}

		return current;
	}
}
