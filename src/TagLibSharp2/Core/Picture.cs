// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Abstract base class for picture/image data in media files.
/// </summary>
/// <remarks>
/// <para>
/// This base class provides common functionality for picture handling across
/// different media formats (ID3v2 APIC, FLAC PICTURE, etc.).
/// </para>
/// <para>
/// Derived classes include <see cref="TagLibSharp2.Id3.Id3v2.Frames.PictureFrame"/> for ID3v2
/// and FlacPicture for FLAC files.
/// </para>
/// </remarks>
public abstract class Picture
{
	/// <summary>
	/// Gets the MIME type of the image (e.g., "image/jpeg", "image/png").
	/// </summary>
	public abstract string MimeType { get; }

	/// <summary>
	/// Gets the picture type indicating the image's purpose.
	/// </summary>
	public abstract PictureType PictureType { get; }

	/// <summary>
	/// Gets the picture description text.
	/// </summary>
	public abstract string Description { get; }

	/// <summary>
	/// Gets the raw picture data.
	/// </summary>
	public abstract BinaryData PictureData { get; }

	/// <summary>
	/// Detects the MIME type from image data by examining magic bytes.
	/// </summary>
	/// <param name="data">The image data.</param>
	/// <param name="filePath">Optional file path to use extension as fallback.</param>
	/// <returns>The detected MIME type, or "application/octet-stream" if unknown.</returns>
	public static string DetectMimeType (ReadOnlySpan<byte> data, string? filePath = null)
	{
		// Check magic bytes first
		if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
			return "image/jpeg";

		if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
			data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
			return "image/png";

		if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 &&
			data[3] == 0x38 && (data[4] == 0x37 || data[4] == 0x39) && data[5] == 0x61)
			return "image/gif";

		if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
			return "image/bmp";

		if (data.Length >= 4 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46)
			return "image/webp"; // RIFF header (WebP)

		// Fall back to file extension if provided
		if (!string.IsNullOrEmpty (filePath)) {
			var ext = System.IO.Path.GetExtension (filePath).ToUpperInvariant ();
			return ext switch {
				".JPG" or ".JPEG" => "image/jpeg",
				".PNG" => "image/png",
				".GIF" => "image/gif",
				".BMP" => "image/bmp",
				".WEBP" => "image/webp",
				".TIFF" or ".TIF" => "image/tiff",
				_ => "application/octet-stream"
			};
		}

		return "application/octet-stream";
	}

	/// <summary>
	/// Saves the picture data to a file.
	/// </summary>
	/// <param name="path">The path to save the image to.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public void SaveToFile (string path)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif
		System.IO.File.WriteAllBytes (path, PictureData.ToArray ());
	}

	/// <summary>
	/// Gets the picture data as a stream.
	/// </summary>
	/// <returns>A <see cref="System.IO.MemoryStream"/> containing the picture data.</returns>
	public System.IO.MemoryStream ToStream () => new (PictureData.ToArray (), writable: false);
}
