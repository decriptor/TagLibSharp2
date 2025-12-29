// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
[TestCategory ("BWF")]
public class BextTagTests
{
	// Minimum bext chunk is 602 bytes (Version 0/1 fixed portion)
	const int MinBextSize = 602;

	[TestMethod]
	public void Parse_ValidBextChunk_ReturnsTag ()
	{
		var data = CreateMinimalBextChunk ();

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.IsNotNull (tag);
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsNull ()
	{
		var data = new byte[100]; // Way too short

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.IsNull (tag);
	}

	[TestMethod]
	public void Description_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		// Write "Test Description" at offset 0
		var desc = "Test Description"u8;
		desc.CopyTo (data.AsSpan (0, desc.Length));

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual ("Test Description", tag!.Description);
	}

	[TestMethod]
	public void Originator_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		// Originator at offset 256, 32 bytes
		var orig = "MyRecorder"u8;
		orig.CopyTo (data.AsSpan (256, orig.Length));

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual ("MyRecorder", tag!.Originator);
	}

	[TestMethod]
	public void OriginatorReference_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		// OriginatorReference at offset 288, 32 bytes
		var origRef = "REF123456"u8;
		origRef.CopyTo (data.AsSpan (288, origRef.Length));

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual ("REF123456", tag!.OriginatorReference);
	}

	[TestMethod]
	public void OriginationDate_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		// OriginationDate at offset 320, 10 bytes (YYYY-MM-DD)
		var date = "2025-12-28"u8;
		date.CopyTo (data.AsSpan (320, date.Length));

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual ("2025-12-28", tag!.OriginationDate);
	}

	[TestMethod]
	public void OriginationTime_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		// OriginationTime at offset 330, 8 bytes (HH:MM:SS)
		var time = "14:30:00"u8;
		time.CopyTo (data.AsSpan (330, time.Length));

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual ("14:30:00", tag!.OriginationTime);
	}

	[TestMethod]
	public void TimeReference_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		// TimeReference at offset 338, 8 bytes (64-bit LE value as low/high DWORDs)
		// Sample count of 44100 (1 second at 44.1kHz)
		var sampleCount = 44100UL;
		BitConverter.TryWriteBytes (data.AsSpan (338), (uint)(sampleCount & 0xFFFFFFFF));
		BitConverter.TryWriteBytes (data.AsSpan (342), (uint)(sampleCount >> 32));

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual (44100UL, tag!.TimeReference);
	}

	[TestMethod]
	public void TimeReference_LargeValue_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		// Large sample count that uses high DWORD
		var sampleCount = 0x1_0000_0000UL; // 4 billion+
		BitConverter.TryWriteBytes (data.AsSpan (338), (uint)(sampleCount & 0xFFFFFFFF));
		BitConverter.TryWriteBytes (data.AsSpan (342), (uint)(sampleCount >> 32));

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual (0x1_0000_0000UL, tag!.TimeReference);
	}

	[TestMethod]
	public void Version_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		// Version at offset 346, 2 bytes (LE)
		data[346] = 0x02; // Version 2
		data[347] = 0x00;

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual ((ushort)2, tag!.Version);
	}

	[TestMethod]
	public void Umid_Version1_ParsedCorrectly ()
	{
		var data = CreateMinimalBextChunk ();
		data[346] = 0x01; // Version 1
		// UMID at offset 348, 64 bytes
		for (int i = 0; i < 64; i++)
			data[348 + i] = (byte)(i + 1);

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.IsNotNull (tag!.Umid);
		Assert.AreEqual (64, tag.Umid!.Length);
		Assert.AreEqual (1, tag.Umid[0]);
		Assert.AreEqual (64, tag.Umid[63]);
	}

	[TestMethod]
	public void Umid_Version0_IsNull ()
	{
		var data = CreateMinimalBextChunk ();
		data[346] = 0x00; // Version 0

		var tag = BextTag.Parse (new BinaryData (data));

		// Version 0 doesn't have UMID field
		Assert.IsNull (tag!.Umid);
	}

	[TestMethod]
	public void CodingHistory_ParsedCorrectly ()
	{
		var history = "A=PCM,F=44100,W=16,M=stereo\r\n"u8;
		var data = new byte[MinBextSize + history.Length];
		Array.Fill<byte> (data, 0);
		data[346] = 0x00; // Version 0

		// CodingHistory starts at offset 602
		history.CopyTo (data.AsSpan (MinBextSize, history.Length));

		var tag = BextTag.Parse (new BinaryData (data));

		Assert.AreEqual ("A=PCM,F=44100,W=16,M=stereo", tag!.CodingHistory);
	}

	[TestMethod]
	public void IsEmpty_EmptyTag_ReturnsTrue ()
	{
		var tag = new BextTag ();

		Assert.IsTrue (tag.IsEmpty);
	}

	[TestMethod]
	public void IsEmpty_WithDescription_ReturnsFalse ()
	{
		var tag = new BextTag { Description = "Test" };

		Assert.IsFalse (tag.IsEmpty);
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesData ()
	{
		var original = new BextTag {
			Description = "Test Description",
			Originator = "Test Originator",
			OriginatorReference = "REF001",
			OriginationDate = "2025-12-28",
			OriginationTime = "10:30:00",
			TimeReference = 88200,
			Version = 1,
			CodingHistory = "A=PCM,F=44100"
		};

		var rendered = original.Render ();
		var parsed = BextTag.Parse (rendered);

		Assert.IsNotNull (parsed);
		Assert.AreEqual (original.Description, parsed.Description);
		Assert.AreEqual (original.Originator, parsed.Originator);
		Assert.AreEqual (original.OriginatorReference, parsed.OriginatorReference);
		Assert.AreEqual (original.OriginationDate, parsed.OriginationDate);
		Assert.AreEqual (original.OriginationTime, parsed.OriginationTime);
		Assert.AreEqual (original.TimeReference, parsed.TimeReference);
		Assert.AreEqual (original.Version, parsed.Version);
		Assert.AreEqual (original.CodingHistory, parsed.CodingHistory);
	}

	static byte[] CreateMinimalBextChunk ()
	{
		var data = new byte[MinBextSize];
		Array.Fill<byte> (data, 0);
		return data;
	}
}
