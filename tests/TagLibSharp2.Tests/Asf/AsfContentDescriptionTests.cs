// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfContentDescriptionTests
{
	// ═══════════════════════════════════════════════════════════════
	// Parsing Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Parse_ExtractsTitle ()
	{
		var data = CreateContentDescriptionData ("Test Title", null, null, null, null);

		var result = AsfContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Title", result.Value.Title);
	}

	[TestMethod]
	public void Parse_ExtractsAuthor ()
	{
		var data = CreateContentDescriptionData (null, "Test Artist", null, null, null);

		var result = AsfContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Artist", result.Value.Author);
	}

	[TestMethod]
	public void Parse_ExtractsCopyright ()
	{
		var data = CreateContentDescriptionData (null, null, "2025 Test", null, null);

		var result = AsfContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("2025 Test", result.Value.Copyright);
	}

	[TestMethod]
	public void Parse_ExtractsDescription ()
	{
		var data = CreateContentDescriptionData (null, null, null, "A description", null);

		var result = AsfContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("A description", result.Value.Description);
	}

	[TestMethod]
	public void Parse_ExtractsRating ()
	{
		var data = CreateContentDescriptionData (null, null, null, null, "5 stars");

		var result = AsfContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("5 stars", result.Value.Rating);
	}

	[TestMethod]
	public void Parse_AllFields_ExtractsAll ()
	{
		var data = CreateContentDescriptionData (
			"My Title",
			"My Author",
			"Copyright 2025",
			"Track description",
			"Excellent");

		var result = AsfContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("My Title", result.Value.Title);
		Assert.AreEqual ("My Author", result.Value.Author);
		Assert.AreEqual ("Copyright 2025", result.Value.Copyright);
		Assert.AreEqual ("Track description", result.Value.Description);
		Assert.AreEqual ("Excellent", result.Value.Rating);
	}

	[TestMethod]
	public void Parse_EmptyStrings_ReturnsEmpty ()
	{
		var data = CreateContentDescriptionData ("", "", "", "", "");

		var result = AsfContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Value.Title);
		Assert.AreEqual ("", result.Value.Author);
		Assert.AreEqual ("", result.Value.Copyright);
		Assert.AreEqual ("", result.Value.Description);
		Assert.AreEqual ("", result.Value.Rating);
	}

	[TestMethod]
	public void Parse_Unicode_DecodesCorrectly ()
	{
		var data = CreateContentDescriptionData ("Café 中文", "日本語 Artist", null, null, null);

		var result = AsfContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Café 中文", result.Value.Title);
		Assert.AreEqual ("日本語 Artist", result.Value.Author);
	}

	[TestMethod]
	public void Parse_TruncatedInput_ReturnsFailure ()
	{
		// Only 8 bytes - not enough for 5 length fields (10 bytes minimum)
		var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

		var result = AsfContentDescription.Parse (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Parse_LengthExceedsData_ReturnsFailure ()
	{
		// Claims title is 100 bytes but data is shorter
		var data = new byte[] {
			0x64, 0x00, // Title length = 100
			0x00, 0x00, // Author length = 0
			0x00, 0x00, // Copyright length = 0
			0x00, 0x00, // Description length = 0
			0x00, 0x00  // Rating length = 0
			// Missing 100 bytes of title data
		};

		var result = AsfContentDescription.Parse (data);

		Assert.IsFalse (result.IsSuccess);
	}

	// ═══════════════════════════════════════════════════════════════
	// Rendering Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_AllFields_WritesCorrectFormat ()
	{
		var desc = new AsfContentDescription (
			"Title",
			"Author",
			"Copyright",
			"Description",
			"Rating");

		var rendered = desc.Render ();

		// Parse it back to verify
		var result = AsfContentDescription.Parse (rendered.Span);
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Title", result.Value.Title);
		Assert.AreEqual ("Author", result.Value.Author);
		Assert.AreEqual ("Copyright", result.Value.Copyright);
		Assert.AreEqual ("Description", result.Value.Description);
		Assert.AreEqual ("Rating", result.Value.Rating);
	}

	[TestMethod]
	public void Render_EmptyFields_WritesZeroLengths ()
	{
		var desc = new AsfContentDescription ("", "", "", "", "");

		var rendered = desc.Render ();

		// First 10 bytes should be lengths (all zero or just null terminators)
		Assert.IsTrue (rendered.Length >= 10);

		// Parse back
		var result = AsfContentDescription.Parse (rendered.Span);
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Value.Title);
	}

	[TestMethod]
	public void RoundTrip_PreservesAllFields ()
	{
		var original = new AsfContentDescription (
			"Test Title 中文",
			"Test Author",
			"© 2025",
			"A longer description with special chars: ñ é ü",
			"★★★★★");

		var rendered = original.Render ();
		var parsed = AsfContentDescription.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (original.Title, parsed.Value.Title);
		Assert.AreEqual (original.Author, parsed.Value.Author);
		Assert.AreEqual (original.Copyright, parsed.Value.Copyright);
		Assert.AreEqual (original.Description, parsed.Value.Description);
		Assert.AreEqual (original.Rating, parsed.Value.Rating);
	}

	// ═══════════════════════════════════════════════════════════════
	// Helper Methods
	// ═══════════════════════════════════════════════════════════════

	static byte[] CreateContentDescriptionData (
		string? title,
		string? author,
		string? copyright,
		string? description,
		string? rating)
	{
		var titleBytes = CreateUtf16String (title ?? "");
		var authorBytes = CreateUtf16String (author ?? "");
		var copyrightBytes = CreateUtf16String (copyright ?? "");
		var descriptionBytes = CreateUtf16String (description ?? "");
		var ratingBytes = CreateUtf16String (rating ?? "");

		var result = new byte[10 + titleBytes.Length + authorBytes.Length +
			copyrightBytes.Length + descriptionBytes.Length + ratingBytes.Length];
		var offset = 0;

		// Write lengths (little-endian 16-bit)
		WriteUInt16LE (result, offset, (ushort)titleBytes.Length);
		offset += 2;
		WriteUInt16LE (result, offset, (ushort)authorBytes.Length);
		offset += 2;
		WriteUInt16LE (result, offset, (ushort)copyrightBytes.Length);
		offset += 2;
		WriteUInt16LE (result, offset, (ushort)descriptionBytes.Length);
		offset += 2;
		WriteUInt16LE (result, offset, (ushort)ratingBytes.Length);
		offset += 2;

		// Write strings
		Array.Copy (titleBytes, 0, result, offset, titleBytes.Length);
		offset += titleBytes.Length;
		Array.Copy (authorBytes, 0, result, offset, authorBytes.Length);
		offset += authorBytes.Length;
		Array.Copy (copyrightBytes, 0, result, offset, copyrightBytes.Length);
		offset += copyrightBytes.Length;
		Array.Copy (descriptionBytes, 0, result, offset, descriptionBytes.Length);
		offset += descriptionBytes.Length;
		Array.Copy (ratingBytes, 0, result, offset, ratingBytes.Length);

		return result;
	}

	static byte[] CreateUtf16String (string value)
	{
		var bytes = System.Text.Encoding.Unicode.GetBytes (value);
		var result = new byte[bytes.Length + 2]; // +2 for null terminator
		Array.Copy (bytes, result, bytes.Length);
		return result;
	}

	static void WriteUInt16LE (byte[] buffer, int offset, ushort value)
	{
		buffer[offset] = (byte)(value & 0xFF);
		buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
	}
}
