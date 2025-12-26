// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Represents a FLAC PICTURE metadata block.
/// </summary>
/// <remarks>
/// <para>
/// The FLAC PICTURE block stores embedded images with additional metadata
/// not present in ID3v2 APIC frames, including image dimensions and color information.
/// </para>
/// <para>
/// Binary format (all integers are big-endian):
/// </para>
/// <code>
/// [type:4][mime_len:4][mime:n][desc_len:4][desc:n]
/// [width:4][height:4][depth:4][colors:4]
/// [data_len:4][data:n]
/// </code>
/// <para>
/// Reference: https://xiph.org/flac/format.html#metadata_block_picture
/// </para>
/// </remarks>
public sealed class FlacPicture : Picture
{
	/// <inheritdoc/>
	public override string MimeType { get; }

	/// <inheritdoc/>
	public override PictureType PictureType { get; }

	/// <inheritdoc/>
	public override string Description { get; }

	/// <inheritdoc/>
	public override BinaryData PictureData { get; }

	/// <summary>
	/// Gets the width of the image in pixels.
	/// </summary>
	/// <remarks>
	/// This is an informational field from the FLAC metadata.
	/// A value of 0 indicates the width is unknown.
	/// </remarks>
	public uint Width { get; }

	/// <summary>
	/// Gets the height of the image in pixels.
	/// </summary>
	/// <remarks>
	/// This is an informational field from the FLAC metadata.
	/// A value of 0 indicates the height is unknown.
	/// </remarks>
	public uint Height { get; }

	/// <summary>
	/// Gets the color depth in bits per pixel.
	/// </summary>
	/// <remarks>
	/// Common values: 24 (RGB), 32 (RGBA), 8 (indexed).
	/// </remarks>
	public uint ColorDepth { get; }

	/// <summary>
	/// Gets the number of colors in the palette for indexed images.
	/// </summary>
	/// <remarks>
	/// 0 for non-indexed (true color) images.
	/// </remarks>
	public uint ColorCount { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="FlacPicture"/> class.
	/// </summary>
	/// <param name="mimeType">The MIME type of the image.</param>
	/// <param name="pictureType">The picture type.</param>
	/// <param name="description">A description of the image.</param>
	/// <param name="pictureData">The raw image data.</param>
	/// <param name="width">The image width in pixels (0 if unknown).</param>
	/// <param name="height">The image height in pixels (0 if unknown).</param>
	/// <param name="colorDepth">The color depth in bits per pixel.</param>
	/// <param name="colorCount">The number of colors for indexed images (0 for true color).</param>
	public FlacPicture (string mimeType, PictureType pictureType, string description,
		BinaryData pictureData, uint width, uint height, uint colorDepth, uint colorCount)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (mimeType is null)
			throw new ArgumentNullException (nameof (mimeType));
		if (description is null)
			throw new ArgumentNullException (nameof (description));
#else
		ArgumentNullException.ThrowIfNull (mimeType);
		ArgumentNullException.ThrowIfNull (description);
#endif
		MimeType = mimeType;
		PictureType = pictureType;
		Description = description;
		PictureData = pictureData;
		Width = width;
		Height = height;
		ColorDepth = colorDepth;
		ColorCount = colorCount;
	}

