// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Linq;

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

/// <summary>
/// Tests for <see cref="UniqueFileIdFrame"/> (UFID frame).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
public class UniqueFileIdFrameTests
{

	[TestMethod]
	public void Constructor_WithBinaryData_SetsProperties ()
	{
		var identifier = new BinaryData (new byte[] { 0x01, 0x02, 0x03, 0x04 });
		var frame = new UniqueFileIdFrame ("http://example.com", identifier);

		Assert.AreEqual ("http://example.com", frame.Owner);
		Assert.AreEqual (4, frame.Identifier.Length);
	}

	[TestMethod]
	public void Constructor_WithByteArray_SetsProperties ()
	{
		var identifier = new byte[] { 0x01, 0x02, 0x03, 0x04 };
		var frame = new UniqueFileIdFrame ("http://example.com", identifier);

		Assert.AreEqual ("http://example.com", frame.Owner);
		Assert.AreEqual (4, frame.Identifier.Length);
	}

	[TestMethod]
	public void Constructor_WithStringIdentifier_EncodesAsAscii ()
	{
		var frame = new UniqueFileIdFrame ("http://musicbrainz.org", "12345678-1234-1234-1234-123456789012");

		Assert.AreEqual ("http://musicbrainz.org", frame.Owner);
		Assert.AreEqual ("12345678-1234-1234-1234-123456789012", frame.IdentifierString);
	}

	[TestMethod]
	public void Constructor_NullOwner_DefaultsToEmpty ()
	{
		var frame = new UniqueFileIdFrame (null!, new byte[] { 0x01 });

		Assert.AreEqual ("", frame.Owner);
	}

	[TestMethod]
	public void Constructor_NullByteArrayIdentifier_DefaultsToEmpty ()
	{
		var frame = new UniqueFileIdFrame ("owner", (byte[])null!);

		Assert.IsTrue (frame.Identifier.IsEmpty);
	}

	[TestMethod]
	public void FrameId_IsUFID ()
	{
		// Verify the constant value matches ID3v2 spec
		var frameId = UniqueFileIdFrame.FrameId;
		Assert.AreEqual (4, frameId.Length);
		Assert.IsTrue (frameId.All (c => char.IsUpper (c)));
	}

	[TestMethod]
	public void MusicBrainzOwner_IsValidUrl ()
	{
		// Verify the constant value is a valid URL for MusicBrainz
		var owner = UniqueFileIdFrame.MusicBrainzOwner;
		Assert.IsTrue (owner.StartsWith ("http://", StringComparison.Ordinal));
		Assert.IsTrue (owner.Contains ("musicbrainz", StringComparison.OrdinalIgnoreCase));
	}



	[TestMethod]
	public void IdentifierString_AsciiBytes_ReturnsString ()
	{
		var frame = new UniqueFileIdFrame ("owner", System.Text.Encoding.ASCII.GetBytes ("ABC123"));

		Assert.AreEqual ("ABC123", frame.IdentifierString);
	}

	[TestMethod]
	public void IdentifierString_NonAsciiBytes_ReturnsNull ()
	{
		var frame = new UniqueFileIdFrame ("owner", new byte[] { 0x00, 0x01, 0x02 });

		Assert.IsNull (frame.IdentifierString);
	}

	[TestMethod]
	public void IdentifierString_EmptyIdentifier_ReturnsNull ()
	{
		var frame = new UniqueFileIdFrame ("owner", Array.Empty<byte> ());

		Assert.IsNull (frame.IdentifierString);
	}

	[TestMethod]
	public void IdentifierString_NullByteArrayIdentifier_ReturnsNull ()
	{
		var frame = new UniqueFileIdFrame ("owner", (byte[])null!);

		Assert.IsNull (frame.IdentifierString);
	}



