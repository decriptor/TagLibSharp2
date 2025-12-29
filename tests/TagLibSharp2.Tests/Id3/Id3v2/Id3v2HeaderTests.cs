// Copyright (c) 2025 Stephen Shaw and contributors
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

}
