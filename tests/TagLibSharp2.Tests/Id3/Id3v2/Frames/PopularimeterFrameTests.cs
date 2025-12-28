// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
public class PopularimeterFrameTests
{
	[TestMethod]
	public void Constructor_DefaultValues_HasCorrectDefaults ()
	{
		var frame = new PopularimeterFrame ("user@example.com");

		Assert.AreEqual ("user@example.com", frame.Email);
		Assert.AreEqual ((byte)0, frame.Rating);
		Assert.AreEqual (0UL, frame.PlayCount);
	}

	[TestMethod]
	public void Constructor_AllValues_StoresCorrectly ()
	{
		var frame = new PopularimeterFrame ("test@test.com", 196, 42);

		Assert.AreEqual ("test@test.com", frame.Email);
		Assert.AreEqual ((byte)196, frame.Rating);
		Assert.AreEqual (42UL, frame.PlayCount);
	}

	[TestMethod]
	public void FrameId_ReturnsPOPM ()
	{
		Assert.AreEqual ("POPM", PopularimeterFrame.FrameId);
	}

	[TestMethod]
	public void Read_SimpleFrame_ParsesCorrectly ()
	{
		// POPM: email(null-term) + rating(1) + counter(variable)
		var data = BuildPopmFrame ("user@example.com", 128, 100);

		var result = PopularimeterFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("user@example.com", result.Frame!.Email);
		Assert.AreEqual ((byte)128, result.Frame.Rating);
		Assert.AreEqual (100UL, result.Frame.PlayCount);
	}

	[TestMethod]
	public void Read_FiveStarRating_ParsesCorrectly ()
	{
		// 255 = 5 stars (max rating)
		var data = BuildPopmFrame ("foobar@example.com", 255, 9999);

		var result = PopularimeterFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ((byte)255, result.Frame!.Rating);
		Assert.AreEqual (9999UL, result.Frame.PlayCount);
	}

	[TestMethod]
	public void Read_ZeroRating_ParsesCorrectly ()
	{
		// 0 = unknown/not rated
		var data = BuildPopmFrame ("nobody@nobody.com", 0, 0);

		var result = PopularimeterFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ((byte)0, result.Frame!.Rating);
		Assert.AreEqual (0UL, result.Frame.PlayCount);
	}

	[TestMethod]
	public void Read_LargePlayCount_ParsesCorrectly ()
	{
		// Counter can be 4+ bytes - test large value
		var data = BuildPopmFrame ("test@test.com", 200, 0x01020304);

		var result = PopularimeterFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0x01020304UL, result.Frame!.PlayCount);
	}

	[TestMethod]
	public void Read_NoPlayCount_ParsesCorrectly ()
	{
		// Just email + rating, no counter bytes
		byte[] data = BuildPopmFrameNoCounter ("wmp@microsoft.com", 192);

		var result = PopularimeterFrame.Read (data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("wmp@microsoft.com", result.Frame!.Email);
		Assert.AreEqual ((byte)192, result.Frame.Rating);
		Assert.AreEqual (0UL, result.Frame.PlayCount);
	}

	[TestMethod]
	public void Read_EmptyData_ReturnsFailure ()
	{
		var result = PopularimeterFrame.Read ([], Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_OnlyEmail_ReturnsFailure ()
	{
		// Email without null terminator and rating
		byte[] data = System.Text.Encoding.Latin1.GetBytes ("user@test.com");

		var result = PopularimeterFrame.Read (data, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void RenderContent_RoundTrips ()
	{
		var original = new PopularimeterFrame ("media@player.com", 220, 12345);

		var rendered = original.RenderContent ();
		var parsed = PopularimeterFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("media@player.com", parsed.Frame!.Email);
		Assert.AreEqual ((byte)220, parsed.Frame.Rating);
		Assert.AreEqual (12345UL, parsed.Frame.PlayCount);
	}

	[TestMethod]
	public void RenderContent_ZeroPlayCount_DoesNotWriteCounter ()
	{
		var frame = new PopularimeterFrame ("test@test.com", 128, 0);

		var rendered = frame.RenderContent ();

		// Should be email + null + rating = 14 + 1 + 1 = 16 bytes
		Assert.AreEqual ("test@test.com".Length + 2, rendered.Length);
	}

	[TestMethod]
	public void RenderContent_LargePlayCount_RoundTrips ()
	{
		ulong largeCount = 0xFFFFFFFF; // Max 32-bit value
		var original = new PopularimeterFrame ("big@count.com", 255, largeCount);

		var rendered = original.RenderContent ();
		var parsed = PopularimeterFrame.Read (rendered.Span, Id3v2Version.V24);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (largeCount, parsed.Frame!.PlayCount);
	}

	[TestMethod]
	public void RatingToStars_ConvertsCorrectly ()
	{
		// According to common conventions:
		// 0 = unknown, 1-31 = 1 star, 32-95 = 2 stars, 96-159 = 3 stars, 160-223 = 4 stars, 224-255 = 5 stars
		Assert.AreEqual (0, PopularimeterFrame.RatingToStars (0));   // Unknown
		Assert.AreEqual (1, PopularimeterFrame.RatingToStars (1));   // 1 star
		Assert.AreEqual (1, PopularimeterFrame.RatingToStars (31));  // 1 star
		Assert.AreEqual (2, PopularimeterFrame.RatingToStars (64));  // 2 stars
		Assert.AreEqual (3, PopularimeterFrame.RatingToStars (128)); // 3 stars
		Assert.AreEqual (4, PopularimeterFrame.RatingToStars (196)); // 4 stars
		Assert.AreEqual (5, PopularimeterFrame.RatingToStars (255)); // 5 stars
	}

	[TestMethod]
	public void StarsToRating_ConvertsCorrectly ()
	{
		Assert.AreEqual ((byte)0, PopularimeterFrame.StarsToRating (0));     // Unknown
		Assert.AreEqual ((byte)1, PopularimeterFrame.StarsToRating (1));     // 1 star
		Assert.AreEqual ((byte)64, PopularimeterFrame.StarsToRating (2));    // 2 stars
		Assert.AreEqual ((byte)128, PopularimeterFrame.StarsToRating (3));   // 3 stars
		Assert.AreEqual ((byte)196, PopularimeterFrame.StarsToRating (4));   // 4 stars
		Assert.AreEqual ((byte)255, PopularimeterFrame.StarsToRating (5));   // 5 stars
	}

	static byte[] BuildPopmFrame (string email, byte rating, ulong playCount)
	{
		using var builder = new BinaryDataBuilder ();

		// Email (Latin-1, null-terminated)
		builder.Add (System.Text.Encoding.Latin1.GetBytes (email));
		builder.Add ((byte)0x00);

		// Rating (1 byte)
		builder.Add (rating);

		// Play counter (big-endian, variable length)
		if (playCount > 0) {
			// Determine minimum bytes needed
			int byteCount = playCount <= 0xFF ? 1 :
							playCount <= 0xFFFF ? 2 :
							playCount <= 0xFFFFFF ? 3 :
							playCount <= 0xFFFFFFFF ? 4 : 8;

			for (int i = byteCount - 1; i >= 0; i--) {
				builder.Add ((byte)((playCount >> (i * 8)) & 0xFF));
			}
		}

		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] BuildPopmFrameNoCounter (string email, byte rating)
	{
		using var builder = new BinaryDataBuilder ();
		builder.Add (System.Text.Encoding.Latin1.GetBytes (email));
		builder.Add ((byte)0x00);
		builder.Add (rating);
		return builder.ToBinaryData ().ToArray ();
	}
}
