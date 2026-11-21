// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Represents an embedded picture in an MP4/M4A file.
/// </summary>
/// <remarks>
/// MP4 cover art is stored in the covr atom, which can contain multiple data children.
/// Each data child represents one image (JPEG, PNG, GIF, or BMP).
/// </remarks>
public sealed class Mp4Picture : IPicture
{
	/// <inheritdoc/>
	public string MimeType { get; }

	/// <inheritdoc/>
	/// <remarks>
	/// MP4 files don't distinguish between different picture types in the same way as ID3v2/FLAC.
	/// All images in the covr atom are typically FrontCover.
	/// </remarks>
	public PictureType PictureType { get; }

	/// <inheritdoc/>
	/// <remarks>
	/// MP4 data atoms don't have a description field, so this is typically empty.
	/// </remarks>
	public string Description { get; }

	/// <inheritdoc/>
	public BinaryData PictureData { get; }

	// Parsed covers keep the file's type code, including 0. Null means the picture was created in memory.
	readonly int? _parsedTypeIndicator;
	readonly uint _parsedLocale;

	/// <summary>
	/// Initializes a new instance of the <see cref="Mp4Picture"/> class.
	/// </summary>
	/// <param name="mimeType">The MIME type (e.g., "image/jpeg" or "image/png").</param>
	/// <param name="pictureType">The picture type.</param>
	/// <param name="description">The description (typically empty for MP4).</param>
	/// <param name="pictureData">The image binary data.</param>
	public Mp4Picture (string mimeType, PictureType pictureType, string description, BinaryData pictureData)
	{
		MimeType = mimeType ?? string.Empty;
		PictureType = pictureType;
		Description = description ?? string.Empty;
		PictureData = pictureData;
	}

	Mp4Picture (string mimeType, PictureType pictureType, string description, BinaryData pictureData, int parsedTypeIndicator, uint parsedLocale)
		: this (mimeType, pictureType, description, pictureData)
	{
		_parsedTypeIndicator = parsedTypeIndicator;
		_parsedLocale = parsedLocale;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Mp4Picture"/> class with minimal parameters.
	/// </summary>
	/// <param name="data">The raw image data.</param>
	/// <param name="isJpeg">
	/// True for JPEG, false for PNG. The bytes decide the stored type when they are a known
	/// image format; otherwise this choice does.
	/// </param>
	public Mp4Picture (byte[] data, bool isJpeg)
		: this (isJpeg ? "image/jpeg" : "image/png", PictureType.FrontCover, string.Empty, new BinaryData (data))
	{
	}

	/// <summary>
	/// Creates an Mp4Picture from a data atom.
	/// </summary>
	/// <param name="dataAtom">The parsed data atom.</param>
	/// <returns>The Mp4Picture instance.</returns>
	internal static Mp4Picture FromDataAtom (Mp4DataAtom dataAtom)
	{
		var mimeType = dataAtom.TypeIndicator switch {
			Mp4AtomMapping.TypeJpeg => "image/jpeg",
			Mp4AtomMapping.TypePng => "image/png",
			Mp4AtomMapping.TypeGif => "image/gif",
			Mp4AtomMapping.TypeBmp => "image/bmp",
			_ => "image/unknown"
		};

		return new Mp4Picture (mimeType, PictureType.FrontCover, string.Empty, dataAtom.Data, dataAtom.TypeIndicator, dataAtom.Locale);
	}

	/// <summary>
	/// Type code written for this picture. Unchanged parsed bytes keep their original code.
	/// A picture created in memory is identified from its bytes first, then from its MIME type.
	/// </summary>
	/// <remarks>
	/// Codes are the well-known data types of the value atom: 13 JPEG, 14 PNG, 27 BMP, and 0 when
	/// no type is indicated (https://developer.apple.com/documentation/quicktime-file-format/well-known_types).
	/// 12 (GIF) is not in that table but is what iTunes-era files use.
	/// </remarks>
	internal int RenderTypeIndicator => _parsedTypeIndicator ?? IdentifyType (PictureData.Span, MimeType);

	/// <summary>
	/// Locale indicator written for this picture: the parsed one, or 0 (every locale) for a new picture.
	/// </summary>
	internal uint RenderLocale => _parsedLocale;

	static int IdentifyType (ReadOnlySpan<byte> data, string mimeType)
	{
		var sniffed = SniffType (data);
		return sniffed != Mp4AtomMapping.TypeBinary ? sniffed : TypeFromMime (mimeType);
	}

	static int TypeFromMime (string mimeType)
	{
		if (mimeType.Contains ("jpeg", StringComparison.OrdinalIgnoreCase) ||
			mimeType.Contains ("jpg", StringComparison.OrdinalIgnoreCase))
			return Mp4AtomMapping.TypeJpeg;

		if (mimeType.Contains ("png", StringComparison.OrdinalIgnoreCase))
			return Mp4AtomMapping.TypePng;

		if (mimeType.Contains ("gif", StringComparison.OrdinalIgnoreCase))
			return Mp4AtomMapping.TypeGif;

		if (mimeType.Contains ("bmp", StringComparison.OrdinalIgnoreCase))
			return Mp4AtomMapping.TypeBmp;

		return Mp4AtomMapping.TypeBinary;
	}

	static int SniffType (ReadOnlySpan<byte> data)
	{
		if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
			return Mp4AtomMapping.TypeJpeg;

		if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
			return Mp4AtomMapping.TypePng;

		if (data.Length >= 4 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8')
			return Mp4AtomMapping.TypeGif;

		if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
			return Mp4AtomMapping.TypeBmp;

		return Mp4AtomMapping.TypeBinary;
	}
}
