// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfExtendedContentDescriptionTests
{
	// ═══════════════════════════════════════════════════════════════
	// Parsing Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Parse_EmptyDescriptors_ReturnsEmptyList ()
	{
		// Just descriptor count = 0
		var data = new byte[] { 0x00, 0x00 };

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.Value.Descriptors.Count);
	}

	[TestMethod]
	public void Parse_SingleStringDescriptor_ExtractsCorrectly ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Test Album"));

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Value.Descriptors.Count);
		Assert.AreEqual ("WM/AlbumTitle", result.Value.Descriptors[0].Name);
		Assert.AreEqual ("Test Album", result.Value.Descriptors[0].StringValue);
	}

	[TestMethod]
	public void Parse_SingleDwordDescriptor_ExtractsCorrectly ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateDword ("WM/TrackNumber", 5));

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Value.Descriptors.Count);
		Assert.AreEqual ("WM/TrackNumber", result.Value.Descriptors[0].Name);
		Assert.AreEqual (5u, result.Value.Descriptors[0].DwordValue);
	}

	[TestMethod]
	public void Parse_SingleQwordDescriptor_ExtractsCorrectly ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateQword ("WM/Duration", 123456789UL));

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Value.Descriptors.Count);
		Assert.AreEqual ("WM/Duration", result.Value.Descriptors[0].Name);
		Assert.AreEqual (123456789UL, result.Value.Descriptors[0].QwordValue);
	}

	[TestMethod]
	public void Parse_SingleBoolDescriptor_ExtractsCorrectly ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateBool ("WM/IsCompilation", true));

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Value.Descriptors.Count);
		Assert.AreEqual ("WM/IsCompilation", result.Value.Descriptors[0].Name);
		Assert.AreEqual (true, result.Value.Descriptors[0].BoolValue);
	}

	[TestMethod]
	public void Parse_SingleBinaryDescriptor_ExtractsCorrectly ()
	{
		var pictureData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateBinary ("WM/Picture", pictureData));

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Value.Descriptors.Count);
		Assert.AreEqual ("WM/Picture", result.Value.Descriptors[0].Name);
		CollectionAssert.AreEqual (pictureData, result.Value.Descriptors[0].BinaryValue!.Value.ToArray ());
	}

	[TestMethod]
	public void Parse_MultipleDescriptors_ExtractsAll ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateString ("WM/AlbumTitle", "My Album"),
			AsfDescriptor.CreateString ("WM/Genre", "Rock"),
			AsfDescriptor.CreateDword ("WM/TrackNumber", 3),
			AsfDescriptor.CreateString ("WM/Year", "2025"));

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4, result.Value.Descriptors.Count);
		Assert.AreEqual ("My Album", result.Value.Descriptors[0].StringValue);
		Assert.AreEqual ("Rock", result.Value.Descriptors[1].StringValue);
		Assert.AreEqual (3u, result.Value.Descriptors[2].DwordValue);
		Assert.AreEqual ("2025", result.Value.Descriptors[3].StringValue);
	}

	[TestMethod]
	public void Parse_UnicodeNames_DecodesCorrectly ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateString ("日本語/タイトル", "Value"));

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("日本語/タイトル", result.Value.Descriptors[0].Name);
	}

	[TestMethod]
	public void Parse_UnicodeValues_DecodesCorrectly ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Café 中文 日本語"));

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Café 中文 日本語", result.Value.Descriptors[0].StringValue);
	}

	[TestMethod]
	public void Parse_TruncatedInput_ReturnsFailure ()
	{
		// Claims 1 descriptor but has no data for it
		var data = new byte[] { 0x01, 0x00 };

		var result = AsfExtendedContentDescription.Parse (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Parse_EmptyInput_ReturnsFailure ()
	{
		var result = AsfExtendedContentDescription.Parse (ReadOnlySpan<byte>.Empty);

		Assert.IsFalse (result.IsSuccess);
	}

	// ═══════════════════════════════════════════════════════════════
	// Lookup Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void GetDescriptor_ExistingName_ReturnsDescriptor ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Test Album"));

		var result = AsfExtendedContentDescription.Parse (data);
		var desc = result.Value.GetDescriptor ("WM/AlbumTitle");

		Assert.IsNotNull (desc);
		Assert.AreEqual ("Test Album", desc.StringValue);
	}

	[TestMethod]
	public void GetDescriptor_NonExistingName_ReturnsNull ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Test Album"));

		var result = AsfExtendedContentDescription.Parse (data);
		var desc = result.Value.GetDescriptor ("WM/Genre");

		Assert.IsNull (desc);
	}

	[TestMethod]
	public void GetDescriptor_CaseInsensitive_Matches ()
	{
		var data = CreateExtendedContentData (
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Test Album"));

		var result = AsfExtendedContentDescription.Parse (data);
		var desc = result.Value.GetDescriptor ("wm/albumtitle");

		Assert.IsNotNull (desc);
		Assert.AreEqual ("Test Album", desc.StringValue);
	}

	// ═══════════════════════════════════════════════════════════════
	// Rendering Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Render_EmptyDescriptors_WritesZeroCount ()
	{
		var ext = new AsfExtendedContentDescription ([]);

		var rendered = ext.Render ();

		Assert.AreEqual (2, rendered.Length);
		Assert.AreEqual (0, rendered.Span[0]);
		Assert.AreEqual (0, rendered.Span[1]);
	}

	[TestMethod]
	public void RoundTrip_SingleDescriptor_Preserves ()
	{
		var original = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Test Album")
		]);

		var rendered = original.Render ();
		var parsed = AsfExtendedContentDescription.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (1, parsed.Value.Descriptors.Count);
		Assert.AreEqual ("WM/AlbumTitle", parsed.Value.Descriptors[0].Name);
		Assert.AreEqual ("Test Album", parsed.Value.Descriptors[0].StringValue);
	}

	[TestMethod]
	public void RoundTrip_MultipleDescriptors_PreservesAll ()
	{
		var original = new AsfExtendedContentDescription ([
			AsfDescriptor.CreateString ("WM/AlbumTitle", "Test Album"),
			AsfDescriptor.CreateString ("WM/Genre", "Rock"),
			AsfDescriptor.CreateDword ("WM/TrackNumber", 5),
			AsfDescriptor.CreateBool ("WM/IsCompilation", true),
			AsfDescriptor.CreateQword ("Duration", 180000000UL)
		]);

		var rendered = original.Render ();
		var parsed = AsfExtendedContentDescription.Parse (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (5, parsed.Value.Descriptors.Count);
		Assert.AreEqual ("Test Album", parsed.Value.Descriptors[0].StringValue);
		Assert.AreEqual ("Rock", parsed.Value.Descriptors[1].StringValue);
		Assert.AreEqual (5u, parsed.Value.Descriptors[2].DwordValue);
		Assert.AreEqual (true, parsed.Value.Descriptors[3].BoolValue);
		Assert.AreEqual (180000000UL, parsed.Value.Descriptors[4].QwordValue);
	}

	// ═══════════════════════════════════════════════════════════════
	// Helper Methods
	// ═══════════════════════════════════════════════════════════════

	static byte[] CreateExtendedContentData (params AsfDescriptor[] descriptors)
	{
		// Use the test builder
		return AsfTestBuilder.CreateExtendedContentDescriptionObject (descriptors)[24..]; // Skip GUID+size
	}
}
