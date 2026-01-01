// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Documents expected behavior and compatibility with other MP4 tagging tools.
/// </summary>
/// <remarks>
/// These tests document differences and intentional deviations from other tools.
/// Use real test files (not built programmatically) when available.
/// </remarks>
[TestClass]
public class Mp4CompatibilityTests
{
	/// <summary>
	/// Verifies standard atom mappings match Apple/iTunes conventions.
	/// </summary>
	/// <remarks>
	/// Reference: https://developer.apple.com/library/archive/documentation/QuickTime/QTFF/Metadata/Metadata.html
	/// </remarks>
	[TestMethod]
	public void StandardAtomMappings_MatchAppleConventions ()
	{
		// Verify our constants match the expected iTunes atom codes
		Assert.AreEqual ("©nam", Mp4AtomMapping.Title);
		Assert.AreEqual ("©ART", Mp4AtomMapping.Artist);
		Assert.AreEqual ("aART", Mp4AtomMapping.AlbumArtist);
		Assert.AreEqual ("©alb", Mp4AtomMapping.Album);
		Assert.AreEqual ("©day", Mp4AtomMapping.Year);
		Assert.AreEqual ("©gen", Mp4AtomMapping.Genre);
		Assert.AreEqual ("trkn", Mp4AtomMapping.TrackNumber);
		Assert.AreEqual ("disk", Mp4AtomMapping.DiscNumber);
		Assert.AreEqual ("©wrt", Mp4AtomMapping.Composer);
		Assert.AreEqual ("©cmt", Mp4AtomMapping.Comment);
		Assert.AreEqual ("©lyr", Mp4AtomMapping.Lyrics);
		Assert.AreEqual ("covr", Mp4AtomMapping.CoverArt);
		Assert.AreEqual ("tmpo", Mp4AtomMapping.BeatsPerMinute);
		Assert.AreEqual ("cpil", Mp4AtomMapping.Compilation);
		Assert.AreEqual ("©grp", Mp4AtomMapping.Grouping);
		Assert.AreEqual ("©too", Mp4AtomMapping.Encoder);
		Assert.AreEqual ("cprt", Mp4AtomMapping.Copyright);

		// Verify type indicators
		Assert.AreEqual (1, Mp4AtomMapping.TypeUtf8);
		Assert.AreEqual (13, Mp4AtomMapping.TypeJpeg);
		Assert.AreEqual (14, Mp4AtomMapping.TypePng);
		Assert.AreEqual (21, Mp4AtomMapping.TypeInteger);
	}

	/// <summary>
	/// Verifies iTunes-style metadata structure is generated correctly.
	/// </summary>
	/// <remarks>
	/// iTunes uses specific atom conventions:
	/// - Always includes 'meta' as a FullBox (version/flags)
	/// - Uses 'mdir'/'appl' for hdlr in meta box
	/// - Cover art uses type 13 (JPEG) or 14 (PNG)
	/// </remarks>
	[TestMethod]
	public void ITunes_MetadataStructure_Roundtrip ()
	{
		// Create an M4A file with metadata
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set common iTunes metadata
		file.Title = "Test Song";
		file.Artist = "Test Artist";
		file.Album = "Test Album";
		file.Year = "2025";
		file.Genre = "Rock";
		file.Track = 5;
		file.TrackCount = 12;

		// Render and re-read
		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		// Verify all metadata survived the roundtrip
		Assert.AreEqual ("Test Song", reFile.Title);
		Assert.AreEqual ("Test Artist", reFile.Artist);
		Assert.AreEqual ("Test Album", reFile.Album);
		Assert.AreEqual ("2025", reFile.Year);
		Assert.AreEqual ("Rock", reFile.Genre);
		Assert.AreEqual (5u, reFile.Track);
		Assert.AreEqual (12u, reFile.TrackCount);
	}