	[TestMethod]
	public void Read_ValidFrame_ParsesCorrectly ()
	{
		// Build UFID frame: owner (null-terminated) + identifier
		var owner = "http://musicbrainz.org";
		var identifier = "12345678-1234-1234-1234-123456789012";
		var data = BuildFrame (owner, System.Text.Encoding.ASCII.GetBytes (identifier));

		var result = UniqueFileIdFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (owner, result.Frame!.Owner);
		Assert.AreEqual (identifier, result.Frame.IdentifierString);
	}

	[TestMethod]
	public void Read_BinaryIdentifier_ParsesCorrectly ()
	{
		var owner = "http://example.com";
		var identifier = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF };
		var data = BuildFrame (owner, identifier);

		var result = UniqueFileIdFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (owner, result.Frame!.Owner);
		Assert.AreEqual (identifier.Length, result.Frame.Identifier.Length);
		Assert.IsTrue (result.Frame.Identifier.Span.SequenceEqual (identifier));
	}

	[TestMethod]
	public void Read_EmptyOwner_ParsesCorrectly ()
	{
		// Owner is empty (just null terminator) + identifier
		var identifier = new byte[] { 0xAB, 0xCD };
		var data = new byte[1 + identifier.Length];
		data[0] = 0; // Empty owner null terminator
		Array.Copy (identifier, 0, data, 1, identifier.Length);

		var result = UniqueFileIdFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("", result.Frame!.Owner);
		Assert.AreEqual (identifier.Length, result.Frame.Identifier.Length);
	}

	[TestMethod]
	public void Read_TooShort_ReturnsFailure ()
	{
		var data = new byte[] { 0x00 }; // Just null terminator, no identifier

		var result = UniqueFileIdFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_NoNullTerminator_ReturnsFailure ()
	{
		var data = new byte[] { (byte)'a', (byte)'b', (byte)'c' }; // No null terminator

		var result = UniqueFileIdFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}



	[TestMethod]
	public void RenderContent_MusicBrainzId_RoundTrips ()
	{
		var original = new UniqueFileIdFrame (
			UniqueFileIdFrame.MusicBrainzOwner,
			"550e8400-e29b-41d4-a716-446655440000");

		var rendered = original.RenderContent ();
		var result = UniqueFileIdFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Owner, result.Frame!.Owner);
		Assert.AreEqual (original.IdentifierString, result.Frame.IdentifierString);
	}

	[TestMethod]
	public void RenderContent_BinaryIdentifier_RoundTrips ()
	{
		var identifier = new byte[] { 0x00, 0x7F, 0x80, 0xFF };
		var original = new UniqueFileIdFrame ("http://example.com/db", identifier);

		var rendered = original.RenderContent ();
		var result = UniqueFileIdFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Owner, result.Frame!.Owner);
		Assert.IsTrue (result.Frame.Identifier.Span.SequenceEqual (identifier));
	}

	[TestMethod]
	public void RenderContent_EmptyIdentifier_RoundTrips ()
	{
		var original = new UniqueFileIdFrame ("http://test.com", Array.Empty<byte> ());

		var rendered = original.RenderContent ();
		var result = UniqueFileIdFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (original.Owner, result.Frame!.Owner);
		Assert.IsTrue (result.Frame.Identifier.IsEmpty);
	}



	[TestMethod]
	public void Id3v2Tag_MusicBrainzRecordingId_GetSet_Works ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		var recordingId = "550e8400-e29b-41d4-a716-446655440000";

		tag.MusicBrainzRecordingId = recordingId;

		Assert.AreEqual (recordingId, tag.MusicBrainzRecordingId);
		Assert.HasCount (1, tag.UniqueFileIdFrames);
		Assert.AreEqual (UniqueFileIdFrame.MusicBrainzOwner, tag.UniqueFileIdFrames[0].Owner);
	}

	[TestMethod]
	public void Id3v2Tag_MusicBrainzRecordingId_SetNull_ClearsFrame ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.MusicBrainzRecordingId = "550e8400-e29b-41d4-a716-446655440000";

		tag.MusicBrainzRecordingId = null;

		Assert.IsNull (tag.MusicBrainzRecordingId);
		Assert.IsEmpty (tag.UniqueFileIdFrames);
	}

	[TestMethod]
	public void Id3v2Tag_MusicBrainzRecordingId_RoundTrip_PreservesValue ()
	{
		var recordingId = "f47ac10b-58cc-4372-a567-0e02b2c3d479";
		var original = new Id3v2Tag (Id3v2Version.V24) {
			MusicBrainzRecordingId = recordingId
		};

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (recordingId, result.Tag!.MusicBrainzRecordingId);
	}

	[TestMethod]
	public void Id3v2Tag_AddUniqueFileId_AddsToCollection ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);

		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://db1.com", "id1"));
		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://db2.com", "id2"));

		Assert.HasCount (2, tag.UniqueFileIdFrames);
	}

	[TestMethod]
	public void Id3v2Tag_GetUniqueFileId_FindsByOwner ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://db1.com", "id1"));
		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://db2.com", "id2"));

		var frame = tag.GetUniqueFileId ("http://db2.com");

		Assert.IsNotNull (frame);
		Assert.AreEqual ("id2", frame.IdentifierString);
	}

	[TestMethod]
	public void Id3v2Tag_GetUniqueFileId_CaseInsensitive ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://MusicBrainz.org", "id1"));

		var frame = tag.GetUniqueFileId ("HTTP://MUSICBRAINZ.ORG");

		Assert.IsNotNull (frame);
	}

	[TestMethod]
	public void Id3v2Tag_RemoveUniqueFileIds_RemovesByOwner ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://db1.com", "id1"));
		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://db2.com", "id2"));

		tag.RemoveUniqueFileIds ("http://db1.com");

		Assert.HasCount (1, tag.UniqueFileIdFrames);
		Assert.AreEqual ("http://db2.com", tag.UniqueFileIdFrames[0].Owner);
	}

	[TestMethod]
	public void Id3v2Tag_RemoveUniqueFileIds_NullOwner_ClearsAll ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24);
		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://db1.com", "id1"));
		tag.AddUniqueFileId (new UniqueFileIdFrame ("http://db2.com", "id2"));

		tag.RemoveUniqueFileIds ();

		Assert.IsEmpty (tag.UniqueFileIdFrames);
	}

	[TestMethod]
	public void Id3v2Tag_Clear_ClearsUniqueFileIds ()
	{
		var tag = new Id3v2Tag (Id3v2Version.V24) {
			Title = "Test",
			MusicBrainzRecordingId = "test-id"
		};

		tag.Clear ();

		Assert.IsNull (tag.MusicBrainzRecordingId);
		Assert.IsEmpty (tag.UniqueFileIdFrames);
	}

	[TestMethod]
	public void Id3v2Tag_MultipleUfidFrames_RoundTrip ()
	{
		var original = new Id3v2Tag (Id3v2Version.V24);
		original.AddUniqueFileId (new UniqueFileIdFrame ("http://musicbrainz.org", "mb-id-123"));
		original.AddUniqueFileId (new UniqueFileIdFrame ("http://cddb.com", "cddb-id-456"));

		var rendered = original.Render ();
		var result = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (2, result.Tag!.UniqueFileIdFrames);
		Assert.IsNotNull (result.Tag.GetUniqueFileId ("http://musicbrainz.org"));
		Assert.IsNotNull (result.Tag.GetUniqueFileId ("http://cddb.com"));
	}



	static byte[] BuildFrame (string owner, byte[] identifier)
	{
		var ownerBytes = System.Text.Encoding.ASCII.GetBytes (owner);
		var result = new byte[ownerBytes.Length + 1 + identifier.Length];

		Array.Copy (ownerBytes, 0, result, 0, ownerBytes.Length);
		result[ownerBytes.Length] = 0; // Null terminator
		Array.Copy (identifier, 0, result, ownerBytes.Length + 1, identifier.Length);

		return result;
	}

}
