// MIT License - Copyright (c) 2024-2025 Stephen Shaw and contributors

using System.Text;

using TagSharp.Core;

namespace TagSharp.Tests.Core;

[TestClass]
public class BinaryDataTests
{

	[TestMethod]
	public void Empty_HasZeroLength ()
	{
		Assert.AreEqual (0, BinaryData.Empty.Length);
		Assert.IsTrue (BinaryData.Empty.IsEmpty);
	}

	[TestMethod]
	public void Constructor_FromByteArray_WrapsData ()
	{
		byte[] data = [0x01, 0x02, 0x03];
		var bd = new BinaryData (data);

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0x01, bd[0]);
		Assert.AreEqual ((byte)0x02, bd[1]);
		Assert.AreEqual ((byte)0x03, bd[2]);
	}

	[TestMethod]
	public void Constructor_FromNullByteArray_CreatesEmpty ()
	{
		var bd = new BinaryData ((byte[]?)null!);

		Assert.AreEqual (0, bd.Length);
		Assert.IsTrue (bd.IsEmpty);
	}

	[TestMethod]
	public void Constructor_FromReadOnlySpan_CopiesData ()
	{
		ReadOnlySpan<byte> span = [0x01, 0x02, 0x03];
		var bd = new BinaryData (span);

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0x01, bd[0]);
	}

	[TestMethod]
	public void Constructor_WithFill_CreatesFilledArray ()
	{
		var bd = new BinaryData (4, 0xFF);

		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0xFF, bd[0]);
		Assert.AreEqual ((byte)0xFF, bd[3]);
	}

	[TestMethod]
	public void Constructor_WithZeroFill_CreatesZeroedArray ()
	{
		var bd = new BinaryData (3);

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0x00, bd[0]);
	}

	[TestMethod]
	public void Count_EqualsLength ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.AreEqual (bd.Length, bd.Count);
	}

	[TestMethod]
	public void Span_ReturnsDataAsSpan ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		Assert.AreEqual (2, bd.Span.Length);
		Assert.AreEqual ((byte)0x01, bd.Span[0]);
	}

	[TestMethod]
	public void Memory_ReturnsDataAsMemory ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		Assert.AreEqual (2, bd.Memory.Length);
		Assert.AreEqual ((byte)0x01, bd.Memory.Span[0]);
	}



	[TestMethod]
	public void RangeIndexer_ReturnsSlice ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03, 0x04, 0x05]);

		var slice = bd[1..4];

		Assert.AreEqual (3, slice.Length);
		Assert.AreEqual ((byte)0x02, slice[0]);
		Assert.AreEqual ((byte)0x04, slice[2]);
	}

	[TestMethod]
	public void RangeIndexer_FromEnd_Works ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03, 0x04, 0x05]);

		var slice = bd[^3..];

		Assert.AreEqual (3, slice.Length);
		Assert.AreEqual ((byte)0x03, slice[0]);
	}



	[TestMethod]
	public void Slice_StartOnly_ReturnsRemainder ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03, 0x04, 0x05]);

		var slice = bd.Slice (2);

		Assert.AreEqual (3, slice.Length);
		Assert.AreEqual ((byte)0x03, slice[0]);
		Assert.AreEqual ((byte)0x05, slice[2]);
	}

	[TestMethod]
	public void Slice_StartAndLength_ReturnsCorrectSubset ()
	{
		byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
		var bd = new BinaryData (data);

		var slice = bd.Slice (1, 3);

		Assert.AreEqual (3, slice.Length);
		Assert.AreEqual ((byte)0x02, slice[0]);
		Assert.AreEqual ((byte)0x04, slice[2]);
	}



	[TestMethod]
	public void ToUInt16BE_ReadsCorrectly ()
	{
		byte[] data = [0x01, 0x02]; // Big-endian: 0x0102 = 258
		var bd = new BinaryData (data);

		Assert.AreEqual ((ushort)0x0102, bd.ToUInt16BE ());
	}

	[TestMethod]
	public void ToUInt16BE_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x01, 0x02];
		var bd = new BinaryData (data);

		Assert.AreEqual ((ushort)0x0102, bd.ToUInt16BE (1));
	}

	[TestMethod]
	public void ToUInt16LE_ReadsCorrectly ()
	{
		byte[] data = [0x01, 0x02]; // Little-endian: 0x0201 = 513
		var bd = new BinaryData (data);

		Assert.AreEqual ((ushort)0x0201, bd.ToUInt16LE ());
	}

	[TestMethod]
	public void ToUInt32BE_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x01, 0x02, 0x03]; // Big-endian: 0x00010203 = 66051
		var bd = new BinaryData (data);

		Assert.AreEqual (0x00010203u, bd.ToUInt32BE ());
	}

	[TestMethod]
	public void ToUInt32LE_ReadsCorrectly ()
	{
		byte[] data = [0x01, 0x02, 0x03, 0x04];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x04030201u, bd.ToUInt32LE ());
	}

	[TestMethod]
	public void ToUInt64BE_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x0000000000010203UL, bd.ToUInt64BE ());
	}

	[TestMethod]
	public void ToUInt64LE_ReadsCorrectly ()
	{
		byte[] data = [0x01, 0x02, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x0000000004030201UL, bd.ToUInt64LE ());
	}

	[TestMethod]
	public void ToSyncSafeUInt32_ReadsId3v2Format ()
	{
		// ID3v2 syncsafe: each byte uses only 7 bits
		// 0x7F 0x7F 0x7F 0x7F = (127 << 21) | (127 << 14) | (127 << 7) | 127 = 0x0FFFFFFF
		byte[] data = [0x7F, 0x7F, 0x7F, 0x7F];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x0FFFFFFFu, bd.ToSyncSafeUInt32 ());
	}

	[TestMethod]
	public void ToUInt24BE_ReadsFlacFormat ()
	{
		byte[] data = [0x01, 0x02, 0x03]; // 0x010203 = 66051
		var bd = new BinaryData (data);

		Assert.AreEqual (0x010203u, bd.ToUInt24BE ());
	}



	[TestMethod]
	public void ToInt16BE_ReadsPositive ()
	{
		byte[] data = [0x01, 0x00]; // 256
		var bd = new BinaryData (data);

		Assert.AreEqual ((short)256, bd.ToInt16BE ());
	}

	[TestMethod]
	public void ToInt16BE_ReadsNegative ()
	{
		byte[] data = [0xFF, 0xFE]; // -2
		var bd = new BinaryData (data);

		Assert.AreEqual ((short)-2, bd.ToInt16BE ());
	}

	[TestMethod]
	public void ToInt16LE_ReadsCorrectly ()
	{
		byte[] data = [0xFE, 0xFF]; // -2 in little-endian
		var bd = new BinaryData (data);

		Assert.AreEqual ((short)-2, bd.ToInt16LE ());
	}

	[TestMethod]
	public void ToInt32BE_ReadsPositive ()
	{
		byte[] data = [0x00, 0x01, 0x00, 0x00]; // 65536
		var bd = new BinaryData (data);

		Assert.AreEqual (65536, bd.ToInt32BE ());
	}

	[TestMethod]
	public void ToInt32BE_ReadsNegative ()
	{
		byte[] data = [0xFF, 0xFF, 0xFF, 0xFE]; // -2
		var bd = new BinaryData (data);

		Assert.AreEqual (-2, bd.ToInt32BE ());
	}

	[TestMethod]
	public void ToInt32LE_ReadsCorrectly ()
	{
		byte[] data = [0xFE, 0xFF, 0xFF, 0xFF]; // -2 in little-endian
		var bd = new BinaryData (data);

		Assert.AreEqual (-2, bd.ToInt32LE ());
	}

	[TestMethod]
	public void ToInt64BE_ReadsCorrectly ()
	{
		byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE]; // -2
		var bd = new BinaryData (data);

		Assert.AreEqual (-2L, bd.ToInt64BE ());
	}

	[TestMethod]
	public void ToInt64LE_ReadsCorrectly ()
	{
		byte[] data = [0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]; // -2 in little-endian
		var bd = new BinaryData (data);

		Assert.AreEqual (-2L, bd.ToInt64LE ());
	}



	[TestMethod]
	public void FromUInt16BE_WritesCorrectly ()
	{
		var bd = BinaryData.FromUInt16BE (0x0102);

		Assert.AreEqual (2, bd.Length);
		Assert.AreEqual ((byte)0x01, bd[0]);
		Assert.AreEqual ((byte)0x02, bd[1]);
	}

	[TestMethod]
	public void FromUInt16LE_WritesCorrectly ()
	{
		var bd = BinaryData.FromUInt16LE (0x0102);

		Assert.AreEqual (2, bd.Length);
		Assert.AreEqual ((byte)0x02, bd[0]);
		Assert.AreEqual ((byte)0x01, bd[1]);
	}

	[TestMethod]
	public void FromUInt32BE_WritesCorrectly ()
	{
		var bd = BinaryData.FromUInt32BE (0x01020304);

		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0x01, bd[0]);
		Assert.AreEqual ((byte)0x04, bd[3]);
	}

	[TestMethod]
	public void FromUInt32LE_WritesCorrectly ()
	{
		var bd = BinaryData.FromUInt32LE (0x01020304);

		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0x04, bd[0]);
		Assert.AreEqual ((byte)0x01, bd[3]);
	}

	[TestMethod]
	public void FromUInt64BE_WritesCorrectly ()
	{
		var bd = BinaryData.FromUInt64BE (0x0102030405060708UL);

		Assert.AreEqual (8, bd.Length);
		Assert.AreEqual ((byte)0x01, bd[0]);
		Assert.AreEqual ((byte)0x08, bd[7]);
	}

	[TestMethod]
	public void FromUInt64LE_WritesCorrectly ()
	{
		var bd = BinaryData.FromUInt64LE (0x0102030405060708UL);

		Assert.AreEqual (8, bd.Length);
		Assert.AreEqual ((byte)0x08, bd[0]);
		Assert.AreEqual ((byte)0x01, bd[7]);
	}

	[TestMethod]
	public void FromSyncSafeUInt32_WritesId3v2Format ()
	{
		var bd = BinaryData.FromSyncSafeUInt32 (0x0FFFFFFF);

		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0x7F, bd[0]);
		Assert.AreEqual ((byte)0x7F, bd[1]);
		Assert.AreEqual ((byte)0x7F, bd[2]);
		Assert.AreEqual ((byte)0x7F, bd[3]);
	}

	[TestMethod]
	public void FromUInt24BE_WritesCorrectly ()
	{
		var bd = BinaryData.FromUInt24BE (0x010203);

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0x01, bd[0]);
		Assert.AreEqual ((byte)0x02, bd[1]);
		Assert.AreEqual ((byte)0x03, bd[2]);
	}



	[TestMethod]
	public void FromInt16BE_WritesPositive ()
	{
		var bd = BinaryData.FromInt16BE (256);

		Assert.AreEqual (2, bd.Length);
		Assert.AreEqual ((byte)0x01, bd[0]);
		Assert.AreEqual ((byte)0x00, bd[1]);
	}

	[TestMethod]
	public void FromInt16BE_WritesNegative ()
	{
		var bd = BinaryData.FromInt16BE (-2);

		Assert.AreEqual (2, bd.Length);
		Assert.AreEqual ((byte)0xFF, bd[0]);
		Assert.AreEqual ((byte)0xFE, bd[1]);
	}

	[TestMethod]
	public void FromInt16LE_WritesCorrectly ()
	{
		var bd = BinaryData.FromInt16LE (-2);

		Assert.AreEqual (2, bd.Length);
		Assert.AreEqual ((byte)0xFE, bd[0]);
		Assert.AreEqual ((byte)0xFF, bd[1]);
	}

	[TestMethod]
	public void FromInt32BE_WritesCorrectly ()
	{
		var bd = BinaryData.FromInt32BE (-2);

		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0xFF, bd[0]);
		Assert.AreEqual ((byte)0xFE, bd[3]);
	}

	[TestMethod]
	public void FromInt32LE_WritesCorrectly ()
	{
		var bd = BinaryData.FromInt32LE (-2);

		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0xFE, bd[0]);
		Assert.AreEqual ((byte)0xFF, bd[3]);
	}

	[TestMethod]
	public void FromInt64BE_WritesCorrectly ()
	{
		var bd = BinaryData.FromInt64BE (-2L);

		Assert.AreEqual (8, bd.Length);
		Assert.AreEqual ((byte)0xFF, bd[0]);
		Assert.AreEqual ((byte)0xFE, bd[7]);
	}

	[TestMethod]
	public void FromInt64LE_WritesCorrectly ()
	{
		var bd = BinaryData.FromInt64LE (-2L);

		Assert.AreEqual (8, bd.Length);
		Assert.AreEqual ((byte)0xFE, bd[0]);
		Assert.AreEqual ((byte)0xFF, bd[7]);
	}



	[TestMethod]
	public void ToStringLatin1_DecodesCorrectly ()
	{
		byte[] data = [0x48, 0x65, 0x6C, 0x6C, 0x6F]; // "Hello"
		var bd = new BinaryData (data);

		Assert.AreEqual ("Hello", bd.ToStringLatin1 ());
	}

	[TestMethod]
	public void ToStringUtf8_DecodesCorrectly ()
	{
		byte[] data = [0xC3, 0xBC]; // "ü" in UTF-8
		var bd = new BinaryData (data);

		Assert.AreEqual ("ü", bd.ToStringUtf8 ());
	}

	[TestMethod]
	public void ToStringUtf16_WithLittleEndianBom_DecodesCorrectly ()
	{
		byte[] data = [0xFF, 0xFE, 0x48, 0x00]; // BOM + "H" in UTF-16LE
		var bd = new BinaryData (data);

		Assert.AreEqual ("H", bd.ToStringUtf16 ());
	}

	[TestMethod]
	public void ToStringUtf16_WithBigEndianBom_DecodesCorrectly ()
	{
		byte[] data = [0xFE, 0xFF, 0x00, 0x48]; // BOM + "H" in UTF-16BE
		var bd = new BinaryData (data);

		Assert.AreEqual ("H", bd.ToStringUtf16 ());
	}

	[TestMethod]
	public void ToStringUtf16_WithoutBom_DefaultsToLittleEndian ()
	{
		byte[] data = [0x48, 0x00]; // "H" in UTF-16LE without BOM
		var bd = new BinaryData (data);

		Assert.AreEqual ("H", bd.ToStringUtf16 ());
	}

	[TestMethod]
	public void ToString_WithEncoding_DecodesCorrectly ()
	{
		byte[] data = [0x48, 0x65, 0x6C, 0x6C, 0x6F];
		var bd = new BinaryData (data);

		Assert.AreEqual ("Hello", bd.ToString (Encoding.ASCII));
	}

	[TestMethod]
	public void FromStringLatin1_EncodesCorrectly ()
	{
		var bd = BinaryData.FromStringLatin1 ("Hello");

		Assert.AreEqual (5, bd.Length);
		Assert.AreEqual ((byte)0x48, bd[0]);
	}

	[TestMethod]
	public void FromStringLatin1_NullReturnsEmpty ()
	{
		var bd = BinaryData.FromStringLatin1 (null!);

		Assert.AreEqual (0, bd.Length);
	}

	[TestMethod]
	public void FromStringUtf8_EncodesCorrectly ()
	{
		var bd = BinaryData.FromStringUtf8 ("ü");

		Assert.AreEqual (2, bd.Length);
		Assert.AreEqual ((byte)0xC3, bd[0]);
		Assert.AreEqual ((byte)0xBC, bd[1]);
	}

	[TestMethod]
	public void FromStringUtf16_IncludesBom ()
	{
		var bd = BinaryData.FromStringUtf16 ("A");

		Assert.AreEqual ((byte)0xFF, bd[0]); // BOM
		Assert.AreEqual ((byte)0xFE, bd[1]); // BOM
		Assert.AreEqual ((byte)0x41, bd[2]); // 'A'
		Assert.AreEqual ((byte)0x00, bd[3]); // null high byte
	}

	[TestMethod]
	public void FromStringUtf16_WithoutBom ()
	{
		var bd = BinaryData.FromStringUtf16 ("A", includeBom: false);

		Assert.AreEqual (2, bd.Length);
		Assert.AreEqual ((byte)0x41, bd[0]); // 'A'
		Assert.AreEqual ((byte)0x00, bd[1]); // null high byte
	}

	[TestMethod]
	public void FromString_WithEncoding_EncodesCorrectly ()
	{
		var bd = BinaryData.FromString ("Hello", Encoding.ASCII);

		Assert.AreEqual (5, bd.Length);
		Assert.AreEqual ((byte)0x48, bd[0]);
	}



	[TestMethod]
	public void ToStringLatin1NullTerminated_StopsAtNull ()
	{
		byte[] data = [0x48, 0x69, 0x00, 0x58, 0x58]; // "Hi\0XX"
		var bd = new BinaryData (data);

		Assert.AreEqual ("Hi", bd.ToStringLatin1NullTerminated ());
	}

	[TestMethod]
	public void ToStringLatin1NullTerminated_NoNull_ReturnsAll ()
	{
		byte[] data = [0x48, 0x69]; // "Hi"
		var bd = new BinaryData (data);

		Assert.AreEqual ("Hi", bd.ToStringLatin1NullTerminated ());
	}

	[TestMethod]
	public void ToStringUtf8NullTerminated_StopsAtNull ()
	{
		byte[] data = [0x48, 0x69, 0x00, 0x58]; // "Hi\0X"
		var bd = new BinaryData (data);

		Assert.AreEqual ("Hi", bd.ToStringUtf8NullTerminated ());
	}

	[TestMethod]
	public void ToStringUtf16NullTerminated_StopsAtDoubleNull ()
	{
		byte[] data = [0xFF, 0xFE, 0x48, 0x00, 0x69, 0x00, 0x00, 0x00, 0x58, 0x00]; // BOM + "Hi" + double-null + "X"
		var bd = new BinaryData (data);

		Assert.AreEqual ("Hi", bd.ToStringUtf16NullTerminated ());
	}

	[TestMethod]
	public void ToStringUtf16NullTerminated_BigEndian_Works ()
	{
		byte[] data = [0xFE, 0xFF, 0x00, 0x48, 0x00, 0x00]; // BE BOM + "H" + double-null
		var bd = new BinaryData (data);

		Assert.AreEqual ("H", bd.ToStringUtf16NullTerminated ());
	}

	[TestMethod]
	public void ToStringUtf16NullTerminated_NoTerminator_ReturnsAll ()
	{
		byte[] data = [0xFF, 0xFE, 0x48, 0x00]; // BOM + "H"
		var bd = new BinaryData (data);

		Assert.AreEqual ("H", bd.ToStringUtf16NullTerminated ());
	}

	[TestMethod]
	public void FromStringLatin1NullTerminated_AppendsNull ()
	{
		var bd = BinaryData.FromStringLatin1NullTerminated ("Hi");

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0x48, bd[0]);
		Assert.AreEqual ((byte)0x69, bd[1]);
		Assert.AreEqual ((byte)0x00, bd[2]);
	}

	[TestMethod]
	public void FromStringUtf8NullTerminated_AppendsNull ()
	{
		var bd = BinaryData.FromStringUtf8NullTerminated ("Hi");

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0x00, bd[2]);
	}

	[TestMethod]
	public void FromStringUtf16NullTerminated_AppendsDoubleNull ()
	{
		var bd = BinaryData.FromStringUtf16NullTerminated ("H");

		// BOM(2) + "H"(2) + double-null(2) = 6
		Assert.AreEqual (6, bd.Length);
		Assert.AreEqual ((byte)0xFF, bd[0]); // BOM
		Assert.AreEqual ((byte)0xFE, bd[1]);
		Assert.AreEqual ((byte)0x00, bd[4]); // null terminator
		Assert.AreEqual ((byte)0x00, bd[5]);
	}

	[TestMethod]
	public void FromStringUtf16NullTerminated_WithoutBom ()
	{
		var bd = BinaryData.FromStringUtf16NullTerminated ("H", includeBom: false);

		// "H"(2) + double-null(2) = 4
		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0x48, bd[0]); // 'H'
	}



	[TestMethod]
	public void PadRight_ExtendsWithZeros ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var padded = bd.PadRight (5);

		Assert.AreEqual (5, padded.Length);
		Assert.AreEqual ((byte)0x01, padded[0]);
		Assert.AreEqual ((byte)0x00, padded[4]);
	}

	[TestMethod]
	public void PadRight_WithCustomByte ()
	{
		var bd = new BinaryData ([0x01]);
		var padded = bd.PadRight (4, 0xFF);

		Assert.AreEqual (4, padded.Length);
		Assert.AreEqual ((byte)0xFF, padded[3]);
	}

	[TestMethod]
	public void PadRight_NoChangeWhenAlreadyLong ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var padded = bd.PadRight (2);

		Assert.AreEqual (3, padded.Length);
	}

	[TestMethod]
	public void PadLeft_ExtendsWithZeros ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var padded = bd.PadLeft (5);

		Assert.AreEqual (5, padded.Length);
		Assert.AreEqual ((byte)0x00, padded[0]);
		Assert.AreEqual ((byte)0x01, padded[3]);
	}

	[TestMethod]
	public void PadLeft_WithCustomByte ()
	{
		var bd = new BinaryData ([0x01]);
		var padded = bd.PadLeft (4, 0xFF);

		Assert.AreEqual (4, padded.Length);
		Assert.AreEqual ((byte)0xFF, padded[0]);
		Assert.AreEqual ((byte)0x01, padded[3]);
	}

	[TestMethod]
	public void TrimEnd_RemovesTrailingZeros ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x00, 0x00]);
		var trimmed = bd.TrimEnd ();

		Assert.AreEqual (2, trimmed.Length);
		Assert.AreEqual ((byte)0x02, trimmed[1]);
	}

	[TestMethod]
	public void TrimEnd_NoChange_WhenNoTrailing ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var trimmed = bd.TrimEnd ();

		Assert.AreEqual (2, trimmed.Length);
	}

	[TestMethod]
	public void TrimStart_RemovesLeadingZeros ()
	{
		var bd = new BinaryData ([0x00, 0x00, 0x01, 0x02]);
		var trimmed = bd.TrimStart ();

		Assert.AreEqual (2, trimmed.Length);
		Assert.AreEqual ((byte)0x01, trimmed[0]);
	}

	[TestMethod]
	public void TrimStart_NoChange_WhenNoLeading ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var trimmed = bd.TrimStart ();

		Assert.AreEqual (2, trimmed.Length);
	}

	[TestMethod]
	public void Trim_RemovesBothEnds ()
	{
		var bd = new BinaryData ([0x00, 0x01, 0x02, 0x00, 0x00]);
		var trimmed = bd.Trim ();

		Assert.AreEqual (2, trimmed.Length);
		Assert.AreEqual ((byte)0x01, trimmed[0]);
		Assert.AreEqual ((byte)0x02, trimmed[1]);
	}

	[TestMethod]
	public void Trim_NoChange_WhenClean ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var trimmed = bd.Trim ();

		Assert.AreEqual (2, trimmed.Length);
	}

	[TestMethod]
	public void Resize_Truncates ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03, 0x04]);
		var resized = bd.Resize (2);

		Assert.AreEqual (2, resized.Length);
		Assert.AreEqual ((byte)0x02, resized[1]);
	}

	[TestMethod]
	public void Resize_Extends ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var resized = bd.Resize (4, 0xFF);

		Assert.AreEqual (4, resized.Length);
		Assert.AreEqual ((byte)0xFF, resized[3]);
	}

	[TestMethod]
	public void Resize_NoChange_WhenSameSize ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var resized = bd.Resize (2);

		Assert.AreEqual (2, resized.Length);
	}



	[TestMethod]
	public void IndexOf_Pattern_FindsPattern ()
	{
		byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
		var bd = new BinaryData (data);

		Assert.AreEqual (2, bd.IndexOf ([0x03, 0x04]));
		Assert.AreEqual (-1, bd.IndexOf ([0x06]));
	}

	[TestMethod]
	public void IndexOf_Pattern_WithStartIndex ()
	{
		byte[] data = [0x01, 0x02, 0x01, 0x02];
		var bd = new BinaryData (data);

		Assert.AreEqual (2, bd.IndexOf ([0x01], 1));
	}

	[TestMethod]
	public void IndexOf_Pattern_EmptyPattern_ReturnsMinusOne ()
	{
		var bd = new BinaryData ([0x01, 0x02]);

		Assert.AreEqual (-1, bd.IndexOf (ReadOnlySpan<byte>.Empty));
	}

	[TestMethod]
	public void IndexOf_Byte_FindsByte ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);

		Assert.AreEqual (1, bd.IndexOf ((byte)0x02));
	}

	[TestMethod]
	public void IndexOf_Byte_WithStartIndex ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x01]);

		Assert.AreEqual (2, bd.IndexOf ((byte)0x01, 1));
	}

	[TestMethod]
	public void LastIndexOf_Pattern_FindsLastOccurrence ()
	{
		byte[] data = [0x01, 0x02, 0x01, 0x02];
		var bd = new BinaryData (data);

		Assert.AreEqual (2, bd.LastIndexOf ([0x01, 0x02]));
	}

	[TestMethod]
	public void LastIndexOf_Pattern_NotFound ()
	{
		var bd = new BinaryData ([0x01, 0x02]);

		Assert.AreEqual (-1, bd.LastIndexOf ([0x03]));
	}

	[TestMethod]
	public void LastIndexOf_Pattern_Empty_ReturnsMinusOne ()
	{
		var bd = new BinaryData ([0x01, 0x02]);

		Assert.AreEqual (-1, bd.LastIndexOf (ReadOnlySpan<byte>.Empty));
	}

	[TestMethod]
	public void LastIndexOf_Byte_FindsLastOccurrence ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x01]);

		Assert.AreEqual (2, bd.LastIndexOf ((byte)0x01));
	}

	[TestMethod]
	public void LastIndexOf_Byte_OnEmptyData ()
	{
		var bd = BinaryData.Empty;

		Assert.AreEqual (-1, bd.LastIndexOf ((byte)0x01));
	}

	[TestMethod]
	public void Contains_Pattern_ReturnsTrue ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);

		Assert.IsTrue (bd.Contains ([0x02, 0x03]));
	}

	[TestMethod]
	public void Contains_Pattern_ReturnsFalse ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);

		Assert.IsFalse (bd.Contains ([0x04]));
	}

	[TestMethod]
	public void Contains_Byte_ReturnsTrue ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);

		Assert.IsTrue (bd.Contains ((byte)0x02));
	}

	[TestMethod]
	public void Contains_Byte_ReturnsFalse ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);

		Assert.IsFalse (bd.Contains ((byte)0x04));
	}

	[TestMethod]
	public void StartsWith_ChecksPrefix ()
	{
		byte[] data = [0x49, 0x44, 0x33]; // "ID3"
		var bd = new BinaryData (data);

		Assert.IsTrue (bd.StartsWith ([0x49, 0x44, 0x33]));
		Assert.IsFalse (bd.StartsWith ([0x66, 0x4C, 0x61, 0x43])); // "fLaC"
	}

	[TestMethod]
	public void EndsWith_ChecksSuffix ()
	{
		byte[] data = [0x01, 0x02, 0x03];
		var bd = new BinaryData (data);

		Assert.IsTrue (bd.EndsWith ([0x02, 0x03]));
		Assert.IsFalse (bd.EndsWith ([0x01, 0x03]));
	}



	[TestMethod]
	public void Add_ConcatenatesData ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = new BinaryData ([0x03, 0x04]);

		var result = a.Add (b);

		Assert.AreEqual (4, result.Length);
		Assert.AreEqual ((byte)0x01, result[0]);
		Assert.AreEqual ((byte)0x04, result[3]);
	}

	[TestMethod]
	public void Add_WithEmptyFirst_ReturnsSecond ()
	{
		var a = BinaryData.Empty;
		var b = new BinaryData ([0x01, 0x02]);

		var result = a.Add (b);

		Assert.AreEqual (2, result.Length);
		Assert.AreEqual ((byte)0x01, result[0]);
	}

	[TestMethod]
	public void Add_WithEmptySecond_ReturnsFirst ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = BinaryData.Empty;

		var result = a.Add (b);

		Assert.AreEqual (2, result.Length);
	}

	[TestMethod]
	public void Operator_Plus_ConcatenatesData ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = new BinaryData ([0x03, 0x04]);

		var result = a + b;

		Assert.AreEqual (4, result.Length);
	}

	[TestMethod]
	public void Concat_MultipleItems ()
	{
		var a = new BinaryData ([0x01]);
		var b = new BinaryData ([0x02]);
		var c = new BinaryData ([0x03]);

		var result = BinaryData.Concat (a, b, c);

		Assert.AreEqual (3, result.Length);
		Assert.AreEqual ((byte)0x01, result[0]);
		Assert.AreEqual ((byte)0x02, result[1]);
		Assert.AreEqual ((byte)0x03, result[2]);
	}

	[TestMethod]
	public void Concat_EmptyArray_ReturnsEmpty ()
	{
		var result = BinaryData.Concat ();

		Assert.IsTrue (result.IsEmpty);
	}



	[TestMethod]
	public void ToHexString_ReturnsLowercase ()
	{
		var bd = new BinaryData ([0xAB, 0xCD, 0xEF]);

		Assert.AreEqual ("abcdef", bd.ToHexString ());
	}

	[TestMethod]
	public void ToHexStringUpper_ReturnsUppercase ()
	{
		var bd = new BinaryData ([0xAB, 0xCD, 0xEF]);

		Assert.AreEqual ("ABCDEF", bd.ToHexStringUpper ());
	}

	[TestMethod]
	public void FromHexString_ParsesCorrectly ()
	{
		var bd = BinaryData.FromHexString ("ABCDEF");

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0xAB, bd[0]);
		Assert.AreEqual ((byte)0xCD, bd[1]);
		Assert.AreEqual ((byte)0xEF, bd[2]);
	}

	[TestMethod]
	public void FromHexString_WithSpaces ()
	{
		var bd = BinaryData.FromHexString ("AB CD EF");

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0xAB, bd[0]);
	}

	[TestMethod]
	public void FromHexString_Empty_ReturnsEmpty ()
	{
		var bd = BinaryData.FromHexString ("");

		Assert.IsTrue (bd.IsEmpty);
	}

	[TestMethod]
	public void FromHexString_Lowercase ()
	{
		var bd = BinaryData.FromHexString ("abcdef");

		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0xAB, bd[0]);
	}



	[TestMethod]
	public void ComputeCrc32_MatchesKnownValue ()
	{
		// "123456789" has CRC-32 = 0xCBF43926
		var data = "123456789"u8.ToArray ();
		var bd = new BinaryData (data);

		Assert.AreEqual (0xCBF43926u, bd.ComputeCrc32 ());
	}

	[TestMethod]
	public void ComputeCrc32_EmptyData ()
	{
		var bd = BinaryData.Empty;

		// CRC-32 of empty data is 0x00000000
		Assert.AreEqual (0x00000000u, bd.ComputeCrc32 ());
	}

	[TestMethod]
	public void ComputeCrc8_Works ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var crc = bd.ComputeCrc8 ();

		// Just verify it returns a value and is deterministic
		Assert.AreEqual (crc, bd.ComputeCrc8 ());
	}

	[TestMethod]
	public void ComputeCrc16Ccitt_Works ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var crc = bd.ComputeCrc16Ccitt ();

		// Just verify it returns a value and is deterministic
		Assert.AreEqual (crc, bd.ComputeCrc16Ccitt ());
	}



	[TestMethod]
	public void Equals_ComparesContent ()
	{
		var a = new BinaryData ([0x01, 0x02, 0x03]);
		var b = new BinaryData ([0x01, 0x02, 0x03]);
		var c = new BinaryData ([0x01, 0x02, 0x04]);

		Assert.IsTrue (a.Equals (b));
		Assert.IsFalse (a.Equals (c));
	}

	[TestMethod]
	public void Equals_Object_ComparesContent ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		object b = new BinaryData ([0x01, 0x02]);
		object c = "not a BinaryData";

		Assert.IsTrue (a.Equals (b));
		Assert.IsFalse (a.Equals (c));
	}

	[TestMethod]
	public void Operator_Equality ()
	{
		var a = new BinaryData ([0x01, 0x02, 0x03]);
		var b = new BinaryData ([0x01, 0x02, 0x03]);
		var c = new BinaryData ([0x01, 0x02, 0x04]);

		Assert.IsTrue (a == b);
		Assert.IsTrue (a != c);
	}

	[TestMethod]
	public void GetHashCode_SameForEqualData ()
	{
		var a = new BinaryData ([0x01, 0x02, 0x03]);
		var b = new BinaryData ([0x01, 0x02, 0x03]);

		Assert.AreEqual (a.GetHashCode (), b.GetHashCode ());
	}



	[TestMethod]
	public void ToArray_ReturnsCopy ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var array = bd.ToArray ();

		Assert.AreEqual ((byte)0x01, array[0]);
		Assert.AreEqual ((byte)0x02, array[1]);
	}

	[TestMethod]
	public void ToReadOnlySpan_ReturnsSpan ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var span = bd.ToReadOnlySpan ();

		Assert.AreEqual (2, span.Length);
		Assert.AreEqual ((byte)0x01, span[0]);
	}

	[TestMethod]
	public void ToString_ReturnsDescription ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);

		Assert.AreEqual ("BinaryData[3]", bd.ToString ());
	}

	[TestMethod]
	public void FromByteArray_CreatesBinaryData ()
	{
		var bd = BinaryData.FromByteArray ([0x01, 0x02]);

		Assert.AreEqual (2, bd.Length);
	}

	[TestMethod]
	public void ImplicitConversion_FromByteArray ()
	{
		BinaryData bd = new byte[] { 0x01, 0x02 };

		Assert.AreEqual (2, bd.Length);
	}

	[TestMethod]
	public void ImplicitConversion_ToReadOnlySpan ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		ReadOnlySpan<byte> span = bd;

		Assert.AreEqual (2, span.Length);
	}



	[TestMethod]
	public void GetEnumerator_IteratesBytes ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var list = new List<byte> ();

		foreach (var b in bd)
			list.Add (b);

		Assert.HasCount (3, list);
		Assert.AreEqual ((byte)0x01, list[0]);
		Assert.AreEqual ((byte)0x03, list[2]);
	}

	[TestMethod]
	public void Enumerable_ToList ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var list = bd.ToList ();

		Assert.HasCount (3, list);
	}

	[TestMethod]
	public void Enumerable_LinqWorks ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03, 0x04]);
		var sum = bd.Sum (b => b);

		Assert.AreEqual (10, sum);
	}



	[TestMethod]
	public void CompareTo_Equal_ReturnsZero ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = new BinaryData ([0x01, 0x02]);

		Assert.AreEqual (0, a.CompareTo (b));
	}

	[TestMethod]
	public void CompareTo_LessThan_ReturnsNegative ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = new BinaryData ([0x01, 0x03]);

		Assert.IsLessThan (0, a.CompareTo (b));
	}

	[TestMethod]
	public void CompareTo_GreaterThan_ReturnsPositive ()
	{
		var a = new BinaryData ([0x01, 0x03]);
		var b = new BinaryData ([0x01, 0x02]);

		Assert.IsGreaterThan (0, a.CompareTo (b));
	}

	[TestMethod]
	public void CompareTo_ShorterPrefix_ReturnsNegative ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = new BinaryData ([0x01, 0x02, 0x03]);

		Assert.IsLessThan (0, a.CompareTo (b));
	}

	[TestMethod]
	public void Operator_LessThan ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = new BinaryData ([0x01, 0x03]);

		Assert.IsTrue (a < b);
		Assert.IsFalse (b < a);
	}

	[TestMethod]
	public void Operator_LessThanOrEqual ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = new BinaryData ([0x01, 0x02]);

		Assert.IsTrue (a <= b);
	}

	[TestMethod]
	public void Operator_GreaterThan ()
	{
		var a = new BinaryData ([0x01, 0x03]);
		var b = new BinaryData ([0x01, 0x02]);

		Assert.IsTrue (a > b);
		Assert.IsFalse (b > a);
	}

	[TestMethod]
	public void Operator_GreaterThanOrEqual ()
	{
		var a = new BinaryData ([0x01, 0x02]);
		var b = new BinaryData ([0x01, 0x02]);

		Assert.IsTrue (a >= b);
	}

	// Parameterized Tests

	[TestMethod]
	[DataRow ((ushort)0x0102, new byte[] { 0x01, 0x02 })]
	[DataRow ((ushort)0xFFFF, new byte[] { 0xFF, 0xFF })]
	[DataRow ((ushort)0x0000, new byte[] { 0x00, 0x00 })]
	public void ToUInt16BE_VariousValues (ushort expected, byte[] data)
	{
		var bd = new BinaryData (data);
		Assert.AreEqual (expected, bd.ToUInt16BE ());
	}

	[TestMethod]
	[DataRow ((uint)0x01020304, new byte[] { 0x01, 0x02, 0x03, 0x04 })]
	[DataRow ((uint)0xFFFFFFFF, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
	[DataRow ((uint)0x00000000, new byte[] { 0x00, 0x00, 0x00, 0x00 })]
	public void ToUInt32BE_VariousValues (uint expected, byte[] data)
	{
		var bd = new BinaryData (data);
		Assert.AreEqual (expected, bd.ToUInt32BE ());
	}

	// Round-trip Tests

	[TestMethod]
	public void RoundTrip_UInt16BE_PreservesValue ()
	{
		const ushort original = 0x1234;
		var bd = BinaryData.FromUInt16BE (original);
		Assert.AreEqual (original, bd.ToUInt16BE ());
	}

	[TestMethod]
	public void RoundTrip_UInt16LE_PreservesValue ()
	{
		const ushort original = 0x1234;
		var bd = BinaryData.FromUInt16LE (original);
		Assert.AreEqual (original, bd.ToUInt16LE ());
	}

	[TestMethod]
	public void RoundTrip_UInt32BE_PreservesValue ()
	{
		const uint original = 0x12345678;
		var bd = BinaryData.FromUInt32BE (original);
		Assert.AreEqual (original, bd.ToUInt32BE ());
	}

	[TestMethod]
	public void RoundTrip_UInt32LE_PreservesValue ()
	{
		const uint original = 0x12345678;
		var bd = BinaryData.FromUInt32LE (original);
		Assert.AreEqual (original, bd.ToUInt32LE ());
	}

	[TestMethod]
	public void RoundTrip_UInt64BE_PreservesValue ()
	{
		const ulong original = 0x123456789ABCDEF0;
		var bd = BinaryData.FromUInt64BE (original);
		Assert.AreEqual (original, bd.ToUInt64BE ());
	}

	[TestMethod]
	public void RoundTrip_UInt64LE_PreservesValue ()
	{
		const ulong original = 0x123456789ABCDEF0;
		var bd = BinaryData.FromUInt64LE (original);
		Assert.AreEqual (original, bd.ToUInt64LE ());
	}

	[TestMethod]
	public void RoundTrip_Int16BE_PreservesValue ()
	{
		const short original = -1234;
		var bd = BinaryData.FromInt16BE (original);
		Assert.AreEqual (original, bd.ToInt16BE ());
	}

	[TestMethod]
	public void RoundTrip_Int32BE_PreservesValue ()
	{
		const int original = -12345678;
		var bd = BinaryData.FromInt32BE (original);
		Assert.AreEqual (original, bd.ToInt32BE ());
	}

	[TestMethod]
	public void RoundTrip_Int64BE_PreservesValue ()
	{
		const long original = -123456789012345;
		var bd = BinaryData.FromInt64BE (original);
		Assert.AreEqual (original, bd.ToInt64BE ());
	}

	[TestMethod]
	public void RoundTrip_SyncSafe_PreservesValue ()
	{
		const uint original = 0x0ABCDEF;
		var bd = BinaryData.FromSyncSafeUInt32 (original);
		Assert.AreEqual (original, bd.ToSyncSafeUInt32 ());
	}

	[TestMethod]
	public void RoundTrip_UInt24BE_PreservesValue ()
	{
		const uint original = 0x123456;
		var bd = BinaryData.FromUInt24BE (original);
		Assert.AreEqual (original, bd.ToUInt24BE ());
	}

	[TestMethod]
	public void RoundTrip_HexString_PreservesData ()
	{
		var original = new BinaryData ([0xAB, 0xCD, 0xEF, 0x12]);
		var hex = original.ToHexString ();
		var roundTripped = BinaryData.FromHexString (hex);
		Assert.AreEqual (original, roundTripped);
	}

	[TestMethod]
	public void RoundTrip_Latin1String_PreservesData ()
	{
		const string original = "Hello World!";
		var bd = BinaryData.FromStringLatin1 (original);
		Assert.AreEqual (original, bd.ToStringLatin1 ());
	}

	[TestMethod]
	public void RoundTrip_Utf8String_PreservesData ()
	{
		const string original = "Hello ü World!";
		var bd = BinaryData.FromStringUtf8 (original);
		Assert.AreEqual (original, bd.ToStringUtf8 ());
	}

	// Boundary/Exception Tests

	[TestMethod]
	public void FromSyncSafeUInt32_MaxValue_Succeeds ()
	{
		var bd = BinaryData.FromSyncSafeUInt32 (0x0FFFFFFF);
		Assert.AreEqual (4, bd.Length);
	}

	[TestMethod]
	public void FromSyncSafeUInt32_ExceedsMax_ThrowsArgumentOutOfRange ()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			BinaryData.FromSyncSafeUInt32 (0x10000000));
	}

	[TestMethod]
	public void FromHexString_OddLength_ThrowsFormatException ()
	{
		Assert.ThrowsExactly<FormatException> (() =>
			BinaryData.FromHexString ("ABC"));
	}

	[TestMethod]
	public void FromHexString_InvalidChar_ThrowsFormatException ()
	{
		Assert.ThrowsExactly<FormatException> (() =>
			BinaryData.FromHexString ("GHIJ"));
	}

	[TestMethod]
	public void IndexOf_StartIndexBeyondLength_ReturnsMinusOne ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		Assert.AreEqual (-1, bd.IndexOf ((byte)0x01, 10));
	}

	[TestMethod]
	public void IndexOf_Pattern_StartIndexBeyondLength_ReturnsMinusOne ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		Assert.AreEqual (-1, bd.IndexOf ([0x01], 10));
	}

	// Null Safety Tests

	[TestMethod]
	public void ToString_NullEncoding_ThrowsArgumentNull ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		Assert.ThrowsExactly<ArgumentNullException> (() =>
			bd.ToString (null!));
	}

	[TestMethod]
	public void FromString_NullEncoding_ThrowsArgumentNull ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() =>
			BinaryData.FromString ("test", null!));
	}

	[TestMethod]
	public void FromHexString_Null_ThrowsArgumentNull ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() =>
			BinaryData.FromHexString (null!));
	}

	[TestMethod]
	public void Concat_NullArray_ThrowsArgumentNull ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() =>
			BinaryData.Concat (null!));
	}

	// Hash Code Tests

	[TestMethod]
	public void GetHashCode_LargeData_UseSampling ()
	{
		// Verify hash code works with large data (uses sampling)
		var largeData = new BinaryData (1000, 0x42);
		var hash1 = largeData.GetHashCode ();
		var hash2 = largeData.GetHashCode ();
		Assert.AreEqual (hash1, hash2);
	}

	[TestMethod]
	public void GetHashCode_DifferentData_ShouldDiffer ()
	{
		var a = new BinaryData ([0x01, 0x02, 0x03]);
		var b = new BinaryData ([0x01, 0x02, 0x04]);
		// Different data should produce different hashes (not guaranteed but very likely)
		Assert.AreNotEqual (a.GetHashCode (), b.GetHashCode ());
	}

	// CRC Known Value Tests

	[TestMethod]
	public void ComputeCrc8_KnownValue ()
	{
		// CRC-8 of "123456789" with polynomial 0x07
		var data = "123456789"u8.ToArray ();
		var bd = new BinaryData (data);
		var crc = bd.ComputeCrc8 ();
		// Known value for CRC-8 with polynomial 0x07
		Assert.AreEqual ((byte)0xF4, crc);
	}

	[TestMethod]
	public void ComputeCrc16Ccitt_KnownValue ()
	{
		// CRC-16-CCITT of "123456789" with polynomial 0x1021
		var data = "123456789"u8.ToArray ();
		var bd = new BinaryData (data);
		var crc = bd.ComputeCrc16Ccitt ();
		// Known value for CRC-16-CCITT (XMODEM variant)
		Assert.AreEqual ((ushort)0x31C3, crc);
	}

	// Immutability Tests

	[TestMethod]
	public void PadRight_NoChange_ReturnsSameInstance ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var padded = bd.PadRight (2);
		// Since struct copies on return, we verify content equality
		Assert.AreEqual (bd, padded);
	}

	[TestMethod]
	public void TrimEnd_NoChange_ReturnsSameContent ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var trimmed = bd.TrimEnd ();
		Assert.AreEqual (bd, trimmed);
	}

	[TestMethod]
	public void Resize_NoChange_ReturnsSameContent ()
	{
		var bd = new BinaryData ([0x01, 0x02]);
		var resized = bd.Resize (2);
		Assert.AreEqual (bd, resized);
	}

	// LE Signed Integer Round-trip Tests

	[TestMethod]
	public void RoundTrip_Int16LE_PreservesValue ()
	{
		const short original = -1234;
		var bd = BinaryData.FromInt16LE (original);
		Assert.AreEqual (original, bd.ToInt16LE ());
	}

	[TestMethod]
	public void RoundTrip_Int32LE_PreservesValue ()
	{
		const int original = -12345678;
		var bd = BinaryData.FromInt32LE (original);
		Assert.AreEqual (original, bd.ToInt32LE ());
	}

	[TestMethod]
	public void RoundTrip_Int64LE_PreservesValue ()
	{
		const long original = -123456789012345;
		var bd = BinaryData.FromInt64LE (original);
		Assert.AreEqual (original, bd.ToInt64LE ());
	}

	// Offset Parameter Tests

	[TestMethod]
	public void ToUInt32BE_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x00, 0x01, 0x02, 0x03, 0x04];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x01020304u, bd.ToUInt32BE (2));
	}

	[TestMethod]
	public void ToUInt32LE_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x00, 0x04, 0x03, 0x02, 0x01];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x01020304u, bd.ToUInt32LE (2));
	}

	[TestMethod]
	public void ToUInt64BE_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x0102030405060708UL, bd.ToUInt64BE (2));
	}

	[TestMethod]
	public void ToUInt64LE_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x00, 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x0102030405060708UL, bd.ToUInt64LE (2));
	}

	[TestMethod]
	public void ToInt16BE_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0xFF, 0xFE];
		var bd = new BinaryData (data);

		Assert.AreEqual ((short)-2, bd.ToInt16BE (1));
	}

	[TestMethod]
	public void ToInt16LE_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0xFE, 0xFF];
		var bd = new BinaryData (data);

		Assert.AreEqual ((short)-2, bd.ToInt16LE (1));
	}

	[TestMethod]
	public void ToSyncSafeUInt32_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x7F, 0x7F, 0x7F, 0x7F];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x0FFFFFFFu, bd.ToSyncSafeUInt32 (1));
	}

	[TestMethod]
	public void ToUInt24BE_WithOffset_ReadsCorrectly ()
	{
		byte[] data = [0x00, 0x01, 0x02, 0x03];
		var bd = new BinaryData (data);

		Assert.AreEqual (0x010203u, bd.ToUInt24BE (1));
	}

	// Slice Boundary Tests

	[TestMethod]
	public void Slice_AtStart_ReturnsFullData ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var slice = bd.Slice (0);

		Assert.AreEqual (3, slice.Length);
		Assert.AreEqual (bd, slice);
	}

	[TestMethod]
	public void Slice_AtEnd_ReturnsEmpty ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var slice = bd.Slice (3);

		Assert.IsTrue (slice.IsEmpty);
	}

	[TestMethod]
	public void Slice_ZeroLength_ReturnsEmpty ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		var slice = bd.Slice (1, 0);

		Assert.IsTrue (slice.IsEmpty);
	}

	// FromUInt24BE Overflow Test

	[TestMethod]
	public void FromUInt24BE_ExceedsMax_ThrowsArgumentOutOfRange ()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() =>
			BinaryData.FromUInt24BE (0x1000000));
	}

	[TestMethod]
	public void FromUInt24BE_MaxValue_Succeeds ()
	{
		var bd = BinaryData.FromUInt24BE (0xFFFFFF);
		Assert.AreEqual (3, bd.Length);
		Assert.AreEqual ((byte)0xFF, bd[0]);
		Assert.AreEqual ((byte)0xFF, bd[1]);
		Assert.AreEqual ((byte)0xFF, bd[2]);
	}

	// Empty Comparison Tests

	[TestMethod]
	public void Empty_EqualsEmpty_IsTrue ()
	{
		Assert.AreEqual (BinaryData.Empty, BinaryData.Empty);
		Assert.IsTrue (BinaryData.Empty == BinaryData.Empty);
	}

	[TestMethod]
	public void Empty_EqualsNewEmpty_IsTrue ()
	{
		var newEmpty = new BinaryData ([]);
		Assert.AreEqual (BinaryData.Empty, newEmpty);
	}

	[TestMethod]
	public void Empty_CompareTo_Empty_ReturnsZero ()
	{
		Assert.AreEqual (0, BinaryData.Empty.CompareTo (BinaryData.Empty));
	}

	[TestMethod]
	public void Empty_LessThan_NonEmpty ()
	{
		var nonEmpty = new BinaryData ([0x01]);
		Assert.IsTrue (BinaryData.Empty < nonEmpty);
		Assert.IsFalse (nonEmpty < BinaryData.Empty);
	}

	[TestMethod]
	public void Empty_GetHashCode_IsDeterministic ()
	{
		var hash1 = BinaryData.Empty.GetHashCode ();
		var hash2 = BinaryData.Empty.GetHashCode ();
		Assert.AreEqual (hash1, hash2);
	}

	// Exception Tests - Insufficient Data

	[TestMethod]
	public void ToUInt16BE_InsufficientData_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01]); // Only 1 byte, need 2
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToUInt16BE ());
	}

	[TestMethod]
	public void ToUInt32BE_InsufficientData_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]); // Only 3 bytes, need 4
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToUInt32BE ());
	}

	[TestMethod]
	public void ToUInt64BE_InsufficientData_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03, 0x04]); // Only 4 bytes, need 8
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToUInt64BE ());
	}

	[TestMethod]
	public void ToInt16LE_InsufficientData_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01]); // Only 1 byte, need 2
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToInt16LE ());
	}

	[TestMethod]
	public void ToInt32LE_InsufficientData_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02]); // Only 2 bytes, need 4
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToInt32LE ());
	}

	[TestMethod]
	public void ToInt64LE_InsufficientData_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]); // Only 7 bytes, need 8
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToInt64LE ());
	}

	[TestMethod]
	public void ToUInt24BE_InsufficientData_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02]); // Only 2 bytes, need 3
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToUInt24BE ());
	}

	[TestMethod]
	public void ToSyncSafeUInt32_InsufficientData_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]); // Only 3 bytes, need 4
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToSyncSafeUInt32 ());
	}

	[TestMethod]
	public void ToUInt16BE_OffsetExceedsLength_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.ToUInt16BE (2)); // Offset 2, need 2 bytes, but only 1 left
	}

	// Exception Tests - Slice Invalid Ranges

	[TestMethod]
	public void Slice_NegativeStart_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.Slice (-1));
	}

	[TestMethod]
	public void Slice_StartBeyondLength_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.Slice (4));
	}

	[TestMethod]
	public void Slice_NegativeLength_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.Slice (0, -1));
	}

	[TestMethod]
	public void Slice_LengthExceedsBounds_ThrowsArgumentOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => bd.Slice (1, 5)); // Start=1, Length=5, but only 2 bytes available
	}

	// Exception Tests - Indexer Out of Bounds

	[TestMethod]
	public void Indexer_NegativeIndex_ThrowsIndexOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.ThrowsExactly<IndexOutOfRangeException> (() => _ = bd[-1]);
	}

	[TestMethod]
	public void Indexer_IndexEqualsLength_ThrowsIndexOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.ThrowsExactly<IndexOutOfRangeException> (() => _ = bd[3]);
	}

	[TestMethod]
	public void Indexer_IndexExceedsLength_ThrowsIndexOutOfRange ()
	{
		var bd = new BinaryData ([0x01, 0x02, 0x03]);
		Assert.ThrowsExactly<IndexOutOfRangeException> (() => _ = bd[10]);
	}

	// Exception Tests - Constructor Validation

	[TestMethod]
	public void Constructor_NegativeLength_ThrowsArgumentOutOfRange ()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new BinaryData (-1));
	}

	[TestMethod]
	public void Constructor_NegativeLengthWithFill_ThrowsArgumentOutOfRange ()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException> (() => new BinaryData (-5, 0xFF));
	}

	// Edge Case Tests - Trim with Custom Byte

	[TestMethod]
	public void Trim_CustomByte_TrimsSpecifiedByte ()
	{
		var bd = new BinaryData ([0xAA, 0xAA, 0x01, 0x02, 0xAA, 0xAA]);
		var trimmed = bd.Trim (0xAA);

		Assert.AreEqual (2, trimmed.Length);
		Assert.AreEqual ((byte)0x01, trimmed[0]);
		Assert.AreEqual ((byte)0x02, trimmed[1]);
	}

	[TestMethod]
	public void TrimStart_CustomByte_OnlyTrimsStart ()
	{
		var bd = new BinaryData ([0xBB, 0xBB, 0x01, 0x02, 0xBB, 0xBB]);
		var trimmed = bd.TrimStart (0xBB);

		Assert.AreEqual (4, trimmed.Length);
		Assert.AreEqual ((byte)0x01, trimmed[0]);
		Assert.AreEqual ((byte)0xBB, trimmed[3]);
	}

	[TestMethod]
	public void TrimEnd_CustomByte_OnlyTrimsEnd ()
	{
		var bd = new BinaryData ([0xCC, 0xCC, 0x01, 0x02, 0xCC, 0xCC]);
		var trimmed = bd.TrimEnd (0xCC);

		Assert.AreEqual (4, trimmed.Length);
		Assert.AreEqual ((byte)0xCC, trimmed[0]);
		Assert.AreEqual ((byte)0x02, trimmed[3]);
	}

	[TestMethod]
	public void Trim_AllMatchingBytes_ReturnsEmpty ()
	{
		var bd = new BinaryData ([0xFF, 0xFF, 0xFF]);
		var trimmed = bd.Trim (0xFF);

		Assert.IsTrue (trimmed.IsEmpty);
	}

	// Edge Case Tests - FromHexString with Spaces

	[TestMethod]
	public void FromHexString_WithSpaces_RemovesSpaces ()
	{
		var bd = BinaryData.FromHexString ("01 02 03 04");

		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0x01, bd[0]);
		Assert.AreEqual ((byte)0x02, bd[1]);
		Assert.AreEqual ((byte)0x03, bd[2]);
		Assert.AreEqual ((byte)0x04, bd[3]);
	}

	[TestMethod]
	public void FromHexString_WithMultipleSpaces_RemovesAllSpaces ()
	{
		var bd = BinaryData.FromHexString ("AABB  CCDD");

		Assert.AreEqual (4, bd.Length);
		Assert.AreEqual ((byte)0xAA, bd[0]);
		Assert.AreEqual ((byte)0xBB, bd[1]);
		Assert.AreEqual ((byte)0xCC, bd[2]);
		Assert.AreEqual ((byte)0xDD, bd[3]);
	}

	[TestMethod]
	public void FromHexString_OnlySpaces_ReturnsEmpty ()
	{
		var bd = BinaryData.FromHexString ("   ");

		Assert.IsTrue (bd.IsEmpty);
	}

	[TestMethod]
	public void FromHexString_EmptyString_ReturnsEmpty ()
	{
		var bd = BinaryData.FromHexString ("");

		Assert.IsTrue (bd.IsEmpty);
	}

	// Default struct behavior

	[TestMethod]
	public void Default_BinaryData_BehavesAsEmpty ()
	{
		BinaryData bd = default;

		Assert.AreEqual (0, bd.Length);
		Assert.IsTrue (bd.IsEmpty);
		Assert.AreEqual (BinaryData.Empty, bd);
	}

	[TestMethod]
	public void Default_BinaryData_CanEnumerate ()
	{
		BinaryData bd = default;
		var count = 0;
		foreach (var _ in bd)
			count++;

		Assert.AreEqual (0, count);
	}

	[TestMethod]
	public void Default_BinaryData_SpanIsEmpty ()
	{
		BinaryData bd = default;
		Assert.AreEqual (0, bd.Span.Length);
	}

}
