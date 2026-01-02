// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfGuidsTests
{
	[TestMethod]
	public void HeaderObjectGuid_MatchesSpecification ()
	{
		// 75B22630-668E-11CF-A6D9-00AA0062CE6C
		var expected = new byte[] {
			0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
			0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
		};

		var rendered = AsfGuids.HeaderObject.Render ();
		CollectionAssert.AreEqual (expected, rendered.ToArray ());
	}

	[TestMethod]
	public void DataObjectGuid_MatchesSpecification ()
	{
		// 75B22636-668E-11CF-A6D9-00AA0062CE6C
		var expected = new byte[] {
			0x36, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
			0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
		};

		var rendered = AsfGuids.DataObject.Render ();
		CollectionAssert.AreEqual (expected, rendered.ToArray ());
	}

	[TestMethod]
	public void ContentDescriptionGuid_MatchesSpecification ()
	{
		// 75B22633-668E-11CF-A6D9-00AA0062CE6C
		var expected = new byte[] {
			0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
			0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
		};

		var rendered = AsfGuids.ContentDescriptionObject.Render ();
		CollectionAssert.AreEqual (expected, rendered.ToArray ());
	}

	[TestMethod]
	public void ExtendedContentDescriptionGuid_MatchesSpecification ()
	{
		// D2D0A440-E307-11D2-97F0-00A0C95EA850
		var expected = new byte[] {
			0x40, 0xA4, 0xD0, 0xD2, 0x07, 0xE3, 0xD2, 0x11,
			0x97, 0xF0, 0x00, 0xA0, 0xC9, 0x5E, 0xA8, 0x50
		};

		var rendered = AsfGuids.ExtendedContentDescriptionObject.Render ();
		CollectionAssert.AreEqual (expected, rendered.ToArray ());
	}

	[TestMethod]
	public void AudioMediaTypeGuid_MatchesSpecification ()
	{
		// F8699E40-5B4D-11CF-A8FD-00805F5C442B
		var expected = new byte[] {
			0x40, 0x9E, 0x69, 0xF8, 0x4D, 0x5B, 0xCF, 0x11,
			0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B
		};

		var rendered = AsfGuids.AudioMediaType.Render ();
		CollectionAssert.AreEqual (expected, rendered.ToArray ());
	}

	[TestMethod]
	public void AllGuids_AreUnique ()
	{
		var guids = new[] {
			AsfGuids.HeaderObject,
			AsfGuids.DataObject,
			AsfGuids.SimpleIndexObject,
			AsfGuids.FilePropertiesObject,
			AsfGuids.StreamPropertiesObject,
			AsfGuids.ContentDescriptionObject,
			AsfGuids.ExtendedContentDescriptionObject,
			AsfGuids.HeaderExtensionObject,
			AsfGuids.CodecListObject,
			AsfGuids.StreamBitratePropertiesObject,
			AsfGuids.PaddingObject,
			AsfGuids.MetadataObject,
			AsfGuids.MetadataLibraryObject,
			AsfGuids.LanguageListObject,
			AsfGuids.ExtendedStreamPropertiesObject,
			AsfGuids.AudioMediaType,
			AsfGuids.VideoMediaType,
			AsfGuids.NoErrorCorrection,
			AsfGuids.AudioSpread
		};

		var distinctCount = guids.Distinct ().Count ();
		Assert.AreEqual (guids.Length, distinctCount, "All GUIDs should be unique");
	}

	[TestMethod]
	public void HeaderAndContentDescription_AreDifferent ()
	{
		Assert.AreNotEqual (AsfGuids.HeaderObject, AsfGuids.ContentDescriptionObject);
	}
}
