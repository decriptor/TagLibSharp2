// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfGuidTests
{
	// ASF Header Object GUID: 75B22630-668E-11CF-A6D9-00AA0062CE6C
	// Stored little-endian mixed format per ASF spec
	static readonly byte[] HeaderObjectGuidBytes = [
		0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
		0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
	];

	// Content Description GUID: 75B22633-668E-11CF-A6D9-00AA0062CE6C
	static readonly byte[] ContentDescriptionGuidBytes = [
		0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
		0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
	];

	[TestMethod]
	public void Parse_ValidGuidBytes_ReturnsCorrectGuid ()
	{
		var guid = AsfGuid.Parse (HeaderObjectGuidBytes);

		Assert.IsTrue (guid.IsSuccess);
		Assert.AreEqual (16, guid.BytesConsumed);
	}

	[TestMethod]
	public void Parse_HeaderObjectGuid_MatchesSpec ()
	{
		var result = AsfGuid.Parse (HeaderObjectGuidBytes);

		Assert.IsTrue (result.IsSuccess);
		// Verify by re-rendering and comparing bytes
		var rendered = result.Value.Render ();
		CollectionAssert.AreEqual (HeaderObjectGuidBytes, rendered.ToArray ());
	}

	[TestMethod]
	public void Parse_TooShort_ReturnsFailure ()
	{
		var shortData = new byte[15]; // Need 16 bytes

		var result = AsfGuid.Parse (shortData);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Parse_EmptyInput_ReturnsFailure ()
	{
		var result = AsfGuid.Parse ([]);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Equals_IdenticalGuids_ReturnsTrue ()
	{
		var guid1 = AsfGuid.Parse (HeaderObjectGuidBytes).Value;
		var guid2 = AsfGuid.Parse (HeaderObjectGuidBytes).Value;

		Assert.AreEqual (guid1, guid2);
		Assert.IsTrue (guid1 == guid2);
		Assert.IsFalse (guid1 != guid2);
	}

	[TestMethod]
	public void Equals_DifferentGuids_ReturnsFalse ()
	{
		var guid1 = AsfGuid.Parse (HeaderObjectGuidBytes).Value;
		var guid2 = AsfGuid.Parse (ContentDescriptionGuidBytes).Value;

		Assert.AreNotEqual (guid1, guid2);
		Assert.IsFalse (guid1 == guid2);
		Assert.IsTrue (guid1 != guid2);
	}

	[TestMethod]
	public void GetHashCode_IdenticalGuids_SameHash ()
	{
		var guid1 = AsfGuid.Parse (HeaderObjectGuidBytes).Value;
		var guid2 = AsfGuid.Parse (HeaderObjectGuidBytes).Value;

		Assert.AreEqual (guid1.GetHashCode (), guid2.GetHashCode ());
	}

	[TestMethod]
	public void GetHashCode_DifferentGuids_DifferentHash ()
	{
		var guid1 = AsfGuid.Parse (HeaderObjectGuidBytes).Value;
		var guid2 = AsfGuid.Parse (ContentDescriptionGuidBytes).Value;

		// Different GUIDs should have different hash codes (statistically)
		Assert.AreNotEqual (guid1.GetHashCode (), guid2.GetHashCode ());
	}

	[TestMethod]
	public void Render_ToBytes_MatchesInput ()
	{
		var guid = AsfGuid.Parse (HeaderObjectGuidBytes).Value;

		var rendered = guid.Render ();

		Assert.AreEqual (16, rendered.Length);
		CollectionAssert.AreEqual (HeaderObjectGuidBytes, rendered.ToArray ());
	}

	[TestMethod]
	public void Render_ContentDescriptionGuid_MatchesInput ()
	{
		var guid = AsfGuid.Parse (ContentDescriptionGuidBytes).Value;

		var rendered = guid.Render ();

		CollectionAssert.AreEqual (ContentDescriptionGuidBytes, rendered.ToArray ());
	}

	[TestMethod]
	public void Equals_WithObject_WorksCorrectly ()
	{
		var guid1 = AsfGuid.Parse (HeaderObjectGuidBytes).Value;
		var guid2 = AsfGuid.Parse (HeaderObjectGuidBytes).Value;
		object boxedGuid2 = guid2;

		Assert.IsTrue (guid1.Equals (boxedGuid2));
	}

	[TestMethod]
	public void Equals_WithNull_ReturnsFalse ()
	{
		var guid = AsfGuid.Parse (HeaderObjectGuidBytes).Value;

		Assert.IsFalse (guid.Equals (null));
	}

	[TestMethod]
	public void Equals_WithDifferentType_ReturnsFalse ()
	{
		var guid = AsfGuid.Parse (HeaderObjectGuidBytes).Value;

		Assert.IsFalse (guid.Equals ("not a guid"));
	}

	[TestMethod]
	public void Default_IsEmpty ()
	{
		var defaultGuid = default (AsfGuid);

		// Default should be all zeros
		var rendered = defaultGuid.Render ();
		Assert.IsTrue (rendered.ToArray ().All (b => b == 0));
	}

	[TestMethod]
	public void Parse_AllZeros_Succeeds ()
	{
		var zeroBytes = new byte[16];

		var result = AsfGuid.Parse (zeroBytes);

		Assert.IsTrue (result.IsSuccess);
	}

	[TestMethod]
	public void Parse_AllOnes_Succeeds ()
	{
		var onesBytes = Enumerable.Repeat ((byte)0xFF, 16).ToArray ();

		var result = AsfGuid.Parse (onesBytes);

		Assert.IsTrue (result.IsSuccess);
		var rendered = result.Value.Render ();
		CollectionAssert.AreEqual (onesBytes, rendered.ToArray ());
	}
}
