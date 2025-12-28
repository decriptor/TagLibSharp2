// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Flac")]
public class FlacCueSheetTests
{
	[TestMethod]
	public void Constructor_SetsDefaults ()
	{
		var cueSheet = new FlacCueSheet ();

		Assert.IsEmpty (cueSheet.Tracks);
		Assert.AreEqual ("", cueSheet.MediaCatalogNumber);
		Assert.AreEqual ((ulong)0, cueSheet.LeadInSamples);
		Assert.IsFalse (cueSheet.IsCompactDisc);
	}

	[TestMethod]
	public void AddTrack_AddsTrack ()
	{
		var cueSheet = new FlacCueSheet ();
		var track = new FlacCueSheetTrack (1, 0);

		cueSheet.AddTrack (track);

		Assert.HasCount (1, cueSheet.Tracks);
		Assert.AreEqual (1, cueSheet.Tracks[0].TrackNumber);
	}

	[TestMethod]
	public void ClearTracks_RemovesAllTracks ()
	{
		var cueSheet = new FlacCueSheet ();
		cueSheet.AddTrack (new FlacCueSheetTrack (1, 0));
		cueSheet.AddTrack (new FlacCueSheetTrack (2, 44100));

		cueSheet.ClearTracks ();

		Assert.IsEmpty (cueSheet.Tracks);
	}

	[TestMethod]
	public void Read_MinimalCueSheet_Succeeds ()
	{
		// Build minimal CUESHEET: catalog(128) + lead-in(8) + flags(1) + reserved(258) + numTracks(1) + leadout track(36)
		// Total minimum = 396 + 36 = 432 bytes
		var data = BuildMinimalCueSheet ();

		var result = FlacCueSheet.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.CueSheet);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[10]; // Way too short

		var result = FlacCueSheet.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_WithCatalogNumber_ParsesCatalog ()
	{
		var data = BuildCueSheetWithCatalog ("1234567890123");

		var result = FlacCueSheet.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("1234567890123", result.CueSheet!.MediaCatalogNumber);
	}

