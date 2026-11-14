// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Id3.Id3v2;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class Id3v2HeaderTests
{
	// ID3v2 Header (10 bytes):
	// Offset  Size  Field
	// 0       3     "ID3" magic
	// 3       1     Major version (2, 3, or 4)
	// 4       1     Minor version (revision)
	// 5       1     Flags
	// 6       4     Size (syncsafe integer)


	[TestMethod]
	public void Read_ValidV24Header_ParsesCorrectly ()
	{
		var data = CreateHeader (version: 4, revision: 0, flags: 0, size: 1024);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4, result.Header.MajorVersion);
		Assert.AreEqual (0, result.Header.MinorVersion);
		Assert.AreEqual (Id3v2HeaderFlags.None, result.Header.Flags);
		Assert.AreEqual (1024u, result.Header.TagSize);
	}

	[TestMethod]
	public void Read_ValidV23Header_ParsesCorrectly ()
	{
		var data = CreateHeader (version: 3, revision: 0, flags: 0, size: 2048);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (3, result.Header.MajorVersion);
		Assert.AreEqual (2048u, result.Header.TagSize);
	}

	[TestMethod]
	public void Read_NoMagic_ReturnsNotFound ()
	{
		var data = new byte[] { (byte)'X', (byte)'Y', (byte)'Z', 4, 0, 0, 0, 0, 0, 0 };

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsNotFound);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0 };

		var result = Id3v2Header.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsFalse (result.IsNotFound);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_UnsupportedVersion_ReturnsFailure ()
	{
		var data = CreateHeader (version: 5, revision: 0, flags: 0, size: 100);

		var result = Id3v2Header.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_InvalidSyncsafe_ReturnsFailure ()
	{
		// Syncsafe bytes must have MSB = 0
		var data = new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0x80, 0, 0, 0 };

		var result = Id3v2Header.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}



	[TestMethod]
	public void Read_UnsyncFlag_DetectedCorrectly ()
	{
		var data = CreateHeader (version: 4, revision: 0, flags: 0x80, size: 100);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Header.Flags.HasFlag (Id3v2HeaderFlags.Unsynchronization));
		Assert.IsTrue (result.Header.IsUnsynchronized);
	}

	[TestMethod]
	public void Read_ExtendedHeaderFlag_DetectedCorrectly ()
	{
		var data = CreateHeader (version: 4, revision: 0, flags: 0x40, size: 100);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Header.Flags.HasFlag (Id3v2HeaderFlags.ExtendedHeader));
		Assert.IsTrue (result.Header.HasExtendedHeader);
	}

	[TestMethod]
	public void Read_FooterFlag_DetectedCorrectly ()
	{
		var data = CreateHeader (version: 4, revision: 0, flags: 0x10, size: 100);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Header.Flags.HasFlag (Id3v2HeaderFlags.Footer));
		Assert.IsTrue (result.Header.HasFooter);
	}

	[TestMethod]
	public void Read_MultipleFlags_AllDetected ()
	{
		var data = CreateHeader (version: 4, revision: 0, flags: 0xF0, size: 100);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Header.IsUnsynchronized);
		Assert.IsTrue (result.Header.HasExtendedHeader);
		Assert.IsTrue (result.Header.IsExperimental);
		Assert.IsTrue (result.Header.HasFooter);
	}



	[TestMethod]
	public void Read_ExtendedHeaderFlagOnV22_IsNotAnExtendedHeader ()
	{
		// ID3v2.2 uses bit 6 for compression. The extended header flag is v2.3 and v2.4.
		var data = CreateHeader (version: 2, revision: 0, flags: 0x40, size: 0);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.Header.HasExtendedHeader);
	}

	[TestMethod]
	public void Read_ExtendedHeaderFlagOnV23_IsAnExtendedHeader ()
	{
		var data = CreateHeader (version: 3, revision: 0, flags: 0x40, size: 100);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.Header.HasExtendedHeader);
	}

	[TestMethod]
	public void Read_FooterFlagOnV23_IsNotAFooter ()
	{
		// Bit 4 is the v2.4 footer. v2.3 does not define it. Treating it as a
		// footer makes TotalSize 10 bytes too big and a save skips the MPEG sync.
		var data = CreateHeader (version: 3, revision: 0, flags: 0x10, size: 0);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsFalse (result.Header.HasFooter);
		Assert.AreEqual (10u, result.Header.TotalSize);
	}

	[TestMethod]
	public void TotalSize_WithoutFooter_IncludesHeaderOnly ()
	{
		var data = CreateHeader (version: 4, revision: 0, flags: 0, size: 1000);

		var result = Id3v2Header.Read (data);

		// Total = 10 (header) + 1000 (tag data)
		Assert.AreEqual (1010u, result.Header.TotalSize);
	}

	[TestMethod]
	public void TotalSize_WithFooter_IncludesHeaderAndFooter ()
	{
		var data = CreateHeader (version: 4, revision: 0, flags: 0x10, size: 1000);

		var result = Id3v2Header.Read (data);

		// Total = 10 (header) + 1000 (tag data) + 10 (footer)
		Assert.AreEqual (1020u, result.Header.TotalSize);
	}

	[TestMethod]
	public void BytesConsumed_Always10 ()
	{
		var data = CreateHeader (version: 4, revision: 0, flags: 0, size: 5000);

		var result = Id3v2Header.Read (data);

		Assert.AreEqual (10, result.BytesConsumed);
	}



	[TestMethod]
	[DataRow (0u, new byte[] { 0, 0, 0, 0 })]
	[DataRow (127u, new byte[] { 0, 0, 0, 0x7F })]
	[DataRow (128u, new byte[] { 0, 0, 1, 0 })]
	[DataRow (256u, new byte[] { 0, 0, 2, 0 })]
	[DataRow (16384u, new byte[] { 0, 1, 0, 0 })]
	[DataRow (268435455u, new byte[] { 0x7F, 0x7F, 0x7F, 0x7F })] // Max syncsafe
	public void Read_SyncsafeSize_DecodesCorrectly (uint expectedSize, byte[] sizeBytes)
	{
		var data = new byte[10];
		data[0] = (byte)'I';
		data[1] = (byte)'D';
		data[2] = (byte)'3';
		data[3] = 4; // version
		data[4] = 0; // revision
		data[5] = 0; // flags
		Array.Copy (sizeBytes, 0, data, 6, 4);

		var result = Id3v2Header.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (expectedSize, result.Header.TagSize);
	}



	[TestMethod]
	public void Render_ValidHeader_Creates10Bytes ()
	{
		var header = new Id3v2Header (4, 0, Id3v2HeaderFlags.None, 1024);

		var data = header.Render ();

		Assert.AreEqual (10, data.Length);
		Assert.IsTrue (data.StartsWith ("ID3"u8));
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesData ()
	{
		var original = new Id3v2Header (4, 0, Id3v2HeaderFlags.Unsynchronization, 5000);

		var rendered = original.Render ();
		var result = Id3v2Header.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.MajorVersion, result.Header.MajorVersion);
		Assert.AreEqual (original.TagSize, result.Header.TagSize);
		Assert.AreEqual (original.Flags, result.Header.Flags);
	}



	// ═══════════════════════════════════════════════════════════════
	// Footer Tests (TDD - ID3v2.4 footer support)
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void RenderFooter_Creates10BytesWithMagic3DI ()
	{
		// ID3v2.4 footer uses "3DI" magic (reverse of "ID3")
		var header = new Id3v2Header (4, 0, Id3v2HeaderFlags.Footer, 1024);

		var footer = header.RenderFooter ();

		Assert.AreEqual (10, footer.Length);
		Assert.AreEqual ((byte)'3', footer[0]);
		Assert.AreEqual ((byte)'D', footer[1]);
		Assert.AreEqual ((byte)'I', footer[2]);
	}

	[TestMethod]
	public void RenderFooter_PreservesVersionAndSize ()
	{
		var header = new Id3v2Header (4, 0, Id3v2HeaderFlags.Footer, 5000);

		var footer = header.RenderFooter ();

		// Version bytes at offset 3-4
		Assert.AreEqual (4, footer[3]);
		Assert.AreEqual (0, footer[4]);
		// Flags at offset 5
		Assert.AreEqual (0x10, footer[5]); // Footer flag
										   // Syncsafe size at offset 6-9 (5000 = 0x00, 0x00, 0x27, 0x08)
		Assert.AreEqual (0, footer[6]);
		Assert.AreEqual (0, footer[7]);
		Assert.AreEqual (39, footer[8]);  // (5000 >> 7) & 0x7F = 39
		Assert.AreEqual (8, footer[9]);   // 5000 & 0x7F = 8
	}

	[TestMethod]
	public void ReadFooter_ValidFooter_ParsesCorrectly ()
	{
		var footerData = CreateFooter (version: 4, revision: 0, flags: 0x10, size: 2048);

		var result = Id3v2Header.ReadFooter (footerData);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4, result.Header.MajorVersion);
		Assert.AreEqual (0x10, (byte)result.Header.Flags);
		Assert.AreEqual (2048u, result.Header.TagSize);
	}

	[TestMethod]
	public void ReadFooter_WrongMagic_ReturnsNotFound ()
	{
		// "ID3" magic instead of "3DI"
		var data = CreateHeader (version: 4, revision: 0, flags: 0x10, size: 1000);

		var result = Id3v2Header.ReadFooter (data);

		Assert.IsTrue (result.IsNotFound);
	}

	[TestMethod]
	public void ReadFooter_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { (byte)'3', (byte)'D', (byte)'I', 4, 0 };

		var result = Id3v2Header.ReadFooter (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsFalse (result.IsNotFound);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void RenderFooter_RoundTrip_PreservesData ()
	{
		var original = new Id3v2Header (4, 0, Id3v2HeaderFlags.Footer | Id3v2HeaderFlags.Unsynchronization, 3000);

		var footer = original.RenderFooter ();
		var result = Id3v2Header.ReadFooter (footer.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.MajorVersion, result.Header.MajorVersion);
		Assert.AreEqual (original.MinorVersion, result.Header.MinorVersion);
		Assert.AreEqual (original.TagSize, result.Header.TagSize);
		Assert.AreEqual (original.Flags, result.Header.Flags);
	}

	[TestMethod]
	public void ReadFooter_V23_ReturnsFailure ()
	{
		// Footer is only valid for v2.4
		var footerData = CreateFooter (version: 3, revision: 0, flags: 0x10, size: 1000);

		var result = Id3v2Header.ReadFooter (footerData);

		// Should reject non-v2.4 footer
		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	static byte[] CreateHeader (byte version, byte revision, byte flags, uint size)
	{
		var data = new byte[10];
		data[0] = (byte)'I';
		data[1] = (byte)'D';
		data[2] = (byte)'3';
		data[3] = version;
		data[4] = revision;
		data[5] = flags;

		// Encode size as syncsafe integer
		data[6] = (byte)((size >> 21) & 0x7F);
		data[7] = (byte)((size >> 14) & 0x7F);
		data[8] = (byte)((size >> 7) & 0x7F);
		data[9] = (byte)(size & 0x7F);

		return data;
	}

	static byte[] CreateFooter (byte version, byte revision, byte flags, uint size)
	{
		var data = new byte[10];
		data[0] = (byte)'3';
		data[1] = (byte)'D';
		data[2] = (byte)'I';
		data[3] = version;
		data[4] = revision;
		data[5] = flags;

		// Encode size as syncsafe integer
		data[6] = (byte)((size >> 21) & 0x7F);
		data[7] = (byte)((size >> 14) & 0x7F);
		data[8] = (byte)((size >> 7) & 0x7F);
		data[9] = (byte)(size & 0x7F);

		return data;
	}

}
