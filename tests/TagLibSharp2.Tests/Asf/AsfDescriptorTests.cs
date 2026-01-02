// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfDescriptorTests
{
	// Helper to create UTF-16LE bytes with null terminator
	static byte[] CreateUtf16String (string value)
	{
		var bytes = Encoding.Unicode.GetBytes (value);
		var result = new byte[bytes.Length + 2]; // +2 for null terminator
		Array.Copy (bytes, result, bytes.Length);
		return result;
	}

	// ═══════════════════════════════════════════════════════════════
	// Factory Method Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void CreateString_SetsCorrectType ()
	{
		var attr = AsfDescriptor.CreateString ("WM/AlbumTitle", "Test Album");

		Assert.AreEqual ("WM/AlbumTitle", attr.Name);
		Assert.AreEqual (AsfAttributeType.UnicodeString, attr.Type);
		Assert.AreEqual ("Test Album", attr.StringValue);
	}

	[TestMethod]
	public void CreateDword_SetsCorrectType ()
	{
		var attr = AsfDescriptor.CreateDword ("WM/TrackNumber", 5);

		Assert.AreEqual ("WM/TrackNumber", attr.Name);
		Assert.AreEqual (AsfAttributeType.Dword, attr.Type);
		Assert.AreEqual (5u, attr.DwordValue);
	}

	[TestMethod]
	public void CreateQword_SetsCorrectType ()
	{
		var attr = AsfDescriptor.CreateQword ("WM/Duration", 123456789UL);

		Assert.AreEqual ("WM/Duration", attr.Name);
		Assert.AreEqual (AsfAttributeType.Qword, attr.Type);
		Assert.AreEqual (123456789UL, attr.QwordValue);
	}

	[TestMethod]
	public void CreateWord_SetsCorrectType ()
	{
		var attr = AsfDescriptor.CreateWord ("SomeWord", 42);

		Assert.AreEqual ("SomeWord", attr.Name);
		Assert.AreEqual (AsfAttributeType.Word, attr.Type);
		Assert.AreEqual ((ushort)42, attr.WordValue);
	}

	[TestMethod]
	public void CreateBool_True_SetsCorrectType ()
	{
		var attr = AsfDescriptor.CreateBool ("IsCompilation", true);

		Assert.AreEqual ("IsCompilation", attr.Name);
		Assert.AreEqual (AsfAttributeType.Bool, attr.Type);
		Assert.AreEqual (true, attr.BoolValue);
	}

	[TestMethod]
	public void CreateBool_False_SetsCorrectType ()
	{
		var attr = AsfDescriptor.CreateBool ("IsCompilation", false);

		Assert.AreEqual (AsfAttributeType.Bool, attr.Type);
		Assert.AreEqual (false, attr.BoolValue);
	}

	[TestMethod]
	public void CreateBinary_SetsCorrectType ()
	{
		var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
		var attr = AsfDescriptor.CreateBinary ("WM/Picture", data);

		Assert.AreEqual ("WM/Picture", attr.Name);
		Assert.AreEqual (AsfAttributeType.Binary, attr.Type);
		CollectionAssert.AreEqual (data, attr.BinaryValue!.Value.ToArray ());
	}

	// ═══════════════════════════════════════════════════════════════
	// Type Accessor Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void StringValue_OnNonString_ReturnsNull ()
	{
		var attr = AsfDescriptor.CreateDword ("Number", 42);

		Assert.IsNull (attr.StringValue);
	}

	[TestMethod]
	public void DwordValue_OnNonDword_ReturnsNull ()
	{
		var attr = AsfDescriptor.CreateString ("Text", "hello");

		Assert.IsNull (attr.DwordValue);
	}

	[TestMethod]
	public void QwordValue_OnNonQword_ReturnsNull ()
	{
		var attr = AsfDescriptor.CreateString ("Text", "hello");

		Assert.IsNull (attr.QwordValue);
	}

	[TestMethod]
	public void BoolValue_OnNonBool_ReturnsNull ()
	{
		var attr = AsfDescriptor.CreateString ("Text", "hello");

		Assert.IsNull (attr.BoolValue);
	}

	// ═══════════════════════════════════════════════════════════════
	// Rendering Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void RenderValue_String_UsesUtf16LE ()
	{
		var attr = AsfDescriptor.CreateString ("Name", "Test");

		var rendered = attr.RenderValue ();

		// UTF-16LE: "Test" = 54 00 65 00 73 00 74 00 + null terminator 00 00
		var expected = CreateUtf16String ("Test");
		CollectionAssert.AreEqual (expected, rendered.ToArray ());
	}

	[TestMethod]
	public void RenderValue_Dword_Uses4BytesLE ()
	{
		var attr = AsfDescriptor.CreateDword ("Number", 0x12345678);

		var rendered = attr.RenderValue ();

		Assert.AreEqual (4, rendered.Length);
		CollectionAssert.AreEqual (new byte[] { 0x78, 0x56, 0x34, 0x12 }, rendered.ToArray ());
	}

	[TestMethod]
	public void RenderValue_Qword_Uses8BytesLE ()
	{
		var attr = AsfDescriptor.CreateQword ("BigNumber", 0x123456789ABCDEF0);

		var rendered = attr.RenderValue ();

		Assert.AreEqual (8, rendered.Length);
		CollectionAssert.AreEqual (
			new byte[] { 0xF0, 0xDE, 0xBC, 0x9A, 0x78, 0x56, 0x34, 0x12 },
			rendered.ToArray ());
	}

	[TestMethod]
	public void RenderValue_Word_Uses2BytesLE ()
	{
		var attr = AsfDescriptor.CreateWord ("SmallNumber", 0x1234);

		var rendered = attr.RenderValue ();

		Assert.AreEqual (2, rendered.Length);
		CollectionAssert.AreEqual (new byte[] { 0x34, 0x12 }, rendered.ToArray ());
	}

	[TestMethod]
	public void RenderValue_Bool_True_Uses4BytesNonZero ()
	{
		var attr = AsfDescriptor.CreateBool ("Flag", true);

		var rendered = attr.RenderValue ();

		Assert.AreEqual (4, rendered.Length);
		CollectionAssert.AreEqual (new byte[] { 0x01, 0x00, 0x00, 0x00 }, rendered.ToArray ());
	}

	[TestMethod]
	public void RenderValue_Bool_False_Uses4BytesZero ()
	{
		var attr = AsfDescriptor.CreateBool ("Flag", false);

		var rendered = attr.RenderValue ();

		Assert.AreEqual (4, rendered.Length);
		CollectionAssert.AreEqual (new byte[] { 0x00, 0x00, 0x00, 0x00 }, rendered.ToArray ());
	}

	[TestMethod]
	public void RenderValue_Binary_ReturnsRawBytes ()
	{
		var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
		var attr = AsfDescriptor.CreateBinary ("Data", data);

		var rendered = attr.RenderValue ();

		CollectionAssert.AreEqual (data, rendered.ToArray ());
	}

	// ═══════════════════════════════════════════════════════════════
	// Unicode Edge Cases
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void CreateString_EmptyString_Works ()
	{
		var attr = AsfDescriptor.CreateString ("Empty", "");

		Assert.AreEqual ("", attr.StringValue);
	}

	[TestMethod]
	public void CreateString_Unicode_PreservesCharacters ()
	{
		var attr = AsfDescriptor.CreateString ("Title", "Caf\u00E9 \u4E2D\u6587");

		Assert.AreEqual ("Caf\u00E9 \u4E2D\u6587", attr.StringValue);
	}

	[TestMethod]
	public void RenderValue_Unicode_EncodesCorrectly ()
	{
		var attr = AsfDescriptor.CreateString ("Title", "\u4E2D"); // Chinese character

		var rendered = attr.RenderValue ();

		// \u4E2D in UTF-16LE = 2D 4E
		// Plus null terminator = 00 00
		CollectionAssert.AreEqual (new byte[] { 0x2D, 0x4E, 0x00, 0x00 }, rendered.ToArray ());
	}
}
