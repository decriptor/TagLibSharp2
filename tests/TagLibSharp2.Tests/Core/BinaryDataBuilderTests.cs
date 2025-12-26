// Copyright (c) 2025 Stephen Shaw and contributors

using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Core")]
public class BinaryDataBuilderTests
{
	// Constructor Tests

	[TestMethod]
	public void Constructor_Default_HasDefaultCapacity ()
	{
		var builder = new BinaryDataBuilder ();

		Assert.AreEqual (0, builder.Length);
		Assert.AreEqual (256, builder.Capacity);
	}

	[TestMethod]
	public void Constructor_WithCapacity_SetsCapacity ()
	{
		var builder = new BinaryDataBuilder (1024);

		Assert.AreEqual (0, builder.Length);
		Assert.AreEqual (1024, builder.Capacity);
	}

	[TestMethod]
	public void Constructor_ZeroCapacity_CreatesEmptyBuffer ()
	{
		var builder = new BinaryDataBuilder (0);

		Assert.AreEqual (0, builder.Length);
		Assert.AreEqual (0, builder.Capacity);
	}

	[TestMethod]
	public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRange ()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new BinaryDataBuilder (-1));
	}

	// Add Single Byte Tests

	[TestMethod]
	public void Add_SingleByte_AppendsToEnd ()
	{
		var builder = new BinaryDataBuilder ();

		builder.Add (0x42);

		Assert.AreEqual (1, builder.Length);
		Assert.AreEqual ((byte)0x42, builder[0]);
	}

	[TestMethod]
	public void Add_MultipleSingleBytes_AppendsInOrder ()
	{
		var builder = new BinaryDataBuilder ();

		builder.Add (0x01).Add (0x02).Add (0x03);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
		Assert.AreEqual ((byte)0x02, builder[1]);
		Assert.AreEqual ((byte)0x03, builder[2]);
	}

	// Add Byte Array Tests

	[TestMethod]
	public void Add_ByteArray_AppendsAll ()
	{
		var builder = new BinaryDataBuilder ();

		builder.Add (0x01, 0x02, 0x03);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
		Assert.AreEqual ((byte)0x03, builder[2]);
	}

	[TestMethod]
	public void Add_EmptyArray_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		builder.Add (Array.Empty<byte> ());

		Assert.AreEqual (1, builder.Length);
	}

	[TestMethod]
	public void Add_NullArray_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		builder.Add ((byte[]?)null!);

		Assert.AreEqual (1, builder.Length);
	}

	// Add BinaryData Tests

	[TestMethod]
	public void Add_BinaryData_AppendsAll ()
	{
		var builder = new BinaryDataBuilder ();
		var data = new BinaryData ([0x01, 0x02, 0x03]);

		builder.Add (data);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
	}

	[TestMethod]
	public void Add_EmptyBinaryData_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		builder.Add (BinaryData.Empty);

		Assert.AreEqual (1, builder.Length);
	}

	// AddZeros / AddFill Tests

	[TestMethod]
	public void AddZeros_AddsZeroBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddZeros (5);

		Assert.AreEqual (5, builder.Length);
		Assert.AreEqual ((byte)0x00, builder[0]);
		Assert.AreEqual ((byte)0x00, builder[4]);
	}

	[TestMethod]
	public void AddZeros_ZeroCount_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddZeros (0);

		Assert.AreEqual (0, builder.Length);
	}

	[TestMethod]
	public void AddFill_AddsFilledBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddFill (0xFF, 3);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0xFF, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[2]);
	}

	// Integer Add Tests - Big Endian

	[TestMethod]
	public void AddUInt16BE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt16BE (0x1234);

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)0x12, builder[0]);
		Assert.AreEqual ((byte)0x34, builder[1]);
	}

	[TestMethod]
	public void AddUInt32BE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt32BE (0x12345678);

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x12, builder[0]);
		Assert.AreEqual ((byte)0x34, builder[1]);
		Assert.AreEqual ((byte)0x56, builder[2]);
		Assert.AreEqual ((byte)0x78, builder[3]);
	}

	[TestMethod]
	public void AddUInt64BE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt64BE (0x123456789ABCDEF0);

		Assert.AreEqual (8, builder.Length);
		Assert.AreEqual ((byte)0x12, builder[0]);
		Assert.AreEqual ((byte)0xF0, builder[7]);
	}

	[TestMethod]
	public void AddInt16BE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddInt16BE (-1); // 0xFFFF

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)0xFF, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[1]);
	}

	[TestMethod]
	public void AddInt32BE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddInt32BE (-1); // 0xFFFFFFFF

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0xFF, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[3]);
	}

	// Integer Add Tests - Little Endian

	[TestMethod]
	public void AddUInt16LE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt16LE (0x1234);

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)0x34, builder[0]);
		Assert.AreEqual ((byte)0x12, builder[1]);
	}

	[TestMethod]
	public void AddUInt32LE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt32LE (0x12345678);

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x78, builder[0]);
		Assert.AreEqual ((byte)0x56, builder[1]);
		Assert.AreEqual ((byte)0x34, builder[2]);
		Assert.AreEqual ((byte)0x12, builder[3]);
	}

	[TestMethod]
	public void AddUInt64LE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt64LE (0x123456789ABCDEF0);

		Assert.AreEqual (8, builder.Length);
		Assert.AreEqual ((byte)0xF0, builder[0]);
		Assert.AreEqual ((byte)0x12, builder[7]);
	}

	// Special Integer Formats

	[TestMethod]
	public void AddUInt24BE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt24BE (0x123456);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0x12, builder[0]);
		Assert.AreEqual ((byte)0x34, builder[1]);
		Assert.AreEqual ((byte)0x56, builder[2]);
	}

	[TestMethod]
	public void AddUInt24BE_ExceedsMax_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.AddUInt24BE (0x1000000));
	}

	[TestMethod]
	public void AddUInt24LE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt24LE (0x123456);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0x56, builder[0]);
		Assert.AreEqual ((byte)0x34, builder[1]);
		Assert.AreEqual ((byte)0x12, builder[2]);
	}

	[TestMethod]
	public void AddUInt24LE_ExceedsMax_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.AddUInt24LE (0x1000000));
	}

	[TestMethod]
	public void AddSyncSafeUInt32_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddSyncSafeUInt32 (0x0FFFFFFF); // Max syncsafe value

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x7F, builder[0]);
		Assert.AreEqual ((byte)0x7F, builder[1]);
		Assert.AreEqual ((byte)0x7F, builder[2]);
		Assert.AreEqual ((byte)0x7F, builder[3]);
	}

	[TestMethod]
	public void AddSyncSafeUInt32_SmallValue_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddSyncSafeUInt32 (127);

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x00, builder[0]);
		Assert.AreEqual ((byte)0x00, builder[1]);
		Assert.AreEqual ((byte)0x00, builder[2]);
		Assert.AreEqual ((byte)0x7F, builder[3]);
	}

	[TestMethod]
	public void AddSyncSafeUInt32_ExceedsMax_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.AddSyncSafeUInt32 (0x10000000));
	}

	// String Add Tests

	[TestMethod]
	public void AddStringLatin1_EncodesCorrectly ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringLatin1 ("ABC");

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)'A', builder[0]);
		Assert.AreEqual ((byte)'B', builder[1]);
		Assert.AreEqual ((byte)'C', builder[2]);
	}

	[TestMethod]
	public void AddStringUtf8_EncodesCorrectly ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringUtf8 ("ABC");

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)'A', builder[0]);
	}

	[TestMethod]
	public void AddStringUtf8_MultiByteChar_EncodesCorrectly ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringUtf8 ("\u00E9"); // é - 2 bytes in UTF-8

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)0xC3, builder[0]);
		Assert.AreEqual ((byte)0xA9, builder[1]);
	}

	[TestMethod]
	public void AddStringUtf16_WithBom_IncludesBom ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringUtf16 ("A", includeBom: true);

		Assert.AreEqual (4, builder.Length); // 2 BOM + 2 char
		Assert.AreEqual ((byte)0xFF, builder[0]); // BOM
		Assert.AreEqual ((byte)0xFE, builder[1]);
		Assert.AreEqual ((byte)'A', builder[2]);
		Assert.AreEqual ((byte)0x00, builder[3]);
	}

	[TestMethod]
	public void AddStringUtf16_WithoutBom_NoBom ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringUtf16 ("A", includeBom: false);

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)'A', builder[0]);
		Assert.AreEqual ((byte)0x00, builder[1]);
	}

	[TestMethod]
	public void AddStringLatin1NullTerminated_AddsNullByte ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringLatin1NullTerminated ("AB");

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)'A', builder[0]);
		Assert.AreEqual ((byte)'B', builder[1]);
		Assert.AreEqual ((byte)0x00, builder[2]);
	}

	[TestMethod]
	public void AddStringUtf8NullTerminated_AddsNullByte ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringUtf8NullTerminated ("AB");

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0x00, builder[2]);
	}

	[TestMethod]
	public void AddStringUtf16NullTerminated_AddsDoubleNull ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringUtf16NullTerminated ("A", includeBom: false);

		Assert.AreEqual (4, builder.Length); // 2 char + 2 null
		Assert.AreEqual ((byte)0x00, builder[2]);
		Assert.AreEqual ((byte)0x00, builder[3]);
	}

	[TestMethod]
	public void AddString_NullValue_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		builder.AddStringLatin1 (null!);

		Assert.AreEqual (1, builder.Length);
	}

	[TestMethod]
	public void AddString_EmptyValue_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		builder.AddStringLatin1 ("");

		Assert.AreEqual (1, builder.Length);
	}

	// Insert Tests

	[TestMethod]
	public void Insert_AtStart_ShiftsExisting ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x03, 0x04);

		builder.Insert (0, new BinaryData ([0x01, 0x02]));

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
		Assert.AreEqual ((byte)0x02, builder[1]);
		Assert.AreEqual ((byte)0x03, builder[2]);
		Assert.AreEqual ((byte)0x04, builder[3]);
	}

	[TestMethod]
	public void Insert_InMiddle_ShiftsRemaining ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x04);

		builder.Insert (1, new BinaryData ([0x02, 0x03]));

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
		Assert.AreEqual ((byte)0x02, builder[1]);
		Assert.AreEqual ((byte)0x03, builder[2]);
		Assert.AreEqual ((byte)0x04, builder[3]);
	}

	[TestMethod]
	public void Insert_AtEnd_AppendsData ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		builder.Insert (2, new BinaryData ([0x03, 0x04]));

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x03, builder[2]);
		Assert.AreEqual ((byte)0x04, builder[3]);
	}

	[TestMethod]
	public void Insert_BeyondLength_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.Insert (5, new BinaryData ([0x02])));
	}

	[TestMethod]
	public void Insert_EmptySpan_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		builder.Insert (1, ReadOnlySpan<byte>.Empty);

		Assert.AreEqual (2, builder.Length);
	}

	// RemoveRange Tests

	[TestMethod]
	public void RemoveRange_FromStart_ShiftsData ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03, 0x04);

		builder.RemoveRange (0, 2);

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)0x03, builder[0]);
		Assert.AreEqual ((byte)0x04, builder[1]);
	}

	[TestMethod]
	public void RemoveRange_FromMiddle_ShiftsRemaining ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03, 0x04);

		builder.RemoveRange (1, 2);

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
		Assert.AreEqual ((byte)0x04, builder[1]);
	}

	[TestMethod]
	public void RemoveRange_FromEnd_TruncatesData ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03, 0x04);

		builder.RemoveRange (2, 2);

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
		Assert.AreEqual ((byte)0x02, builder[1]);
	}

	[TestMethod]
	public void RemoveRange_ZeroCount_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		builder.RemoveRange (1, 0);

		Assert.AreEqual (2, builder.Length);
	}

	[TestMethod]
	public void RemoveRange_InvalidIndex_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.RemoveRange (5, 1));
	}

	[TestMethod]
	public void RemoveRange_CountExceedsBounds_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.RemoveRange (1, 5));
	}

	// Clear/Reset Tests

	[TestMethod]
	public void Clear_ResetsLengthButKeepsCapacity ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);
		var capacityBefore = builder.Capacity;

		builder.Clear ();

		Assert.AreEqual (0, builder.Length);
		Assert.AreEqual (capacityBefore, builder.Capacity);
	}

	[TestMethod]
	public void Reset_ResetsLengthAndCapacity ()
	{
		var builder = new BinaryDataBuilder (1024);
		builder.Add (0x01, 0x02, 0x03);

		builder.Reset ();

		Assert.AreEqual (0, builder.Length);
		Assert.AreEqual (0, builder.Capacity);
	}

	// ToBinaryData / ToArray Tests

	[TestMethod]
	public void ToBinaryData_ReturnsCorrectData ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);

		var result = builder.ToBinaryData ();

		Assert.AreEqual (3, result.Length);
		Assert.AreEqual ((byte)0x01, result[0]);
		Assert.AreEqual ((byte)0x02, result[1]);
		Assert.AreEqual ((byte)0x03, result[2]);
	}

	[TestMethod]
	public void ToBinaryData_Empty_ReturnsEmpty ()
	{
		var builder = new BinaryDataBuilder ();

		var result = builder.ToBinaryData ();

		Assert.IsTrue (result.IsEmpty);
	}

	[TestMethod]
	public void ToBinaryData_IsImmutableCopy ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		var result = builder.ToBinaryData ();
		builder.Add (0x03); // Modify after

		Assert.AreEqual (2, result.Length); // Original unchanged
		Assert.AreEqual (3, builder.Length);
	}

	[TestMethod]
	public void ToArray_ReturnsCorrectData ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);

		var result = builder.ToArray ();

		Assert.HasCount (3, result);
		Assert.AreEqual ((byte)0x01, result[0]);
	}

	// Indexer Tests

	[TestMethod]
	public void Indexer_Get_ReturnsCorrectByte ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);

		Assert.AreEqual ((byte)0x02, builder[1]);
	}

	[TestMethod]
	public void Indexer_Set_ModifiesByte ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);

		builder[1] = 0xFF;

		Assert.AreEqual ((byte)0xFF, builder[1]);
	}

	[TestMethod]
	public void Indexer_NegativeIndex_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => _ = builder[-1]);
	}

	[TestMethod]
	public void Indexer_IndexEqualsLength_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => _ = builder[2]);
	}

	// Span/Memory Tests

	[TestMethod]
	public void Span_ReturnsCurrentData ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);

		var span = builder.Span;

		Assert.AreEqual (3, span.Length);
		Assert.AreEqual ((byte)0x01, span[0]);
	}

	[TestMethod]
	public void Memory_ReturnsCurrentData ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);

		var memory = builder.Memory;

		Assert.AreEqual (3, memory.Length);
		Assert.AreEqual ((byte)0x01, memory.Span[0]);
	}

	// Capacity Growth Tests

	[TestMethod]
	public void Add_ExceedsCapacity_GrowsBuffer ()
	{
		var builder = new BinaryDataBuilder (4);
		builder.Add (0x01, 0x02, 0x03, 0x04);
		var originalCapacity = builder.Capacity;

		builder.Add (0x05);

		Assert.IsGreaterThan (originalCapacity, builder.Capacity);
		Assert.AreEqual (5, builder.Length);
		Assert.AreEqual ((byte)0x05, builder[4]);
	}

	[TestMethod]
	public void EnsureCapacity_GrowsIfNeeded ()
	{
		var builder = new BinaryDataBuilder (4);

		builder.EnsureCapacity (100);

		Assert.IsGreaterThanOrEqualTo (100, builder.Capacity);
	}

	[TestMethod]
	public void EnsureCapacity_DoesNotShrink ()
	{
		var builder = new BinaryDataBuilder (100);

		builder.EnsureCapacity (10);

		Assert.AreEqual (100, builder.Capacity);
	}

	// Fluent API Tests

	[TestMethod]
	public void FluentApi_ChainsCorrectly ()
	{
		var result = new BinaryDataBuilder ()
			.Add (0x49)
			.Add (0x44)
			.Add (0x33)
			.AddUInt32BE (256)
			.AddStringLatin1 ("Test")
			.ToBinaryData ();

		Assert.AreEqual (11, result.Length);
		Assert.AreEqual ((byte)'I', result[0]);
		Assert.AreEqual ((byte)'D', result[1]);
		Assert.AreEqual ((byte)'3', result[2]);
	}

	// Real-world Usage Tests

	[TestMethod]
	public void BuildId3v2Header_CorrectFormat ()
	{
		var result = new BinaryDataBuilder ()
			.Add (0x49, 0x44, 0x33) // "ID3"
			.Add (0x04, 0x00)       // Version 2.4.0
			.Add (0x00)             // Flags
			.AddSyncSafeUInt32 (1000) // Size
			.ToBinaryData ();

		Assert.AreEqual (10, result.Length);
		Assert.AreEqual ((byte)'I', result[0]);
		Assert.AreEqual ((byte)'D', result[1]);
		Assert.AreEqual ((byte)'3', result[2]);
		Assert.AreEqual ((byte)0x04, result[3]); // Major version
		Assert.AreEqual ((byte)0x00, result[4]); // Minor version
	}

	[TestMethod]
	public void BuildVorbisComment_CorrectFormat ()
	{
		var vendor = "TagLibSharp2";
		var comment = "ARTIST=Test Artist";

		var result = new BinaryDataBuilder ()
			.AddUInt32LE ((uint)vendor.Length)
			.AddStringUtf8 (vendor)
			.AddUInt32LE (1) // Comment count
			.AddUInt32LE ((uint)comment.Length)
			.AddStringUtf8 (comment)
			.ToBinaryData ();

		// 4 (vendor length) + 12 (vendor) + 4 (count) + 4 (comment length) + 18 (comment)
		Assert.AreEqual (42, result.Length);
		Assert.AreEqual (12u, result.ToUInt32LE ()); // Vendor length
	}

	// Exception Tests - Missing Coverage

	[TestMethod]
	public void AddString_NullEncoding_ThrowsArgumentNullException ()
	{
		var builder = new BinaryDataBuilder ();

		Assert.ThrowsExactly<ArgumentNullException> (() =>
			builder.AddString ("test", null!));
	}

	[TestMethod]
	public void AddZeros_NegativeCount_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddZeros (-5);

		Assert.AreEqual (0, builder.Length);
	}

	[TestMethod]
	public void AddFill_NegativeCount_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddFill (0xFF, -5);

		Assert.AreEqual (0, builder.Length);
	}

	[TestMethod]
	public void RemoveRange_NegativeIndex_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.RemoveRange (-1, 1));
	}

	[TestMethod]
	public void RemoveRange_NegativeCount_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.RemoveRange (0, -1));
	}

	[TestMethod]
	public void Insert_NegativeIndex_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder.Insert (-1, new BinaryData ([0x03])));
	}

	[TestMethod]
	public void Indexer_Set_OutOfRange_ThrowsArgumentOutOfRange ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02);

		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			builder[5] = 0xFF);
	}

	[TestMethod]
	public void Add_ToZeroCapacityBuilder_GrowsBuffer ()
	{
		var builder = new BinaryDataBuilder (0);

		builder.Add (0x01);

		Assert.AreEqual (1, builder.Length);
		Assert.IsGreaterThan (0, builder.Capacity);
		Assert.AreEqual ((byte)0x01, builder[0]);
	}

	[TestMethod]
	public void Insert_RequiresGrow_ExpandsBuffer ()
	{
		var builder = new BinaryDataBuilder (4);
		builder.Add (0x01, 0x02, 0x03, 0x04); // At capacity

		builder.Insert (2, new BinaryData ([0xAA, 0xBB])); // Should grow

		Assert.AreEqual (6, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
		Assert.AreEqual ((byte)0xAA, builder[2]);
		Assert.AreEqual ((byte)0xBB, builder[3]);
	}

	[TestMethod]
	public void Clear_Span_ReturnsEmpty ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);

		builder.Clear ();

		Assert.IsTrue (builder.Span.IsEmpty);
	}

	[TestMethod]
	public void Reset_Memory_ReturnsEmpty ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03);

		builder.Reset ();

		Assert.IsTrue (builder.Memory.IsEmpty);
	}

	[TestMethod]
	public void AddUInt24BE_MaxValue_WritesCorrectly ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt24BE (0xFFFFFF);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0xFF, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[1]);
		Assert.AreEqual ((byte)0xFF, builder[2]);
	}

	[TestMethod]
	public void AddUInt24LE_MaxValue_WritesCorrectly ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt24LE (0xFFFFFF);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0xFF, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[1]);
		Assert.AreEqual ((byte)0xFF, builder[2]);
	}

	[TestMethod]
	public void AddInt16LE_NegativeValue_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddInt16LE (-1);

		Assert.AreEqual (2, builder.Length);
		Assert.AreEqual ((byte)0xFF, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[1]);
	}

	[TestMethod]
	public void AddInt64BE_NegativeValue_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddInt64BE (-1);

		Assert.AreEqual (8, builder.Length);
		Assert.AreEqual ((byte)0xFF, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[7]);
	}

	[TestMethod]
	public void AddInt64LE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddInt64LE (0x123456789ABCDEF0);

		Assert.AreEqual (8, builder.Length);
		Assert.AreEqual ((byte)0xF0, builder[0]);
		Assert.AreEqual ((byte)0x12, builder[7]);
	}

	// Missing Coverage - Priority 2: Public API Coverage

	[TestMethod]
	public void AddInt32LE_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddInt32LE (0x12345678);

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x78, builder[0]);
		Assert.AreEqual ((byte)0x56, builder[1]);
		Assert.AreEqual ((byte)0x34, builder[2]);
		Assert.AreEqual ((byte)0x12, builder[3]);
	}

	[TestMethod]
	public void AddFill_ZeroCount_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		builder.AddFill (0xFF, 0);

		Assert.AreEqual (1, builder.Length);
	}

	[TestMethod]
	public void AddString_EmptyString_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		builder.AddString ("", Encoding.UTF8);

		Assert.AreEqual (1, builder.Length);
	}

	[TestMethod]
	public void AddString_NullString_DoesNothing ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01);

		builder.AddString (null!, Encoding.UTF8);

		Assert.AreEqual (1, builder.Length);
	}

	// Missing Coverage - Priority 3: Integration & Real-World Patterns

	[TestMethod]
	public void MultipleInserts_MaintainsDataIntegrity ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x05);

		builder.Insert (1, new BinaryData ([0x02]));
		builder.Insert (2, new BinaryData ([0x03]));
		builder.Insert (3, new BinaryData ([0x04]));

		Assert.AreEqual (5, builder.Length);
		Assert.AreEqual ((byte)0x01, builder[0]);
		Assert.AreEqual ((byte)0x02, builder[1]);
		Assert.AreEqual ((byte)0x03, builder[2]);
		Assert.AreEqual ((byte)0x04, builder[3]);
		Assert.AreEqual ((byte)0x05, builder[4]);
	}

	[TestMethod]
	public void InterleavedInsertAndRemove_MaintainsConsistency ()
	{
		var builder = new BinaryDataBuilder ();
		builder.Add (0x01, 0x02, 0x03, 0x04, 0x05);

		builder.RemoveRange (2, 1); // Remove 0x03 -> [0x01, 0x02, 0x04, 0x05]
		builder.Insert (2, new BinaryData ([0xAA, 0xBB])); // Insert at 2 -> [0x01, 0x02, 0xAA, 0xBB, 0x04, 0x05]
		builder.RemoveRange (0, 1); // Remove 0x01 -> [0x02, 0xAA, 0xBB, 0x04, 0x05]

		Assert.AreEqual (5, builder.Length);
		Assert.AreEqual ((byte)0x02, builder[0]);
		Assert.AreEqual ((byte)0xAA, builder[1]);
		Assert.AreEqual ((byte)0xBB, builder[2]);
		Assert.AreEqual ((byte)0x04, builder[3]);
		Assert.AreEqual ((byte)0x05, builder[4]);
	}

	[TestMethod]
	public void LargeDataAddition_HandlesMultipleGrows ()
	{
		var builder = new BinaryDataBuilder (4); // Start with tiny capacity
		var totalBytes = 1000;

		for (int i = 0; i < totalBytes; i++)
			builder.Add ((byte)(i & 0xFF));

		Assert.AreEqual (totalBytes, builder.Length);
		Assert.IsGreaterThanOrEqualTo (totalBytes, builder.Capacity);
		Assert.AreEqual ((byte)0x00, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[255]);
		Assert.AreEqual ((byte)0x00, builder[256]);
	}

	[TestMethod]
	public void AddSpan_LargeSpan_GrowsCorrectly ()
	{
		var builder = new BinaryDataBuilder (4); // Start with tiny capacity
		var largeData = new byte[100];
		for (int i = 0; i < largeData.Length; i++)
			largeData[i] = (byte)i;

		builder.Add (largeData.AsSpan ());

		Assert.AreEqual (100, builder.Length);
		Assert.IsGreaterThanOrEqualTo (100, builder.Capacity);
		Assert.AreEqual ((byte)0x00, builder[0]);
		Assert.AreEqual ((byte)0x63, builder[99]);
	}

	// Missing Coverage - Priority 4: Boundary Value Completeness

	[TestMethod]
	public void AddInt64LE_NegativeValue_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddInt64LE (-1);

		Assert.AreEqual (8, builder.Length);
		Assert.AreEqual ((byte)0xFF, builder[0]);
		Assert.AreEqual ((byte)0xFF, builder[7]);
	}

	[TestMethod]
	public void AddUInt24BE_ZeroValue_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt24BE (0);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0x00, builder[0]);
		Assert.AreEqual ((byte)0x00, builder[1]);
		Assert.AreEqual ((byte)0x00, builder[2]);
	}

	[TestMethod]
	public void AddUInt24LE_ZeroValue_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddUInt24LE (0);

		Assert.AreEqual (3, builder.Length);
		Assert.AreEqual ((byte)0x00, builder[0]);
		Assert.AreEqual ((byte)0x00, builder[1]);
		Assert.AreEqual ((byte)0x00, builder[2]);
	}

	[TestMethod]
	public void AddSyncSafeUInt32_ZeroValue_WritesCorrectBytes ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddSyncSafeUInt32 (0);

		Assert.AreEqual (4, builder.Length);
		Assert.AreEqual ((byte)0x00, builder[0]);
		Assert.AreEqual ((byte)0x00, builder[1]);
		Assert.AreEqual ((byte)0x00, builder[2]);
		Assert.AreEqual ((byte)0x00, builder[3]);
	}

	[TestMethod]
	public void AddStringUtf16NullTerminated_WithBom_IncludesBomAndDoubleNull ()
	{
		var builder = new BinaryDataBuilder ();

		builder.AddStringUtf16NullTerminated ("A", includeBom: true);

		Assert.AreEqual (6, builder.Length); // 2 BOM + 2 char + 2 null
		Assert.AreEqual ((byte)0xFF, builder[0]); // BOM
		Assert.AreEqual ((byte)0xFE, builder[1]);
		Assert.AreEqual ((byte)'A', builder[2]);
		Assert.AreEqual ((byte)0x00, builder[3]);
		Assert.AreEqual ((byte)0x00, builder[4]); // Double null
		Assert.AreEqual ((byte)0x00, builder[5]);
	}

	// Grow after Reset test - verifies minimum capacity floor fix

	[TestMethod]
	public void Reset_ThenAdd_GrowsToDefaultCapacity ()
	{
		var builder = new BinaryDataBuilder (1024);
		builder.Add (0x01, 0x02, 0x03);
		builder.Reset (); // Buffer is now empty []

		builder.Add (0x04); // Should grow to DefaultCapacity (256), not 1

		Assert.AreEqual (1, builder.Length);
		Assert.AreEqual (256, builder.Capacity); // Minimum capacity floor
		Assert.AreEqual ((byte)0x04, builder[0]);
	}
}
