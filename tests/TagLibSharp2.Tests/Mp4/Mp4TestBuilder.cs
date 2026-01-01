// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Builder for constructing valid MP4/M4A byte sequences programmatically.
/// </summary>
/// <remarks>
/// MP4 is based on ISO Base Media File Format (ISO 14496-12).
/// Structure: Boxes/Atoms with 4-byte size + 4-byte type + data.
/// </remarks>
public static class Mp4TestBuilder
{
	/// <summary>
	/// Creates a minimal valid M4A file with optional metadata.
	/// </summary>
	/// <param name="title">Optional title metadata.</param>
	/// <param name="artist">Optional artist metadata.</param>
	/// <param name="durationMs">Duration in milliseconds (default: 1000).</param>
	/// <param name="timescale">Timescale for duration (default: 1000).</param>
	/// <returns>Complete M4A file bytes.</returns>
	public static byte[] CreateMinimalM4a (string? title = null, string? artist = null, uint durationMs = 1000, uint timescale = 1000)
	{
		var builder = new BinaryDataBuilder ();

		// ftyp box (file type)
		builder.Add (CreateFtypBox ("M4A "));

		// moov box (movie/metadata container)
		var moovChildren = new List<byte[]> ();

		// mvhd box (movie header)
		moovChildren.Add (CreateMvhdBox (durationMs, timescale));

		// trak box (track)
		moovChildren.Add (CreateTrakBox (durationMs, timescale));

		// udta box with meta box containing ilst (metadata)
		if (title is not null || artist is not null) {
			var metadataItems = new List<(string atom, string value)> ();
			if (title is not null)
				metadataItems.Add (("©nam", title));
			if (artist is not null)
				metadataItems.Add (("©ART", artist));
			moovChildren.Add (CreateUdtaBox (metadataItems.ToArray ()));
		}

		builder.Add (CreateMoovBox (moovChildren.ToArray ()));

		// mdat box (media data - minimal placeholder)
		builder.Add (CreateBox ("mdat", [0x00, 0x00, 0x00, 0x00]));

		return builder.ToArray ();
	}

	/// <summary>
	/// Creates an ftyp (file type) box.
	/// </summary>
	/// <param name="majorBrand">4-character major brand (e.g., "M4A ", "isom", "mp42").</param>
	/// <param name="minorVersion">Minor version (default: 0).</param>
	/// <param name="compatibleBrands">Array of 4-character compatible brands.</param>
	/// <returns>Complete ftyp box bytes.</returns>
	public static byte[] CreateFtypBox (string majorBrand = "M4A ", uint minorVersion = 0, params string[] compatibleBrands)
	{
		var content = new BinaryDataBuilder ();
		content.Add (Encoding.ASCII.GetBytes (majorBrand.PadRight (4)[..4]));
		content.AddUInt32BE (minorVersion);

		// Add compatible brands (default to major brand if none specified)
		if (compatibleBrands.Length == 0)
			compatibleBrands = [majorBrand];

		foreach (var brand in compatibleBrands)
			content.Add (Encoding.ASCII.GetBytes (brand.PadRight (4)[..4]));

		return CreateBox ("ftyp", content.ToArray ());
	}

	/// <summary>
	/// Creates a moov (movie) box containing child boxes.
	/// </summary>
	/// <param name="children">Child box data.</param>
	/// <returns>Complete moov box bytes.</returns>
	public static byte[] CreateMoovBox (params byte[][] children)
	{
		var content = new BinaryDataBuilder ();
		foreach (var child in children)
			content.Add (child);
		return CreateBox ("moov", content.ToArray ());
	}