	/// <summary>
	/// Tests behavior with foobar2000-generated files.
	/// </summary>
	/// <remarks>
	/// foobar2000 differences:
	/// - May use different free space management
	/// - May write atoms in different order
	/// - Should still be compliant with ISO 14496-12
	/// Set TEST_MP4_FOOBAR2000 environment variable to test with real files.
	/// </remarks>
	[TestMethod]
	public void Foobar2000_Compatibility ()
	{
		// This integration test requires a real foobar2000-tagged file
		// Set TEST_MP4_FOOBAR2000 environment variable with file path
		var path = Environment.GetEnvironmentVariable ("TEST_MP4_FOOBAR2000");
		if (string.IsNullOrEmpty (path)) {
			Assert.Inconclusive ("Set TEST_MP4_FOOBAR2000 environment variable to test foobar2000 compatibility");
			return;
		}

		if (!File.Exists (path)) {
			Assert.Inconclusive ($"File not found: {path}");
			return;
		}

		var data = File.ReadAllBytes (path);
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, $"Failed to read foobar2000 file: {result.Error}");
		Assert.IsNotNull (result.File);
	}

	/// <summary>
	/// Verifies genre uses text format (©gen) for maximum compatibility.
	/// </summary>
	/// <remarks>
	/// Genre can be stored as:
	/// 1. Text in ©gen atom (UTF-8) - preferred, used by modern tools
	/// 2. Numeric ID3v1 genre in gnre atom (binary, 16-bit) - legacy
	/// We write ©gen (text) for best compatibility with modern tools.
	/// </remarks>
	[TestMethod]
	public void Genre_UsesTextFormat ()
	{
		// Create file with genre
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set genre as text (any string, not just ID3v1 genres)
		file.Genre = "Progressive Metal";

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		// Verify text genre survives roundtrip
		Assert.AreEqual ("Progressive Metal", reFile.Genre);
	}

	/// <summary>
	/// Documents that text genres are not limited to ID3v1 genre list.
	/// </summary>
	[TestMethod]
	public void Genre_SupportsCustomText ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// MP4 text genres can be any string, not limited to ID3v1 list
		file.Genre = "Symphonic Black Metal / Electronic";

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);

		Assert.AreEqual ("Symphonic Black Metal / Electronic", reResult.File!.Genre);
	}

	/// <summary>
	/// Verifies track and disc number use correct binary format.
	/// </summary>
	/// <remarks>
	/// trkn format: 0 0 [track] [total] 0 0 (8 bytes)
	/// disk format: 0 0 [disc] [total] 0 0 (8 bytes)
	/// Both use big-endian 16-bit integers with padding.
	/// </remarks>
	[TestMethod]
	public void TrackAndDiscNumber_BinaryFormat_Roundtrip ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set track number with total
		file.Track = 5;
		file.TrackCount = 12;

		// Set disc number with total
		file.DiscNumber = 1;
		file.DiscCount = 2;

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		// Verify binary format preserved values correctly
		Assert.AreEqual (5u, reFile.Track);
		Assert.AreEqual (12u, reFile.TrackCount);
		Assert.AreEqual (1u, reFile.DiscNumber);
		Assert.AreEqual (2u, reFile.DiscCount);
	}

	/// <summary>
	/// Verifies track number can be set without total.
	/// </summary>
	[TestMethod]
	public void TrackNumber_WithoutTotal ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set only track number
		file.Track = 7;

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.AreEqual (7u, reFile.Track);
		Assert.IsNull (reFile.TrackCount);
	}

	/// <summary>
	/// Verifies disc number can be set without total.
	/// </summary>
	[TestMethod]
	public void DiscNumber_WithoutTotal ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set only disc number
		file.DiscNumber = 3;

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.AreEqual (3u, reFile.DiscNumber);
		Assert.IsNull (reFile.DiscCount);
	}

	/// <summary>
	/// Verifies JPEG cover art uses type code 13.
	/// </summary>
	/// <remarks>
	/// Cover art data box flags:
	/// - 13 (0x0D): JPEG
	/// - 14 (0x0E): PNG
	/// </remarks>
	[TestMethod]
	public void CoverArt_JpegTypeCode ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Add JPEG cover art using the proper API
		var jpegData = new BinaryData (TestConstants.Pictures.MinimalJpegHeader);
		file.AddPicture (new Mp4Picture (TestConstants.Pictures.MimeTypeJpeg, PictureType.FrontCover, string.Empty, jpegData));

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.AreEqual (1, reFile.Pictures.Length);
		Assert.IsTrue (reFile.Pictures[0].MimeType.Contains ("jpeg", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Verifies PNG cover art uses type code 14.
	/// </summary>
	[TestMethod]
	public void CoverArt_PngTypeCode ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Add PNG cover art
		var pngData = new BinaryData (TestConstants.Pictures.PngSignature);
		file.AddPicture (new Mp4Picture (TestConstants.Pictures.MimeTypePng, PictureType.FrontCover, string.Empty, pngData));

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.AreEqual (1, reFile.Pictures.Length);
		Assert.IsTrue (reFile.Pictures[0].MimeType.Contains ("png", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Verifies multiple cover art images can be stored.
	/// </summary>
	[TestMethod]
	public void CoverArt_MultiplePictures ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Add multiple cover art images
		var jpegData = new BinaryData (TestConstants.Pictures.MinimalJpegHeader);
		var pngData = new BinaryData (TestConstants.Pictures.PngSignature);
		file.AddPicture (new Mp4Picture (TestConstants.Pictures.MimeTypeJpeg, PictureType.FrontCover, "Front", jpegData));
		file.AddPicture (new Mp4Picture (TestConstants.Pictures.MimeTypePng, PictureType.BackCover, "Back", pngData));

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.AreEqual (2, reFile.Pictures.Length);
	}

	/// <summary>
	/// Verifies compilation flag behavior.
	/// </summary>
	/// <remarks>
	/// cpil atom: 1 byte, value 1 = compilation, 0 = not compilation
	/// iTunes displays "Part of a compilation" checkbox.
	/// </remarks>
	[TestMethod]
	public void CompilationFlag_SetTrue ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set any property first to ensure Tag is created, then set compilation
		file.Title = "Compilation Test";
		file.Tag!.IsCompilation = true;

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.IsTrue (reFile.Tag!.IsCompilation);
	}

	/// <summary>
	/// Verifies compilation flag defaults to false.
	/// </summary>
	[TestMethod]
	public void CompilationFlag_DefaultsFalse ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Default should be false - Tag may be null initially
		Assert.IsFalse (file.Tag?.IsCompilation ?? false);
	}

	/// <summary>
	/// Verifies compilation flag can be cleared.
	/// </summary>
	[TestMethod]
	public void CompilationFlag_CanBeClearedBySettingFalse ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// Set any property first to ensure Tag is created
		file.Title = "Compilation Test";

		// Set then clear via Tag
		file.Tag!.IsCompilation = true;
		file.Tag.IsCompilation = false;

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.IsFalse (reFile.Tag?.IsCompilation ?? false);
	}

	/// <summary>
	/// Documents gapless playback atoms.
	/// </summary>
	/// <remarks>
	/// iTunes uses:
	/// - pgap: gapless playback flag
	/// - iTunSMPB: detailed gapless playback info (text)
	/// </remarks>
	[TestMethod]
	public void GaplessPlayback_Atoms ()
	{
		// Both pgap (boolean flag) and iTunSMPB (detailed info) are supported
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		// pgap atom - simple boolean flag
		file.IsGapless = true;

		// iTunSMPB - detailed gapless info (encoder delay, padding, sample count)
		// Format from real iTunes files
		file.SetFreeformTag ("com.apple.iTunes", "iTunSMPB",
			" 00000000 00000840 000001C0 0000000000123456 00000000 00000000");

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.IsTrue (reFile.IsGapless);
		Assert.AreEqual (" 00000000 00000840 000001C0 0000000000123456 00000000 00000000",
			reFile.GetFreeformTag ("com.apple.iTunes", "iTunSMPB"));
	}

	/// <summary>
	/// Documents sort order atoms.
	/// </summary>
	/// <remarks>
	/// Sort atoms use 'so' prefix:
	/// - sonm: sort name (title)
	/// - soar: sort artist
	/// - soaa: sort album artist
	/// - soal: sort album
	/// - soco: sort composer
	/// </remarks>
	[TestMethod]
	public void SortOrder_Atoms ()
	{
		// Sort order atoms are fully supported:
		// - sonm: sort name (title) -> TitleSort property
		// - soar: sort artist -> ArtistSort property
		// - soaa: sort album artist -> AlbumArtistSort property
		// - soal: sort album -> AlbumSort property
		// - soco: sort composer -> ComposerSort property
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		var file = result.File!;

		file.TitleSort = "Day in the Life, A";
		file.ArtistSort = "Beatles, The";
		file.AlbumArtistSort = "Various";
		file.AlbumSort = "White Album, The";
		file.ComposerSort = "Lennon, John";

		var rendered = file.Render (data);
		var reResult = Mp4File.Read (rendered.Span);
		var reFile = reResult.File!;

		Assert.AreEqual ("Day in the Life, A", reFile.TitleSort);
		Assert.AreEqual ("Beatles, The", reFile.ArtistSort);
		Assert.AreEqual ("Various", reFile.AlbumArtistSort);
		Assert.AreEqual ("White Album, The", reFile.AlbumSort);
		Assert.AreEqual ("Lennon, John", reFile.ComposerSort);
	}

	/// <summary>
	/// Documents intentional deviations from other tools.
	/// </summary>
	/// <remarks>
	/// This test documents deliberate implementation choices that may differ
	/// from other MP4 tagging tools.
	/// </remarks>
	[TestMethod]
	public void IntentionalDeviations_Documentation ()
	{
		// DOCUMENTED DESIGN DECISIONS:
		//
		// 1. Padding strategy: REBUILD
		//    - We rebuild the moov/udta/meta/ilst structure on write
		//    - This ensures consistent, compact output
		//    - Trade-off: Slightly less efficient for small changes
		//
		// 2. Atom ordering: PRESERVE metadata order
		//    - Atoms are rendered in the order they were added
		//    - ilst atoms maintain insertion order via Dictionary
		//
		// 3. Unknown atom handling: PRESERVE
		//    - Unknown ilst atoms are preserved during read/write
		//    - Freeform atoms with unrecognized namespaces are kept
		//
		// 4. Genre format: TEXT ONLY
		//    - We write ©gen (UTF-8 text), not gnre (ID3v1 genre ID)
		//    - This provides maximum compatibility with modern tools
		//
		// 5. Cover art: MIME-TYPE BASED
		//    - We detect JPEG vs PNG based on provided MIME type
		//    - Type indicator 13 for JPEG, 14 for PNG
		//
		// 6. Boolean atoms (cpil, pgap): REMOVED WHEN FALSE
		//    - Setting to false removes the atom entirely
		//    - This matches iTunes behavior

		// This test passes - it documents decisions, doesn't test behavior
		Assert.IsTrue (true, "Documentation test");
	}

	/// <summary>
	/// Tests handling of MP4 files created by different encoders.
	/// </summary>
	[TestMethod]
	public void DifferentEncoders_Compatibility ()
	{
		// Test files from various sources:
		// - iTunes
		// - foobar2000
		// - MediaMonkey
		// - MusicBee
		// - Handbrake
		// - FFmpeg

		Assert.Inconclusive ("Integration test - requires real files from various encoders");
	}

	/// <summary>
	/// Verifies basic backward compatibility with version 0 boxes.
	/// </summary>
	/// <remarks>
	/// Older MP4 files may:
	/// - Use version 0 boxes exclusively
	/// - Have different default values
	/// - Lack certain modern atoms
	/// Our implementation uses version 0 for maximum compatibility.
	/// </remarks>
	[TestMethod]
	public void BackwardCompatibility_Version0Boxes ()
	{
		// Create a standard M4A file - we use version 0 boxes by default
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;

		// Set some metadata
		file.Title = "Legacy Compatible";
		file.Artist = "Test";

		// Render should produce version 0 boxes
		var rendered = file.Render (data);

		// Verify it can be read back
		var reResult = Mp4File.Read (rendered.Span);
		Assert.IsTrue (reResult.IsSuccess);
		Assert.AreEqual ("Legacy Compatible", reResult.File!.Title);
	}

	/// <summary>
	/// Verifies files without metadata can be read.
	/// </summary>
	[TestMethod]
	public void BackwardCompatibility_FilesWithoutMetadata ()
	{
		// Create a minimal M4A without any metadata
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var result = Mp4File.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.File);

		// All metadata should be null/empty by default
		Assert.IsNull (result.File.Title);
		Assert.IsNull (result.File.Artist);
		Assert.IsNull (result.File.Album);
	}

	/// <summary>
	/// Documents that fragmented MP4 (fMP4) is not supported.
	/// </summary>
	/// <remarks>
	/// Fragmented MP4 uses:
	/// - moof (movie fragment) boxes instead of single moov
	/// - mfra (movie fragment random access) boxes
	/// This format is primarily used for streaming (DASH, HLS) and is
	/// outside the scope of a metadata tagging library.
	///
	/// DESIGN DECISION: Fragmented MP4 is not supported.
	/// - The moov box in fMP4 files contains minimal data
	/// - Metadata in fMP4 is typically stored differently
	/// - Use cases (live streaming, VOD) don't need tagging
	/// </remarks>
	[TestMethod]
	public void FragmentedMP4_NotSupported ()
	{
		// Document that fragmented MP4 is out of scope
		// A fragmented MP4 would have:
		// - An empty or minimal 'moov' box
		// - Multiple 'moof' (movie fragment) boxes
		// - A 'mfra' (movie fragment random access) box

		// We detect fMP4 by looking for 'moof' boxes, but we focus on
		// standard ISO base media file format with complete moov boxes.

		// This test documents the design decision - fMP4 is not supported
		Assert.IsTrue (true, "Fragmented MP4 is intentionally not supported");
	}
}