	[TestMethod]
	public void Read_CompactDiscFlag_ParsesFlag ()
	{
		var data = BuildMinimalCueSheet (isCompactDisc: true);

		var result = FlacCueSheet.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.CueSheet!.IsCompactDisc);
	}

	[TestMethod]
	public void Read_WithLeadIn_ParsesLeadIn ()
	{
		var data = BuildMinimalCueSheet (leadInSamples: 88200); // 2 seconds at 44.1kHz

		var result = FlacCueSheet.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ((ulong)88200, result.CueSheet!.LeadInSamples);
	}

	[TestMethod]
	public void Read_WithTracks_ParsesTracks ()
	{
		var cueSheet = new FlacCueSheet {
			MediaCatalogNumber = "1234567890123",
			IsCompactDisc = true,
			LeadInSamples = 88200
		};

		// Track 1 at offset 0
		var track1 = new FlacCueSheetTrack (1, 0);
		track1.AddIndex (new FlacCueSheetIndex (1, 0));
		cueSheet.AddTrack (track1);

		// Track 2 at 5 minutes
		var track2 = new FlacCueSheetTrack (2, 44100 * 60 * 5);
		track2.AddIndex (new FlacCueSheetIndex (1, 0));
		cueSheet.AddTrack (track2);

		// Lead-out track (track 170 for CD audio)
		var leadOut = new FlacCueSheetTrack (170, 44100 * 60 * 10);
		cueSheet.AddTrack (leadOut);

		var rendered = cueSheet.Render ();
		var result = FlacCueSheet.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (3, result.CueSheet!.Tracks);
		Assert.AreEqual (1, result.CueSheet.Tracks[0].TrackNumber);
		Assert.AreEqual (2, result.CueSheet.Tracks[1].TrackNumber);
		Assert.AreEqual (170, result.CueSheet.Tracks[2].TrackNumber);
	}

	[TestMethod]
	public void RenderContent_RoundTrips ()
	{
		var cueSheet = new FlacCueSheet {
			MediaCatalogNumber = "1234567890123",
			IsCompactDisc = true,
			LeadInSamples = 88200
		};

		var track = new FlacCueSheetTrack (1, 0) { Isrc = "USRC17607839" };
		track.AddIndex (new FlacCueSheetIndex (0, 0)); // Pre-gap
		track.AddIndex (new FlacCueSheetIndex (1, 150 * 588)); // Index 1
		cueSheet.AddTrack (track);

		cueSheet.AddTrack (new FlacCueSheetTrack (170, 44100 * 60 * 5)); // Lead-out

		var rendered = cueSheet.Render ();
		var result = FlacCueSheet.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("1234567890123", result.CueSheet!.MediaCatalogNumber);
		Assert.IsTrue (result.CueSheet.IsCompactDisc);
		Assert.AreEqual ((ulong)88200, result.CueSheet.LeadInSamples);
		Assert.HasCount (2, result.CueSheet.Tracks);
		Assert.AreEqual ("USRC17607839", result.CueSheet.Tracks[0].Isrc);
		Assert.HasCount (2, result.CueSheet.Tracks[0].Indices);
	}

	// Helper methods to build test data

	static byte[] BuildMinimalCueSheet (bool isCompactDisc = false, ulong leadInSamples = 0)
	{
		using var builder = new BinaryDataBuilder ();

		// Media catalog number (128 bytes, null-padded)
		builder.AddZeros (128);

		// Lead-in samples (8 bytes, big-endian)
		AddUInt64BE (builder, leadInSamples);

		// Flags: 1 bit for compact disc, 7 bits + 258 bytes reserved
		builder.Add ((byte)(isCompactDisc ? 0x80 : 0x00));
		builder.AddZeros (258);

		// Number of tracks (1 byte) - minimum 1 for lead-out
		builder.Add ((byte)1);

		// Lead-out track (track 170 for CD)
		AddTrack (builder, 170, 0, isAudio: false, isLeadOut: true);

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildCueSheetWithCatalog (string catalog)
	{
		using var builder = new BinaryDataBuilder ();

		// Media catalog number (128 bytes, null-padded ASCII)
		var catalogBytes = System.Text.Encoding.ASCII.GetBytes (catalog);
		builder.Add (catalogBytes);
		builder.AddZeros (128 - catalogBytes.Length);

		// Lead-in samples (8 bytes)
		builder.AddZeros (8);

		// Flags + reserved (259 bytes)
		builder.Add ((byte)0x00);
		builder.AddZeros (258);

		// Number of tracks (1 byte)
		builder.Add ((byte)1);

		// Lead-out track
		AddTrack (builder, 170, 0, isAudio: false, isLeadOut: true);

		return builder.ToBinaryData ().ToArray ();
	}

	static void AddTrack (BinaryDataBuilder builder, byte trackNumber, ulong offset, bool isAudio = true, bool isLeadOut = false)
	{
		// Track offset (8 bytes, big-endian)
		AddUInt64BE (builder, offset);

		// Track number (1 byte)
		builder.Add (trackNumber);

		// ISRC (12 bytes, null for lead-out)
		builder.AddZeros (12);

		// Flags: bit 0 = non-audio (0 for audio), bit 1 = pre-emphasis
		builder.Add ((byte)(isAudio ? 0x00 : 0x80));

		// Reserved (13 bytes)
		builder.AddZeros (13);

		// Number of track indices (1 byte) - lead-out has 0
		builder.Add ((byte)(isLeadOut ? 0 : 1));

		// Add index point if not lead-out
		if (!isLeadOut)
			AddIndex (builder, 1, 0);
	}

	static void AddIndex (BinaryDataBuilder builder, byte indexNumber, ulong offset)
	{
		// Index offset (8 bytes, big-endian)
		AddUInt64BE (builder, offset);

		// Index number (1 byte)
		builder.Add (indexNumber);

		// Reserved (3 bytes)
		builder.AddZeros (3);
	}

	static void AddUInt64BE (BinaryDataBuilder builder, ulong value)
	{
		builder.Add ((byte)((value >> 56) & 0xFF));
		builder.Add ((byte)((value >> 48) & 0xFF));
		builder.Add ((byte)((value >> 40) & 0xFF));
		builder.Add ((byte)((value >> 32) & 0xFF));
		builder.Add ((byte)((value >> 24) & 0xFF));
		builder.Add ((byte)((value >> 16) & 0xFF));
		builder.Add ((byte)((value >> 8) & 0xFF));
		builder.Add ((byte)(value & 0xFF));
	}
}