	/// <summary>
	/// Creates a <see cref="FlacPicture"/> from image data with automatic MIME type detection.
	/// </summary>
	/// <param name="imageData">The raw image data.</param>
	/// <param name="pictureType">The picture type. Defaults to <see cref="PictureType.FrontCover"/>.</param>
	/// <param name="description">Optional description. Defaults to empty string.</param>
	/// <returns>A new <see cref="FlacPicture"/> with detected MIME type.</returns>
	public static FlacPicture FromBytes (byte[] imageData, PictureType pictureType = PictureType.FrontCover,
		string description = "")
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (imageData is null)
			throw new ArgumentNullException (nameof (imageData));
#else
		ArgumentNullException.ThrowIfNull (imageData);
#endif
		var mimeType = DetectMimeType (imageData);
		return new FlacPicture (mimeType, pictureType, description, new BinaryData (imageData),
			0, 0, 0, 0); // Dimensions unknown without image parsing
	}

	/// <summary>
	/// Creates a <see cref="FlacPicture"/> from a file with automatic MIME type detection.
	/// </summary>
	/// <param name="path">The path to the image file.</param>
	/// <param name="pictureType">The picture type. Defaults to <see cref="PictureType.FrontCover"/>.</param>
	/// <param name="description">Optional description. Defaults to empty string.</param>
	/// <returns>A new <see cref="FlacPicture"/> with detected MIME type.</returns>
	public static FlacPicture FromFile (string path, PictureType pictureType = PictureType.FrontCover,
		string description = "")
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif
		var data = System.IO.File.ReadAllBytes (path);
		var mimeType = DetectMimeType (data, path);
		return new FlacPicture (mimeType, pictureType, description, new BinaryData (data),
			0, 0, 0, 0); // Dimensions unknown without image parsing
	}

	/// <summary>
	/// Attempts to read a FLAC PICTURE block from binary data.
	/// </summary>
	/// <param name="data">The binary data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static FlacPictureReadResult Read (ReadOnlySpan<byte> data)
	{
		// Minimum size: 4+4+4+4+4+4+4+4 = 32 bytes (all lengths zero)
		if (data.Length < 32)
			return FlacPictureReadResult.Failure ("Data too short for FLAC PICTURE block");

		var offset = 0;

		// Picture type (4 bytes BE)
		var pictureType = (PictureType)ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		// MIME type length (4 bytes BE)
		var mimeLenRaw = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		// Overflow protection: check if length exceeds int.MaxValue
		if (mimeLenRaw > int.MaxValue)
			return FlacPictureReadResult.Failure ("MIME type length overflow (exceeds maximum)");

		var mimeLen = (int)mimeLenRaw;
		if (offset + mimeLen > data.Length)
			return FlacPictureReadResult.Failure ("Invalid MIME type length");

		var mimeType = System.Text.Encoding.ASCII.GetString (data.Slice (offset, mimeLen));
		offset += mimeLen;

		// Description length (4 bytes BE)
		if (offset + 4 > data.Length)
			return FlacPictureReadResult.Failure ("Data too short for description length");

		var descLenRaw = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		// Overflow protection: check if length exceeds int.MaxValue
		if (descLenRaw > int.MaxValue)
			return FlacPictureReadResult.Failure ("Description length overflow (exceeds maximum)");

		var descLen = (int)descLenRaw;
		if (offset + descLen > data.Length)
			return FlacPictureReadResult.Failure ("Invalid description length");

		var description = System.Text.Encoding.UTF8.GetString (data.Slice (offset, descLen));
		offset += descLen;

		// Dimensions (16 bytes total)
		if (offset + 16 > data.Length)
			return FlacPictureReadResult.Failure ("Data too short for dimensions");

		var width = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		var height = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		var colorDepth = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		var colorCount = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		// Picture data length (4 bytes BE)
		if (offset + 4 > data.Length)
			return FlacPictureReadResult.Failure ("Data too short for picture data length");

		var dataLenRaw = ReadUInt32BE (data.Slice (offset, 4));
		offset += 4;

		// Overflow protection: check if length exceeds int.MaxValue
		if (dataLenRaw > int.MaxValue)
			return FlacPictureReadResult.Failure ("Picture data length overflow (exceeds maximum)");

		var dataLen = (int)dataLenRaw;
		if (offset + dataLen > data.Length)
			return FlacPictureReadResult.Failure ("Invalid picture data length");

		var pictureData = new BinaryData (data.Slice (offset, dataLen));

		var picture = new FlacPicture (mimeType, pictureType, description, pictureData,
			width, height, colorDepth, colorCount);

		return FlacPictureReadResult.Success (picture, offset + dataLen);
	}

	/// <summary>
	/// Renders the picture block content to binary data.
	/// </summary>
	/// <returns>The complete picture block data.</returns>
	public BinaryData RenderContent ()
	{
		var mimeBytes = System.Text.Encoding.ASCII.GetBytes (MimeType);
		var descBytes = System.Text.Encoding.UTF8.GetBytes (Description);

		var totalSize = 4 + // picture type
			4 + mimeBytes.Length + // MIME
			4 + descBytes.Length + // description
			16 + // dimensions
			4 + PictureData.Length; // picture data

		using var builder = new BinaryDataBuilder (totalSize);

		// Picture type (4 bytes BE)
		WriteUInt32BE (builder, (uint)PictureType);

		// MIME type
		WriteUInt32BE (builder, (uint)mimeBytes.Length);
		builder.Add (mimeBytes);

		// Description
		WriteUInt32BE (builder, (uint)descBytes.Length);
		builder.Add (descBytes);

		// Dimensions
		WriteUInt32BE (builder, Width);
		WriteUInt32BE (builder, Height);
		WriteUInt32BE (builder, ColorDepth);
		WriteUInt32BE (builder, ColorCount);

		// Picture data
		WriteUInt32BE (builder, (uint)PictureData.Length);
		builder.Add (PictureData);

		return builder.ToBinaryData ();
	}

	static uint ReadUInt32BE (ReadOnlySpan<byte> data)
	{
		return ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
	}

	static void WriteUInt32BE (BinaryDataBuilder builder, uint value)
	{
		builder.Add ((byte)((value >> 24) & 0xFF));
		builder.Add ((byte)((value >> 16) & 0xFF));
		builder.Add ((byte)((value >> 8) & 0xFF));
		builder.Add ((byte)(value & 0xFF));
	}
}

/// <summary>
/// Represents the result of reading a <see cref="FlacPicture"/> from binary data.
/// </summary>
public readonly struct FlacPictureReadResult : IEquatable<FlacPictureReadResult>
{
	/// <summary>
	/// Gets the parsed picture, or null if parsing failed.
	/// </summary>
	public FlacPicture? Picture { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Picture is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data.
	/// </summary>
	public int BytesConsumed { get; }

	FlacPictureReadResult (FlacPicture? picture, string? error, int bytesConsumed)
	{
		Picture = picture;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="picture">The parsed picture.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static FlacPictureReadResult Success (FlacPicture picture, int bytesConsumed) =>
		new (picture, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static FlacPictureReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (FlacPictureReadResult other) =>
		ReferenceEquals (Picture, other.Picture) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is FlacPictureReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Picture, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (FlacPictureReadResult left, FlacPictureReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (FlacPictureReadResult left, FlacPictureReadResult right) =>
		!left.Equals (right);
}
