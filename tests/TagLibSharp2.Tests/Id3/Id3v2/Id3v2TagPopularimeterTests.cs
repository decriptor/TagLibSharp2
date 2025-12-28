// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class Id3v2TagPopularimeterTests
{
	[TestMethod]
	public void Rating_Get_ReturnsNullWhenNoPopmFrame ()
	{
		var tag = CreateEmptyTag ();

		Assert.IsNull (tag.Rating);
		Assert.IsNull (tag.PlayCount);
	}

	[TestMethod]
	public void Rating_Set_CreatesPopmFrame ()
	{
		var tag = CreateEmptyTag ();

		tag.Rating = 128;

		Assert.AreEqual ((byte)128, tag.Rating);
		Assert.HasCount (1, tag.PopularimeterFrames);
	}

	[TestMethod]
	public void Rating_SetNull_ClearsPopmFrames ()
	{
		var tag = CreateEmptyTag ();
		tag.Rating = 200;

		tag.Rating = null;

		Assert.IsNull (tag.Rating);
		Assert.IsEmpty (tag.PopularimeterFrames);
	}

	[TestMethod]
	public void PlayCount_Set_CreatesPopmFrame ()
	{
		var tag = CreateEmptyTag ();

		tag.PlayCount = 100;

		Assert.AreEqual (100UL, tag.PlayCount);
		Assert.HasCount (1, tag.PopularimeterFrames);
	}

	[TestMethod]
	public void SetPopularimeter_AddsNewFrame ()
	{
		var tag = CreateEmptyTag ();

		tag.SetPopularimeter ("wmp@microsoft.com", 196, 42);

		var frame = tag.GetPopularimeter ("wmp@microsoft.com");
		Assert.IsNotNull (frame);
		Assert.AreEqual ((byte)196, frame.Rating);
		Assert.AreEqual (42UL, frame.PlayCount);
	}

	[TestMethod]
	public void SetPopularimeter_UpdatesExistingFrame ()
	{
		var tag = CreateEmptyTag ();
		tag.SetPopularimeter ("test@test.com", 100, 10);

		tag.SetPopularimeter ("test@test.com", 200, 50);

		Assert.HasCount (1, tag.PopularimeterFrames);
		var frame = tag.GetPopularimeter ("test@test.com");
		Assert.AreEqual ((byte)200, frame!.Rating);
		Assert.AreEqual (50UL, frame.PlayCount);
	}

	[TestMethod]
	public void GetPopularimeter_CaseInsensitive ()
	{
		var tag = CreateEmptyTag ();
		tag.SetPopularimeter ("User@Example.COM", 255, 0);

		var frame = tag.GetPopularimeter ("user@example.com");

		Assert.IsNotNull (frame);
		Assert.AreEqual ((byte)255, frame.Rating);
	}

	[TestMethod]
	public void Read_ParsesPopmFrame ()
	{
		var tagData = BuildTagWithPopmFrame ("player@media.com", 128, 999);

		var result = Id3v2Tag.Read (tagData);

		Assert.IsTrue (result.IsSuccess);
		Assert.HasCount (1, result.Tag!.PopularimeterFrames);
		Assert.AreEqual ("player@media.com", result.Tag.PopularimeterFrames[0].Email);
		Assert.AreEqual ((byte)128, result.Tag.PopularimeterFrames[0].Rating);
		Assert.AreEqual (999UL, result.Tag.PopularimeterFrames[0].PlayCount);
	}

	[TestMethod]
	public void Render_RoundTrips ()
	{
		var tag = CreateEmptyTag ();
		tag.SetPopularimeter ("test@example.com", 220, 12345);
		tag.Title = "Test Song";

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("Test Song", parsed.Tag!.Title);
		Assert.HasCount (1, parsed.Tag.PopularimeterFrames);
		Assert.AreEqual ("test@example.com", parsed.Tag.PopularimeterFrames[0].Email);
		Assert.AreEqual ((byte)220, parsed.Tag.PopularimeterFrames[0].Rating);
		Assert.AreEqual (12345UL, parsed.Tag.PopularimeterFrames[0].PlayCount);
	}

	[TestMethod]
	public void Render_MultiplePopmFrames_RoundTrips ()
	{
		var tag = CreateEmptyTag ();
		tag.SetPopularimeter ("user1@example.com", 100, 10);
		tag.SetPopularimeter ("user2@example.com", 200, 20);

		var rendered = tag.Render ();
		var parsed = Id3v2Tag.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.HasCount (2, parsed.Tag!.PopularimeterFrames);

		var frame1 = parsed.Tag.GetPopularimeter ("user1@example.com");
		Assert.IsNotNull (frame1);
		Assert.AreEqual ((byte)100, frame1.Rating);

		var frame2 = parsed.Tag.GetPopularimeter ("user2@example.com");
		Assert.IsNotNull (frame2);
		Assert.AreEqual ((byte)200, frame2.Rating);
	}

	static Id3v2Tag CreateEmptyTag ()
	{
		// Create a minimal valid tag and read it to get a mutable tag
		var header = new Id3v2Header (4, 0, Id3v2HeaderFlags.None, 0);
		using var builder = new BinaryDataBuilder ();
		builder.Add (header.Render ());
		var result = Id3v2Tag.Read (builder.ToBinaryData ().Span);
		return result.Tag!;
	}

	static byte[] BuildTagWithPopmFrame (string email, byte rating, ulong playCount)
	{
		using var builder = new BinaryDataBuilder ();

		// Build POPM frame content
		var emailBytes = System.Text.Encoding.Latin1.GetBytes (email);
		using var contentBuilder = new BinaryDataBuilder ();
		contentBuilder.Add (emailBytes);
		contentBuilder.Add ((byte)0x00);
		contentBuilder.Add (rating);

		if (playCount > 0) {
			int byteCount = playCount <= 0xFF ? 1 :
							playCount <= 0xFFFF ? 2 :
							playCount <= 0xFFFFFF ? 3 : 4;
			for (int i = byteCount - 1; i >= 0; i--) {
				contentBuilder.Add ((byte)((playCount >> (i * 8)) & 0xFF));
			}
		}

		var frameContent = contentBuilder.ToBinaryData ();

		// Frame header (10 bytes): ID(4) + syncsafe size(4) + flags(2)
		builder.Add (System.Text.Encoding.ASCII.GetBytes ("POPM"));
		builder.AddSyncSafeUInt32 ((uint)frameContent.Length);
		builder.Add ((byte)0x00); // Flags
		builder.Add ((byte)0x00);
		builder.Add (frameContent);

		var framesData = builder.ToBinaryData ();

		// Build complete tag
		using var tagBuilder = new BinaryDataBuilder ();
		var header = new Id3v2Header (4, 0, Id3v2HeaderFlags.None, (uint)framesData.Length);
		tagBuilder.Add (header.Render ());
		tagBuilder.Add (framesData);

		return tagBuilder.ToBinaryData ().ToArray ();
	}
}
