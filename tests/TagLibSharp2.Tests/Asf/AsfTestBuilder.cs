// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

/// <summary>
/// Utility class for programmatically building ASF/WMA test files.
/// </summary>
public static class AsfTestBuilder
{
	/// <summary>
	/// Creates a minimal valid WMA file with optional metadata.
	/// </summary>
	public static byte[] CreateMinimalWma (
		string? title = null,
		string? artist = null,
		uint durationMs = 1000,
		uint bitrate = 128000,
		uint sampleRate = 44100,
		ushort channels = 2,
		ushort bitsPerSample = 16)
	{
		// Build header child objects
		var fileProps = CreateFilePropertiesObject (durationMs, bitrate);
		var streamProps = CreateAudioStreamPropertiesObject (sampleRate, channels, bitsPerSample);

		var children = new List<byte[]> { fileProps, streamProps };

		if (title is not null || artist is not null)
			children.Add (CreateContentDescriptionObject (title, artist));

		// Build header
		var header = CreateHeaderObject ([.. children]);

		// Build minimal data object (just header, no packets)
		var data = CreateDataObject (0);

		// Combine
		var result = new byte[header.Length + data.Length];
		Array.Copy (header, result, header.Length);
		Array.Copy (data, 0, result, header.Length, data.Length);
		return result;
	}

