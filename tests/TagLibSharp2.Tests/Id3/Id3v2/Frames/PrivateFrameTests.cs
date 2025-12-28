// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class PrivateFrameTests
{
	[TestMethod]
	public void Constructor_SetsProperties ()
	{
		var data = new BinaryData ([1, 2, 3, 4, 5]);
		var frame = new PrivateFrame ("com.example.app", data);

		Assert.AreEqual ("com.example.app", frame.OwnerId);
		Assert.AreEqual (5, frame.Data.Length);
	}

	[TestMethod]
	public void FrameId_ReturnsPRIV ()
	{
		Assert.AreEqual ("PRIV", PrivateFrame.FrameId);
	}

	[TestMethod]
	public void Read_SimpleFrame_ParsesCorrectly ()
	{
		var data = BuildPrivFrame ("com.example.app", [1, 2, 3, 4, 5]);

		var result = PrivateFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("com.example.app", result.Frame!.OwnerId);
		Assert.AreEqual (5, result.Frame.Data.Length);
	}

	[TestMethod]
	public void Read_EmptyData_ParsesCorrectly ()
	{
		var data = BuildPrivFrame ("empty.owner", []);

		var result = PrivateFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("empty.owner", result.Frame!.OwnerId);
		Assert.AreEqual (0, result.Frame.Data.Length);
	}

	[TestMethod]
	public void Read_EmptyInput_ReturnsFailure ()
	{
		var result = PrivateFrame.Read ([], Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_NoNullTerminator_ReturnsFailure ()
	{
		// Just owner ID without null terminator
		var data = System.Text.Encoding.Latin1.GetBytes ("com.example");

		var result = PrivateFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void RenderContent_RoundTrips ()
	{
		var originalData = new BinaryData ([0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE]);
		var original = new PrivateFrame ("org.musicbrainz.fingerprint", originalData);

		var rendered = original.RenderContent ();
		var parsed = PrivateFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("org.musicbrainz.fingerprint", parsed.Frame!.OwnerId);
		Assert.AreEqual (6, parsed.Frame.Data.Length);
		Assert.AreEqual (0xDE, parsed.Frame.Data.Span[0]);
		Assert.AreEqual (0xFE, parsed.Frame.Data.Span[5]);
	}

	[TestMethod]
	public void RenderContent_LongOwnerId_RoundTrips ()
	{
		var longOwnerId = "com.very.long.owner.identifier.for.testing.purposes";
		var data = new BinaryData ([42]);
		var original = new PrivateFrame (longOwnerId, data);

		var rendered = original.RenderContent ();
		var parsed = PrivateFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (longOwnerId, parsed.Frame!.OwnerId);
	}

	static byte[] BuildPrivFrame (string ownerId, byte[] privateData)
	{
		using var builder = new BinaryDataBuilder ();

		// Owner identifier (null-terminated Latin-1)
		builder.Add (System.Text.Encoding.Latin1.GetBytes (ownerId));
		builder.Add ((byte)0x00);

		// Private data
		builder.Add (privateData);

		return builder.ToBinaryData ().ToArray ();
	}
}
