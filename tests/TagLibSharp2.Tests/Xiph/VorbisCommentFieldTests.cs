// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Xiph")]
public class VorbisCommentFieldTests
{
	[TestMethod]
	public void Constructor_StoresNameAndValue ()
	{
		var field = new VorbisCommentField ("TITLE", "Test Song");

		Assert.AreEqual ("TITLE", field.Name);
		Assert.AreEqual ("Test Song", field.Value);
	}

	[TestMethod]
	public void Constructor_UppercasesName ()
	{
		var field = new VorbisCommentField ("title", "Test Song");

		Assert.AreEqual ("TITLE", field.Name);
	}

	[TestMethod]
	public void Constructor_NullName_ThrowsArgumentNullException ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() => new VorbisCommentField (null!, "value"));
	}

	[TestMethod]
	public void Constructor_NullValue_ThrowsArgumentNullException ()
	{
		Assert.ThrowsExactly<ArgumentNullException> (() => new VorbisCommentField ("NAME", null!));
	}

	[TestMethod]
	public void Constructor_EmptyName_ThrowsArgumentException ()
	{
		Assert.ThrowsExactly<ArgumentException> (() => new VorbisCommentField ("", "value"));
	}

	[TestMethod]
	public void Constructor_InvalidNameWithEquals_ThrowsArgumentException ()
	{
		Assert.ThrowsExactly<ArgumentException> (() => new VorbisCommentField ("NAME=BAD", "value"));
	}

	[TestMethod]
	public void ToString_ReturnsCorrectFormat ()
	{
		var field = new VorbisCommentField ("ARTIST", "The Beatles");

		Assert.AreEqual ("ARTIST=The Beatles", field.ToString ());
	}

	[TestMethod]
	public void Parse_ValidField_ReturnsCorrectField ()
	{
		var result = VorbisCommentField.Parse ("TITLE=My Song");

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("TITLE", result.Field.Name);
		Assert.AreEqual ("My Song", result.Field.Value);
	}

	[TestMethod]
	public void Parse_LowercaseName_UppercasesName ()
	{
		var result = VorbisCommentField.Parse ("artist=Some Artist");

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("ARTIST", result.Field.Name);
		Assert.AreEqual ("Some Artist", result.Field.Value);
	}

	[TestMethod]
	public void Parse_EmptyValue_Works ()
	{
		var result = VorbisCommentField.Parse ("COMMENT=");

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("COMMENT", result.Field.Name);
		Assert.AreEqual ("", result.Field.Value);
	}

	[TestMethod]
	public void Parse_ValueWithEquals_KeepsEquals ()
	{
		var result = VorbisCommentField.Parse ("DESCRIPTION=a=b=c");

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("DESCRIPTION", result.Field.Name);
		Assert.AreEqual ("a=b=c", result.Field.Value);
	}

	[TestMethod]
	public void Parse_NoEquals_ReturnsFailure ()
	{
		var result = VorbisCommentField.Parse ("INVALID_FIELD");

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Parse_EmptyName_ReturnsFailure ()
	{
		var result = VorbisCommentField.Parse ("=value");

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Equals_SameNameAndValue_ReturnsTrue ()
	{
		var field1 = new VorbisCommentField ("TITLE", "Song");
		var field2 = new VorbisCommentField ("TITLE", "Song");

		Assert.AreEqual (field1, field2);
		Assert.IsTrue (field1 == field2);
	}

	[TestMethod]
	public void Equals_DifferentCase_TreatedAsEqual ()
	{
		var field1 = new VorbisCommentField ("title", "Song");
		var field2 = new VorbisCommentField ("TITLE", "Song");

		Assert.AreEqual (field1, field2);
	}

	[TestMethod]
	public void Equals_DifferentValue_ReturnsFalse ()
	{
		var field1 = new VorbisCommentField ("TITLE", "Song1");
		var field2 = new VorbisCommentField ("TITLE", "Song2");

		Assert.AreNotEqual (field1, field2);
		Assert.IsTrue (field1 != field2);
	}

	[TestMethod]
	public void GetHashCode_SameFields_ReturnsSameHash ()
	{
		var field1 = new VorbisCommentField ("ARTIST", "Test");
		var field2 = new VorbisCommentField ("artist", "Test");

		Assert.AreEqual (field1.GetHashCode (), field2.GetHashCode ());
	}

	[TestMethod]
	public void Constructor_InvalidAsciiCharacter_ThrowsArgumentException ()
	{
		// Field names must be ASCII 0x20-0x7D (printable ASCII minus delete)
		// Tab (0x09) is not valid
		Assert.ThrowsExactly<ArgumentException> (() => new VorbisCommentField ("TAB\tHERE", "value"));
	}

	[TestMethod]
	public void Constructor_ControlCharacter_ThrowsArgumentException ()
	{
		// Control characters (0x00-0x1F) are not valid
		Assert.ThrowsExactly<ArgumentException> (() => new VorbisCommentField ("CTRL\x01X", "value"));
	}

	[TestMethod]
	public void Constructor_DeleteCharacter_ThrowsArgumentException ()
	{
		// DEL (0x7F) is not valid
		Assert.ThrowsExactly<ArgumentException> (() => new VorbisCommentField ("DEL\x7FX", "value"));
	}

	[TestMethod]
	public void Constructor_HighAscii_ThrowsArgumentException ()
	{
		// Characters above 0x7D are not valid
		Assert.ThrowsExactly<ArgumentException> (() => new VorbisCommentField ("HIGH\x80X", "value"));
	}

	[TestMethod]
	public void Constructor_ValidPrintableAscii_Succeeds ()
	{
		// Test various valid ASCII characters (0x20-0x7D excluding 0x3D)
		var field = new VorbisCommentField ("FIELD_NAME-123", "value");
		Assert.AreEqual ("FIELD_NAME-123", field.Name);
	}

	[TestMethod]
	public void Parse_InvalidAsciiInName_ReturnsFailure ()
	{
		var result = VorbisCommentField.Parse ("TAB\tNAME=value");

		Assert.IsFalse (result.IsSuccess);
		Assert.Contains ("character", result.Error!.ToLowerInvariant ());
	}
}