	/// <summary>
	/// Creates an mvhd (movie header) box.
	/// </summary>
	/// <param name="duration">Duration in timescale units.</param>
	/// <param name="timescale">Timescale (units per second).</param>
	/// <param name="version">Version (0 for 32-bit, 1 for 64-bit).</param>
	/// <returns>Complete mvhd box bytes.</returns>
	public static byte[] CreateMvhdBox (uint duration, uint timescale, byte version = 0)
	{
		var content = new BinaryDataBuilder ();

		// Version and flags
		content.Add (version);
		content.AddUInt24BE (0); // flags

		if (version == 0) {
			// Version 0: 32-bit values
			content.AddUInt32BE (0); // creation_time
			content.AddUInt32BE (0); // modification_time
			content.AddUInt32BE (timescale);
			content.AddUInt32BE (duration);
		} else {
			// Version 1: 64-bit values
			content.AddUInt64BE (0); // creation_time
			content.AddUInt64BE (0); // modification_time
			content.AddUInt32BE (timescale);
			content.AddUInt64BE (duration);
		}

		// Fixed-point rate (1.0)
		content.AddInt32BE (0x00010000);
		// Fixed-point volume (1.0)
		content.AddInt16BE (0x0100);
		// Reserved
		content.AddZeros (10);
		// Unity matrix (9 x 32-bit values)
		content.AddInt32BE (0x00010000);
		content.AddZeros (12);
		content.AddInt32BE (0x00010000);
		content.AddZeros (12);
		content.AddInt32BE (0x40000000);
		// pre_defined
		content.AddZeros (24);
		// next_track_ID
		content.AddUInt32BE (2);

		return CreateFullBox ("mvhd", version, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates a trak (track) box with minimal audio track data.
	/// </summary>
	/// <param name="duration">Track duration in timescale units.</param>
	/// <param name="timescale">Timescale (units per second).</param>
	/// <returns>Complete trak box bytes.</returns>
	public static byte[] CreateTrakBox (uint duration, uint timescale)
	{
		var children = new List<byte[]> ();

		// tkhd (track header)
		children.Add (CreateTkhdBox (duration, timescale));

		// mdia (media) box
		children.Add (CreateMdiaBox (duration, timescale));

		var content = new BinaryDataBuilder ();
		foreach (var child in children)
			content.Add (child);

		return CreateBox ("trak", content.ToArray ());
	}

	/// <summary>
	/// Creates a tkhd (track header) box.
	/// </summary>
	static byte[] CreateTkhdBox (uint duration, uint timescale, byte version = 0)
	{
		var content = new BinaryDataBuilder ();

		// Version and flags (track enabled, in movie, in preview)
		content.Add (version);
		content.AddUInt24BE (0x07);

		if (version == 0) {
			content.AddUInt32BE (0); // creation_time
			content.AddUInt32BE (0); // modification_time
			content.AddUInt32BE (1); // track_ID
			content.AddUInt32BE (0); // reserved
			content.AddUInt32BE (duration);
		} else {
			content.AddUInt64BE (0); // creation_time
			content.AddUInt64BE (0); // modification_time
			content.AddUInt32BE (1); // track_ID
			content.AddUInt32BE (0); // reserved
			content.AddUInt64BE (duration);
		}

		content.AddZeros (8); // reserved
		content.AddInt16BE (0); // layer
		content.AddInt16BE (0); // alternate_group
		content.AddInt16BE (0x0100); // volume (1.0 for audio)
		content.AddZeros (2); // reserved
		// Unity matrix
		content.AddInt32BE (0x00010000);
		content.AddZeros (12);
		content.AddInt32BE (0x00010000);
		content.AddZeros (12);
		content.AddInt32BE (0x40000000);
		// Width and height (0 for audio)
		content.AddUInt32BE (0);
		content.AddUInt32BE (0);

		return CreateFullBox ("tkhd", version, 0x07, content.ToArray ());
	}

	/// <summary>
	/// Creates an mdia (media) box.
	/// </summary>
	static byte[] CreateMdiaBox (uint duration, uint timescale)
	{
		var children = new List<byte[]> ();

		// mdhd (media header)
		children.Add (CreateMdhdBox (duration, timescale));

		// hdlr (handler reference)
		children.Add (CreateHdlrBox ("soun"));

		// minf (media information)
		children.Add (CreateMinfBox ());

		var content = new BinaryDataBuilder ();
		foreach (var child in children)
			content.Add (child);

		return CreateBox ("mdia", content.ToArray ());
	}

	/// <summary>
	/// Creates an mdhd (media header) box.
	/// </summary>
	static byte[] CreateMdhdBox (uint duration, uint timescale, byte version = 0)
	{
		var content = new BinaryDataBuilder ();

		content.Add (version);
		content.AddUInt24BE (0); // flags

		if (version == 0) {
			content.AddUInt32BE (0); // creation_time
			content.AddUInt32BE (0); // modification_time
			content.AddUInt32BE (timescale);
			content.AddUInt32BE (duration);
		} else {
			content.AddUInt64BE (0); // creation_time
			content.AddUInt64BE (0); // modification_time
			content.AddUInt32BE (timescale);
			content.AddUInt64BE (duration);
		}

		// Language (ISO 639-2/T, "und" = undetermined = 0x55C4)
		content.AddUInt16BE (0x55C4);
		// pre_defined
		content.AddUInt16BE (0);

		return CreateFullBox ("mdhd", version, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates an hdlr (handler reference) box.
	/// </summary>
	static byte[] CreateHdlrBox (string handlerType = "soun", string name = "SoundHandler")
	{
		var content = new BinaryDataBuilder ();

		content.Add ((byte)0); // version
		content.AddUInt24BE (0); // flags
		content.AddUInt32BE (0); // pre_defined
		content.Add (Encoding.ASCII.GetBytes (handlerType.PadRight (4)[..4]));
		content.AddZeros (12); // reserved
		content.AddStringUtf8NullTerminated (name);

		return CreateFullBox ("hdlr", 0, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates a minimal minf (media information) box.
	/// </summary>
	static byte[] CreateMinfBox ()
	{
		var children = new List<byte[]> ();

		// smhd (sound media header)
		children.Add (CreateSmhdBox ());

		// dinf (data information)
		children.Add (CreateDinfBox ());

		// stbl (sample table)
		children.Add (CreateStblBox ());

		var content = new BinaryDataBuilder ();
		foreach (var child in children)
			content.Add (child);

		return CreateBox ("minf", content.ToArray ());
	}

	/// <summary>
	/// Creates an smhd (sound media header) box.
	/// </summary>
	static byte[] CreateSmhdBox ()
	{
		var content = new BinaryDataBuilder ();
		content.Add ((byte)0); // version
		content.AddUInt24BE (0); // flags
		content.AddInt16BE (0); // balance
		content.AddUInt16BE (0); // reserved
		return CreateFullBox ("smhd", 0, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates a dinf (data information) box.
	/// </summary>
	static byte[] CreateDinfBox ()
	{
		// Contains dref box
		var dref = CreateDrefBox ();
		return CreateBox ("dinf", dref);
	}

	/// <summary>
	/// Creates a dref (data reference) box.
	/// </summary>
	static byte[] CreateDrefBox ()
	{
		var content = new BinaryDataBuilder ();
		content.Add ((byte)0); // version
		content.AddUInt24BE (0); // flags
		content.AddUInt32BE (1); // entry_count

		// url box with "self-contained" flag
		var url = new BinaryDataBuilder ();
		url.Add ((byte)0); // version
		url.AddUInt24BE (0x01); // flags (self-contained)
		content.Add (CreateFullBox ("url ", 0, 0x01, url.ToArray ()));

		return CreateFullBox ("dref", 0, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates a minimal stbl (sample table) box.
	/// </summary>
	static byte[] CreateStblBox ()
	{
		var children = new List<byte[]> ();

		// stsd (sample description)
		children.Add (CreateStsdBox ());

		// stts (time-to-sample)
		children.Add (CreateSttsBox ());

		// stsc (sample-to-chunk)
		children.Add (CreateStscBox ());

		// stsz (sample sizes)
		children.Add (CreateStszBox ());

		// stco (chunk offsets)
		children.Add (CreateStcoBox ());

		var content = new BinaryDataBuilder ();
		foreach (var child in children)
			content.Add (child);

		return CreateBox ("stbl", content.ToArray ());
	}

	/// <summary>
	/// Creates a minimal stsd (sample description) box.
	/// </summary>
	static byte[] CreateStsdBox ()
	{
		var content = new BinaryDataBuilder ();
		content.Add ((byte)0); // version
		content.AddUInt24BE (0); // flags
		content.AddUInt32BE (1); // entry_count

		// mp4a audio sample entry (minimal)
		var mp4a = new BinaryDataBuilder ();
		mp4a.AddZeros (6); // reserved
		mp4a.AddUInt16BE (1); // data_reference_index
		mp4a.AddZeros (8); // reserved
		mp4a.AddUInt16BE (2); // channel_count
		mp4a.AddUInt16BE (16); // sample_size
		mp4a.AddUInt16BE (0); // pre_defined
		mp4a.AddUInt16BE (0); // reserved
		mp4a.AddUInt32BE (unchecked((uint)(44100 << 16))); // sample_rate (16.16 fixed-point)

		content.Add (CreateBox ("mp4a", mp4a.ToArray ()));

		return CreateFullBox ("stsd", 0, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates an empty stts (time-to-sample) box.
	/// </summary>
	static byte[] CreateSttsBox ()
	{
		var content = new BinaryDataBuilder ();
		content.Add ((byte)0); // version
		content.AddUInt24BE (0); // flags
		content.AddUInt32BE (0); // entry_count
		return CreateFullBox ("stts", 0, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates an empty stsc (sample-to-chunk) box.
	/// </summary>
	static byte[] CreateStscBox ()
	{
		var content = new BinaryDataBuilder ();
		content.Add ((byte)0); // version
		content.AddUInt24BE (0); // flags
		content.AddUInt32BE (0); // entry_count
		return CreateFullBox ("stsc", 0, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates an empty stsz (sample sizes) box.
	/// </summary>
	static byte[] CreateStszBox ()
	{
		var content = new BinaryDataBuilder ();
		content.Add ((byte)0); // version
		content.AddUInt24BE (0); // flags
		content.AddUInt32BE (0); // sample_size
		content.AddUInt32BE (0); // sample_count
		return CreateFullBox ("stsz", 0, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates an empty stco (chunk offsets) box.
	/// </summary>
	static byte[] CreateStcoBox ()
	{
		var content = new BinaryDataBuilder ();
		content.Add ((byte)0); // version
		content.AddUInt24BE (0); // flags
		content.AddUInt32BE (0); // entry_count
		return CreateFullBox ("stco", 0, 0, content.ToArray ());
	}

	/// <summary>
	/// Creates a udta (user data) box containing meta box with metadata.
	/// </summary>
	static byte[] CreateUdtaBox (params (string atom, string value)[] metadata)
	{
		// meta box contains hdlr and ilst
		var metaContent = new BinaryDataBuilder ();
		metaContent.Add ((byte)0); // version
		metaContent.AddUInt24BE (0); // flags

		// hdlr for metadata
		var hdlr = new BinaryDataBuilder ();
		hdlr.Add ((byte)0); // version
		hdlr.AddUInt24BE (0); // flags
		hdlr.AddUInt32BE (0); // pre_defined
		hdlr.Add (Encoding.ASCII.GetBytes ("mdir"));
		hdlr.Add (Encoding.ASCII.GetBytes ("appl"));
		hdlr.AddZeros (8); // reserved
		hdlr.AddStringUtf8NullTerminated ("");
		metaContent.Add (CreateFullBox ("hdlr", 0, 0, hdlr.ToArray ()));

		// ilst box contains metadata items
		metaContent.Add (CreateIlstBox (metadata));

		var meta = CreateFullBox ("meta", 0, 0, metaContent.ToArray ());

		return CreateBox ("udta", meta);
	}

	/// <summary>
	/// Creates an ilst (item list) box containing metadata atoms.
	/// </summary>
	/// <param name="metadata">Array of (atom name, value) tuples.</param>
	/// <returns>Complete ilst box bytes.</returns>
	public static byte[] CreateIlstBox (params (string atom, string value)[] metadata)
	{
		var content = new BinaryDataBuilder ();

		foreach (var (atom, value) in metadata) {
			// Each metadata item: atom -> data box
			var dataContent = new BinaryDataBuilder ();
			dataContent.Add ((byte)0); // version
			dataContent.AddUInt24BE (1); // flags (UTF-8 text)
			dataContent.AddUInt32BE (0); // reserved
			dataContent.AddStringUtf8 (value);

			var dataBox = CreateFullBox ("data", 0, 1, dataContent.ToArray ());

			content.Add (CreateBox (atom, dataBox));
		}

		return CreateBox ("ilst", content.ToArray ());
	}

	/// <summary>
	/// Creates a covr (cover art) box with image data.
	/// </summary>
	/// <param name="imageData">Raw image data.</param>
	/// <param name="isJpeg">True for JPEG, false for PNG.</param>
	/// <returns>Complete covr box bytes.</returns>
	public static byte[] CreateCovrBox (byte[] imageData, bool isJpeg = true)
	{
		var dataContent = new BinaryDataBuilder ();
		dataContent.Add ((byte)0); // version
		dataContent.AddUInt24BE ((uint)(isJpeg ? 0x0D : 0x0E)); // flags (JPEG=13, PNG=14)
		dataContent.AddUInt32BE (0); // reserved
		dataContent.Add (imageData);

		var dataBox = CreateFullBox ("data", 0, (uint)(isJpeg ? 0x0D : 0x0E), dataContent.ToArray ());

		return CreateBox ("covr", dataBox);
	}

	/// <summary>
	/// Creates a generic MP4 box with 4-byte size and 4-byte type.
	/// </summary>
	/// <param name="type">4-character box type.</param>
	/// <param name="data">Box data (after size and type).</param>
	/// <returns>Complete box bytes.</returns>
	public static byte[] CreateBox (string type, byte[] data)
	{
		var size = 8u + (uint)data.Length; // 4-byte size + 4-byte type + data
		var builder = new BinaryDataBuilder ();

		builder.AddUInt32BE (size);
		builder.Add (Encoding.ASCII.GetBytes (type.PadRight (4)[..4]));
		builder.Add (data);

		return builder.ToArray ();
	}

	/// <summary>
	/// Creates a FullBox (box with version and flags).
	/// </summary>
	/// <param name="type">4-character box type.</param>
	/// <param name="version">Version byte.</param>
	/// <param name="flags">24-bit flags.</param>
	/// <param name="data">Box data (after version/flags).</param>
	/// <returns>Complete box bytes.</returns>
	public static byte[] CreateFullBox (string type, byte version, uint flags, byte[] data)
	{
		var content = new BinaryDataBuilder ();
		content.Add (version);
		content.AddUInt24BE (flags & 0xFFFFFF);
		content.Add (data);

		return CreateBox (type, content.ToArray ());
	}

	/// <summary>
	/// Creates a box with extended 64-bit size.
	/// </summary>
	/// <param name="type">4-character box type.</param>
	/// <param name="data">Box data.</param>
	/// <returns>Complete box bytes with extended size.</returns>
	public static byte[] CreateExtendedSizeBox (string type, byte[] data)
	{
		var size = 16uL + (ulong)data.Length; // 4-byte marker + 4-byte type + 8-byte size + data
		var builder = new BinaryDataBuilder ();

		builder.AddUInt32BE (1); // Size = 1 indicates extended size
		builder.Add (Encoding.ASCII.GetBytes (type.PadRight (4)[..4]));
		builder.AddUInt64BE (size);
		builder.Add (data);

		return builder.ToArray ();
	}
}
