// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Tests that verify MP4 data preservation through read-write-read cycles.
/// </summary>
[TestClass]
public class Mp4RoundTripTests
{
	[TestMethod]
	public void MinimalFile_PreservesStructure ()
	{
		// Arrange
		var original = Mp4TestBuilder.CreateMinimalM4a ("Test Title", "Test Artist");

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Implement when Mp4File is available
			// var file1 = Mp4File.Parse(original);
			// var written = file1.Save();
			// var file2 = Mp4File.Parse(written);
			//
			// Assert.AreEqual(file1.Tag.Title, file2.Tag.Title);
			// Assert.AreEqual(file1.Tag.Artist, file2.Tag.Artist);
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void AllMetadataFields_SurviveRoundTrip ()
	{
		// Arrange: Create file with all standard metadata
		var metadata = new[] {
			("©nam", "Title"),
			("©ART", "Artist"),
			("©alb", "Album"),
			("©day", "2025"),
			("©gen", "Rock"),
			("trkn", "5/12"), // Track number special format
			("disk", "1/2"),  // Disc number special format
			("©wrt", "Composer"),
			("©cmt", "Comment"),
		};

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Build file with all metadata, round-trip, verify all fields
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void CoverArt_PreservesBinaryData ()
	{
		// Arrange: JPEG cover art
		var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Add cover art, round-trip, verify bytes are identical
			// var original = Mp4TestBuilder.CreateMinimalM4a();
			// // Add cover art
			// var written = save();
			// var reloaded = parse(written);
			// CollectionAssert.AreEqual(jpegData, reloaded.Tag.Pictures[0].Data);
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void UnknownBoxes_ArePreserved ()
	{
		// Arrange: File with custom/unknown box
		var builder = new BinaryDataBuilder ();
		builder.Add (Mp4TestBuilder.CreateFtypBox ("M4A "));

		var customData = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
		var customBox = Mp4TestBuilder.CreateBox ("CUST", customData);

		builder.Add (customBox);
		builder.Add (Mp4TestBuilder.CreateMinimalM4a ());

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Unknown boxes should be preserved during read-write
			// This is important for compatibility with extended formats
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void MediaData_RemainsUnchanged ()
	{
		// Arrange
		var originalMdat = new byte[1024];
		Array.Fill (originalMdat, (byte)0x42);

		var builder = new BinaryDataBuilder ();
		builder.Add (Mp4TestBuilder.CreateFtypBox ("M4A "));
		builder.Add (Mp4TestBuilder.CreateMinimalM4a ());
		builder.Add (Mp4TestBuilder.CreateBox ("mdat", originalMdat));

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: mdat box should be untouched (we only edit metadata)
			// var file = parse();
			// file.Tag.Title = "New Title";
			// var written = file.Save();
			//
			// // Extract mdat from written file
			// // Verify it's byte-for-byte identical to originalMdat
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void BoxOrder_IsPreserved ()
	{
		// Arrange: Non-standard box order
		var builder = new BinaryDataBuilder ();
		builder.Add (Mp4TestBuilder.CreateBox ("free", [0x00, 0x00])); // Free space first
		builder.Add (Mp4TestBuilder.CreateFtypBox ("M4A "));
		builder.Add (Mp4TestBuilder.CreateMinimalM4a ());

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Box order should be maintained (or documented if reordered)
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void EmptyMetadata_ToPopulated_ToEmpty ()
	{
		// Arrange: Start with no metadata
		var file = Mp4TestBuilder.CreateMinimalM4a ();

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Test cycle: empty -> add metadata -> remove all -> verify clean
			// 1. Parse file with no ilst
			// 2. Add Title/Artist
			// 3. Save
			// 4. Remove all metadata
			// 5. Save
			// 6. Verify ilst is gone or empty
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void Unicode_AllScripts ()
	{
		// Arrange: Metadata with various Unicode scripts
		var metadata = new[] {
			("©nam", "Título"),           // Latin with diacritics
			("©ART", "芸術家"),            // Japanese
			("©alb", "Альбом"),           // Cyrillic
			("©cmt", "تعليق"),            // Arabic
			("©gen", "Μουσική"),          // Greek
			("aART", "😀🎵🎸"),            // Emoji
		};

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: All Unicode should survive round-trip
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void LargeMetadata_70KB ()
	{
		// Arrange: Very long comment (lyrics)
		var longComment = new string ('X', 70000);

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Large metadata should be preserved
			// May require multiple passes or streaming
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void MultipleWrites_NoDataGrowth ()
	{
		// Arrange
		var original = Mp4TestBuilder.CreateMinimalM4a ("Title", "Artist");

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Multiple save operations shouldn't inflate file size
			// 1. Parse file
			// 2. Save (size1)
			// 3. Parse again
			// 4. Save (size2)
			// 5. Assert size1 == size2 (or very close, accounting for padding)
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void ExtendedSizeBox_RoundTrip ()
	{
		// Arrange: Box with 64-bit extended size
		var largeData = new byte[100000];
		var extBox = Mp4TestBuilder.CreateExtendedSizeBox ("free", largeData);

		var builder = new BinaryDataBuilder ();
		builder.Add (Mp4TestBuilder.CreateFtypBox ("M4A "));
		builder.Add (extBox);
		builder.Add (Mp4TestBuilder.CreateMinimalM4a ());

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Extended size should be preserved or converted correctly
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void CustomAtoms_NotInStandardSet ()
	{
		// Arrange: Custom metadata atoms (non-standard)
		var metadata = new[] {
			("XYZW", "Custom Value"),
			("©xyz", "Another Custom"),
		};

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Custom atoms should be preserved
			// Important for compatibility with other taggers
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void FreeBoxes_Padding ()
	{
		// Arrange: File with free/skip boxes (padding)
		var builder = new BinaryDataBuilder ();
		builder.Add (Mp4TestBuilder.CreateFtypBox ("M4A "));
		builder.Add (Mp4TestBuilder.CreateBox ("free", new byte[512]));
		builder.Add (Mp4TestBuilder.CreateMinimalM4a ("Title", "Artist"));
		builder.Add (Mp4TestBuilder.CreateBox ("skip", new byte[256]));

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Padding strategy
			// Option 1: Preserve free boxes
			// Option 2: Remove and add padding as needed
			// Document the chosen behavior
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void TrackNumber_BinaryFormat ()
	{
		// Arrange: trkn uses binary format, not text
		// Format: 0 0 track total 0 0
		var trkn = new BinaryDataBuilder ();
		trkn.AddUInt16BE (0);
		trkn.AddUInt16BE (5);  // track
		trkn.AddUInt16BE (12); // total
		trkn.AddUInt16BE (0);

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Binary track number format should round-trip correctly
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}

	[TestMethod]
	public void TimeValues_32BitAnd64Bit ()
	{
		// Arrange: mvhd can be version 0 (32-bit) or version 1 (64-bit)
		var mvhdV0 = Mp4TestBuilder.CreateMvhdBox (1000, 1000, 0);
		var mvhdV1 = Mp4TestBuilder.CreateMvhdBox (1000, 1000, 1);

		// Act & Assert
		Assert.ThrowsExactly<NotImplementedException> (() => {
			// TODO: Both versions should parse correctly and preserve version
			throw new NotImplementedException ("Mp4File not yet implemented");
		});
	}
}
