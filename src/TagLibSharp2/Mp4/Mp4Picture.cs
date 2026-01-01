// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Represents an embedded picture in an MP4/M4A file.
/// </summary>
/// <remarks>
/// MP4 cover art is stored in the covr atom, which can contain multiple data children.
/// Each data child represents one image (JPEG or PNG).
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

	/// <summary>
	/// Initializes a new instance of the <see cref="Mp4Picture"/> class with minimal parameters.
	/// </summary>
	/// <param name="data">The raw image data.</param>
	/// <param name="isJpeg">True for JPEG, false for PNG.</param>
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
			_ => "image/unknown"
		};

		return new Mp4Picture (mimeType, PictureType.FrontCover, string.Empty, dataAtom.Data);
	}

	/// <summary>
	/// Converts this picture to a data atom for rendering.
	/// </summary>
	/// <returns>The binary representation as a data atom.</returns>
	internal BinaryData ToDataAtom ()
	{
		var isJpeg = MimeType.Contains ("jpeg", StringComparison.OrdinalIgnoreCase) ||
		             MimeType.Contains ("jpg", StringComparison.OrdinalIgnoreCase);

		return Mp4DataAtom.CreateImage (PictureData, isJpeg);
	}
}