	/// <summary>
	/// Creates an ASF Header Object containing the specified child objects.
	/// </summary>
	public static byte[] CreateHeaderObject (params byte[][] childObjects)
	{
		// Calculate content size: 6 bytes (object count + reserved) + all children
		var contentSize = 6;
		foreach (var child in childObjects)
			contentSize += child.Length;

		var totalSize = 24 + contentSize; // GUID(16) + Size(8) + content
		var result = new byte[totalSize];
		var offset = 0;

		// Write Header Object GUID
		WriteGuid (result.AsSpan (offset), AsfGuids.HeaderObject);
		offset += 16;

		// Write size
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), (ulong)totalSize);
		offset += 8;

		// Write number of header objects
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), (uint)childObjects.Length);
		offset += 4;

		// Write reserved bytes (0x01, 0x02)
		result[offset++] = 0x01;
		result[offset++] = 0x02;

		// Write child objects
		foreach (var child in childObjects) {
			Array.Copy (child, 0, result, offset, child.Length);
			offset += child.Length;
		}

		return result;
	}

	/// <summary>
	/// Creates a File Properties Object.
	/// </summary>
	public static byte[] CreateFilePropertiesObject (uint durationMs, uint bitrate)
	{
		const int contentSize = 80; // Fixed size per spec
		var result = new byte[24 + contentSize];
		var offset = 0;

		// Write GUID
		WriteGuid (result.AsSpan (offset), AsfGuids.FilePropertiesObject);
		offset += 16;

		// Write size
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), (ulong)result.Length);
		offset += 8;

		// File ID (random GUID)
		WriteGuid (result.AsSpan (offset), AsfGuids.HeaderObject); // Use any GUID
		offset += 16;

		// File size (placeholder)
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), 0);
		offset += 8;

		// Creation date (FILETIME)
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), 0);
		offset += 8;

		// Data packets count
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), 1);
		offset += 8;

		// Play duration (100-nanosecond units)
		var durationNs = (ulong)durationMs * 10000;
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), durationNs);
		offset += 8;

		// Send duration
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), durationNs);
		offset += 8;

		// Preroll (ms)
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), 0);
		offset += 8;

		// Flags
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), 0x02); // Seekable
		offset += 4;

		// Min/Max packet size
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), 0);
		offset += 4;
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), 0);
		offset += 4;

		// Max bitrate
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), bitrate);

		return result;
	}

	/// <summary>
	/// Creates an Audio Stream Properties Object.
	/// </summary>
	public static byte[] CreateAudioStreamPropertiesObject (
		uint sampleRate = 44100,
		ushort channels = 2,
		ushort bitsPerSample = 16)
	{
		// Type-specific data: WAVEFORMATEX (18 bytes min)
		const int waveFormatSize = 18;
		const int contentSize = 54 + waveFormatSize; // Fixed fields + WAVEFORMATEX
		var result = new byte[24 + contentSize];
		var offset = 0;

		// Write GUID
		WriteGuid (result.AsSpan (offset), AsfGuids.StreamPropertiesObject);
		offset += 16;

		// Write size
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), (ulong)result.Length);
		offset += 8;

		// Stream Type GUID (Audio)
		WriteGuid (result.AsSpan (offset), AsfGuids.AudioMediaType);
		offset += 16;

		// Error Correction Type GUID (No Error Correction)
		WriteGuid (result.AsSpan (offset), AsfGuids.NoErrorCorrection);
		offset += 16;

		// Time Offset
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), 0);
		offset += 8;

		// Type-Specific Data Length
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), waveFormatSize);
		offset += 4;

		// Error Correction Data Length
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), 0);
		offset += 4;

		// Flags (stream number = 1)
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), 0x0001);
		offset += 2;

		// Reserved
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), 0);
		offset += 4;

		// WAVEFORMATEX structure
		// wFormatTag (WMA = 0x0161)
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), 0x0161);
		offset += 2;

		// nChannels
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), channels);
		offset += 2;

		// nSamplesPerSec
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), sampleRate);
		offset += 4;

		// nAvgBytesPerSec
		var bytesPerSec = sampleRate * channels * (uint)(bitsPerSample / 8);
		BinaryPrimitives.WriteUInt32LittleEndian (result.AsSpan (offset), bytesPerSec);
		offset += 4;

		// nBlockAlign
		var blockAlign = (ushort)(channels * bitsPerSample / 8);
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), blockAlign);
		offset += 2;

		// wBitsPerSample
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), bitsPerSample);
		offset += 2;

		// cbSize (extra bytes)
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), 0);

		return result;
	}

	/// <summary>
	/// Creates a Content Description Object with 5 fixed fields.
	/// </summary>
	public static byte[] CreateContentDescriptionObject (
		string? title = null,
		string? author = null,
		string? copyright = null,
		string? description = null,
		string? rating = null)
	{
		var titleBytes = CreateUtf16String (title ?? "");
		var authorBytes = CreateUtf16String (author ?? "");
		var copyrightBytes = CreateUtf16String (copyright ?? "");
		var descriptionBytes = CreateUtf16String (description ?? "");
		var ratingBytes = CreateUtf16String (rating ?? "");

		var contentSize = 10 + titleBytes.Length + authorBytes.Length +
			copyrightBytes.Length + descriptionBytes.Length + ratingBytes.Length;
		var result = new byte[24 + contentSize];
		var offset = 0;

		// Write GUID
		WriteGuid (result.AsSpan (offset), AsfGuids.ContentDescriptionObject);
		offset += 16;

		// Write size
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), (ulong)result.Length);
		offset += 8;

		// Write lengths
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)titleBytes.Length);
		offset += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)authorBytes.Length);
		offset += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)copyrightBytes.Length);
		offset += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)descriptionBytes.Length);
		offset += 2;
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)ratingBytes.Length);
		offset += 2;

		// Write strings
		Array.Copy (titleBytes, 0, result, offset, titleBytes.Length);
		offset += titleBytes.Length;
		Array.Copy (authorBytes, 0, result, offset, authorBytes.Length);
		offset += authorBytes.Length;
		Array.Copy (copyrightBytes, 0, result, offset, copyrightBytes.Length);
		offset += copyrightBytes.Length;
		Array.Copy (descriptionBytes, 0, result, offset, descriptionBytes.Length);
		offset += descriptionBytes.Length;
		Array.Copy (ratingBytes, 0, result, offset, ratingBytes.Length);

		return result;
	}

	/// <summary>
	/// Creates an Extended Content Description Object with the specified descriptors.
	/// </summary>
	public static byte[] CreateExtendedContentDescriptionObject (params AsfDescriptor[] descriptors)
	{
		// Calculate content size
		var contentSize = 2; // Descriptor count
		foreach (var desc in descriptors) {
			contentSize += 2; // Name length
			contentSize += desc.RenderName ().Length;
			contentSize += 2; // Value type
			contentSize += 2; // Value length
			contentSize += desc.RenderValue ().Length;
		}

		var result = new byte[24 + contentSize];
		var offset = 0;

		// Write GUID
		WriteGuid (result.AsSpan (offset), AsfGuids.ExtendedContentDescriptionObject);
		offset += 16;

		// Write size
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), (ulong)result.Length);
		offset += 8;

		// Write descriptor count
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)descriptors.Length);
		offset += 2;

		// Write descriptors
		foreach (var desc in descriptors) {
			var nameBytes = desc.RenderName ().ToArray ();
			var valueBytes = desc.RenderValue ().ToArray ();

			// Name length
			BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)nameBytes.Length);
			offset += 2;

			// Name
			Array.Copy (nameBytes, 0, result, offset, nameBytes.Length);
			offset += nameBytes.Length;

			// Value type
			BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)desc.Type);
			offset += 2;

			// Value length
			BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), (ushort)valueBytes.Length);
			offset += 2;

			// Value
			Array.Copy (valueBytes, 0, result, offset, valueBytes.Length);
			offset += valueBytes.Length;
		}

		return result;
	}

	/// <summary>
	/// Creates a minimal Data Object.
	/// </summary>
	public static byte[] CreateDataObject (ulong packetCount)
	{
		const int contentSize = 26; // Fixed content size (File ID + packet count + reserved)
		var result = new byte[24 + contentSize];
		var offset = 0;

		// Write GUID
		WriteGuid (result.AsSpan (offset), AsfGuids.DataObject);
		offset += 16;

		// Write size
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), (ulong)result.Length);
		offset += 8;

		// File ID
		WriteGuid (result.AsSpan (offset), AsfGuids.HeaderObject);
		offset += 16;

		// Total data packets
		BinaryPrimitives.WriteUInt64LittleEndian (result.AsSpan (offset), packetCount);
		offset += 8;

		// Reserved
		BinaryPrimitives.WriteUInt16LittleEndian (result.AsSpan (offset), 0x0101);

		return result;
	}

	/// <summary>
	/// Creates a UTF-16LE encoded string with null terminator.
	/// </summary>
	public static byte[] CreateUtf16String (string value)
	{
		var bytes = Encoding.Unicode.GetBytes (value);
		var result = new byte[bytes.Length + 2]; // +2 for null terminator
		Array.Copy (bytes, result, bytes.Length);
		return result;
	}

	/// <summary>
	/// Writes an AsfGuid to a span.
	/// </summary>
	static void WriteGuid (Span<byte> destination, AsfGuid guid)
	{
		var bytes = guid.Render ().ToArray ();
		bytes.CopyTo (destination);
	}
}
