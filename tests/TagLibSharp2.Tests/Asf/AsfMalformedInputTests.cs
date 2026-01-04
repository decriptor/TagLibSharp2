// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
[TestCategory ("Security")]
public class AsfMalformedInputTests
{
	// ═══════════════════════════════════════════════════════════════
	// Empty/Minimal Input Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void EmptyInput_ReturnsFailure ()
	{
		var result = AsfFile.Read ([]);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void TruncatedGuid_ReturnsFailure ()
	{
		// Only 10 bytes - not enough for a GUID (16 bytes)
		var data = new byte[10];
		AsfGuids.HeaderObject.Render ().Span.Slice (0, 10).CopyTo (data);

		var result = AsfFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void TruncatedHeader_ReturnsFailure ()
	{
		// Full GUID but not enough for header size field
		var data = new byte[20];
		AsfGuids.HeaderObject.Render ().Span.CopyTo (data);

		var result = AsfFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	// ═══════════════════════════════════════════════════════════════
	// Size Overflow Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void HeaderSizeExceedsFileSize_ReturnsFailure ()
	{
		// Header claims to be 1GB but file is only 50 bytes
		var data = new byte[50];
		AsfGuids.HeaderObject.Render ().Span.CopyTo (data);
		// Write a huge size (1GB) at offset 16
		BitConverter.GetBytes (0x40000000UL).CopyTo (data, 16);

		var result = AsfFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void ObjectSizeExceedsRemaining_StopsParsing ()
	{
		// Create minimal WMA with a child object that claims huge size
		var data = AsfTestBuilder.CreateMinimalWma ();

		// Modify the file properties object size to be huge
		// File properties starts after header (30 bytes)
		// Find file properties object and corrupt its size
		var pos = 30; // After header
		while (pos + 24 < data.Length) {
			var guid = new byte[16];
			Array.Copy (data, pos, guid, 0, 16);
			if (guid.SequenceEqual (AsfGuids.FilePropertiesObject.Render ().ToArray ())) {
				// Found it - write a huge size
				BitConverter.GetBytes (0x7FFFFFFFUL).CopyTo (data, pos + 16);
				break;
			}
			// Read current size and skip to next object
			var objSize = BitConverter.ToUInt64 (data, pos + 16);
			if (objSize == 0 || objSize > (ulong)data.Length)
				break;
			pos += (int)objSize;
		}

		// Should not crash - parsing stops gracefully
		var result = AsfFile.Read (data);
		// May succeed or fail, but should not crash
		Assert.IsNotNull (result);
	}

	// ═══════════════════════════════════════════════════════════════
	// Descriptor Count Overflow Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void DescriptorCountHuge_DoesNotAllocateExcessively ()
	{
		// Create extended content description with huge descriptor count
		// but truncated data
		using var ms = new MemoryStream ();

		// Descriptor count = 1 billion
		ms.Write (BitConverter.GetBytes (0x3B9ACA00U), 0, 4); // 1 billion
															  // No actual descriptors

		var result = AsfExtendedContentDescription.Parse (ms.ToArray ());

		// Should return failure or empty list, not allocate 1 billion items
		// This test passes if it completes quickly without OOM
		if (result.IsSuccess)
			Assert.IsTrue (result.Value.Descriptors.Count < 1000000);
		// If parsing failed, that's also acceptable - we just shouldn't OOM
	}

	// ═══════════════════════════════════════════════════════════════
	// Invalid Encoding Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void InvalidUtf16_DoesNotCrash ()
	{
		// Create content description with odd-length string (invalid UTF-16)
		using var ms = new MemoryStream ();

		// String lengths (all 5 fixed fields)
		ms.Write (BitConverter.GetBytes ((ushort)3), 0, 2); // Title: 3 bytes (invalid - not even)
		ms.Write (BitConverter.GetBytes ((ushort)0), 0, 2); // Author
		ms.Write (BitConverter.GetBytes ((ushort)0), 0, 2); // Copyright
		ms.Write (BitConverter.GetBytes ((ushort)0), 0, 2); // Description
		ms.Write (BitConverter.GetBytes ((ushort)0), 0, 2); // Rating

		// Invalid UTF-16 data (odd bytes)
		ms.WriteByte (0x41);
		ms.WriteByte (0x00);
		ms.WriteByte (0x42); // Orphan byte

		var result = AsfContentDescription.Parse (ms.ToArray ());

		// Should not crash - returns something
		Assert.IsNotNull (result);
	}

	[TestMethod]
	public void StringLengthExceedsData_ReturnsFailure ()
	{
		// Content description claims title length of 1000 but only has 10 bytes
		using var ms = new MemoryStream ();

		// String lengths
		ms.Write (BitConverter.GetBytes ((ushort)1000), 0, 2); // Title: claims 1000 bytes
		ms.Write (BitConverter.GetBytes ((ushort)0), 0, 2);
		ms.Write (BitConverter.GetBytes ((ushort)0), 0, 2);
		ms.Write (BitConverter.GetBytes ((ushort)0), 0, 2);
		ms.Write (BitConverter.GetBytes ((ushort)0), 0, 2);

		// Only 10 bytes of actual data
		for (int i = 0; i < 10; i++)
			ms.WriteByte (0x41);

		var result = AsfContentDescription.Parse (ms.ToArray ());

		// Should fail gracefully
		Assert.IsFalse (result.IsSuccess);
	}

	// ═══════════════════════════════════════════════════════════════
	// Random/Fuzz Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void RandomData_DoesNotCrash ()
	{
		var random = new Random (42); // Fixed seed for reproducibility

		for (int i = 0; i < 100; i++) {
			var data = new byte[random.Next (1, 1000)];
			random.NextBytes (data);

			// This should not throw - just return failure
			var result = AsfFile.Read (data);
			Assert.IsNotNull (result);
		}
	}

	[TestMethod]
	public void RandomDataWithAsfHeader_DoesNotCrash ()
	{
		var random = new Random (123);

		for (int i = 0; i < 100; i++) {
			var data = new byte[random.Next (30, 500)];
			random.NextBytes (data);

			// Put valid ASF header GUID at start
			AsfGuids.HeaderObject.Render ().Span.CopyTo (data);
			// Put some size that fits
			BitConverter.GetBytes ((ulong)data.Length).CopyTo (data, 16);

			// This should not throw
			var result = AsfFile.Read (data);
			Assert.IsNotNull (result);
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Boundary Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void ExactMinimumSize_Parses ()
	{
		// Create exactly 30-byte minimum header
		var data = new byte[30];
		AsfGuids.HeaderObject.Render ().Span.CopyTo (data);
		BitConverter.GetBytes (30UL).CopyTo (data, 16); // Size
		BitConverter.GetBytes (0U).CopyTo (data, 24);   // Child count
		data[28] = 0x01; // Reserved
		data[29] = 0x02;

		var result = AsfFile.Read (data);

		// Should succeed with empty tag
		Assert.IsTrue (result.IsSuccess);
	}

	[TestMethod]
	public void ZeroChildCount_ReturnsEmptyTag ()
	{
		var data = new byte[30];
		AsfGuids.HeaderObject.Render ().Span.CopyTo (data);
		BitConverter.GetBytes (30UL).CopyTo (data, 16);
		BitConverter.GetBytes (0U).CopyTo (data, 24); // Zero children

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsTrue (result.File!.Tag.IsEmpty);
	}
}
