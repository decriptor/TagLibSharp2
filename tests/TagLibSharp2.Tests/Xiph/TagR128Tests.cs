// Copyright (c) 2025 Stephen Shaw and contributors
// Tests for R128 loudness normalization support

using TagLibSharp2.Core;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("R128")]
public class TagR128Tests
{
	[TestMethod]
	public void R128TrackGainDb_GetPositiveValue_ConvertsCorrectly ()
	{
		// Arrange - Q7.8 value 256 = +1 dB
		var tag = new VorbisComment ();
		tag.R128TrackGain = "256";

		// Act
		var dB = tag.R128TrackGainDb;

		// Assert
		Assert.AreEqual (1.0, dB);
	}

	[TestMethod]
	public void R128TrackGainDb_GetNegativeValue_ConvertsCorrectly ()
	{
		// Arrange - Q7.8 value -512 = -2 dB
		var tag = new VorbisComment ();
		tag.R128TrackGain = "-512";

		// Act
		var dB = tag.R128TrackGainDb;

		// Assert
		Assert.AreEqual (-2.0, dB);
	}

	[TestMethod]
	public void R128TrackGainDb_SetPositiveValue_ConvertsToQ78 ()
	{
		// Arrange
		var tag = new VorbisComment ();

		// Act
		tag.R128TrackGainDb = 1.5;

		// Assert - 1.5 * 256 = 384
		Assert.AreEqual ("384", tag.R128TrackGain);
	}

	[TestMethod]
	public void R128TrackGainDb_SetNegativeValue_ConvertsToQ78 ()
	{
		// Arrange
		var tag = new VorbisComment ();

		// Act
		tag.R128TrackGainDb = -3.0;

		// Assert - -3.0 * 256 = -768
		Assert.AreEqual ("-768", tag.R128TrackGain);
	}

	[TestMethod]
	public void R128TrackGainDb_SetNull_ClearsValue ()
	{
		// Arrange
		var tag = new VorbisComment ();
		tag.R128TrackGain = "256";

		// Act
		tag.R128TrackGainDb = null;

		// Assert
		Assert.IsNull (tag.R128TrackGain);
	}

	[TestMethod]
	public void R128TrackGainDb_GetNullValue_ReturnsNull ()
	{
		// Arrange
		var tag = new VorbisComment ();

		// Act & Assert
		Assert.IsNull (tag.R128TrackGainDb);
	}

	[TestMethod]
	public void R128TrackGainDb_GetInvalidValue_ReturnsNull ()
	{
		// Arrange - non-numeric value
		var tag = new VorbisComment ();
		tag.R128TrackGain = "not a number";

		// Act & Assert
		Assert.IsNull (tag.R128TrackGainDb);
	}

	[TestMethod]
	public void R128AlbumGainDb_GetValue_ConvertsCorrectly ()
	{
		// Arrange
		var tag = new VorbisComment ();
		tag.R128AlbumGain = "-1024"; // -4 dB

		// Act
		var dB = tag.R128AlbumGainDb;

		// Assert
		Assert.AreEqual (-4.0, dB);
	}

	[TestMethod]
	public void R128AlbumGainDb_SetValue_ConvertsToQ78 ()
	{
		// Arrange
		var tag = new VorbisComment ();

		// Act
		tag.R128AlbumGainDb = 2.5;

		// Assert - 2.5 * 256 = 640
		Assert.AreEqual ("640", tag.R128AlbumGain);
	}

	[TestMethod]
	public void R128GainDb_ClampToMaxQ78_ClampsCorrectly ()
	{
		// Arrange - value larger than short.MaxValue / 256
		var tag = new VorbisComment ();

		// Act - set a huge value that would exceed short.MaxValue
		tag.R128TrackGainDb = 200.0; // 200 * 256 = 51200 > 32767

		// Assert - should be clamped to 32767
		Assert.AreEqual ("32767", tag.R128TrackGain);
	}

	[TestMethod]
	public void R128GainDb_ClampToMinQ78_ClampsCorrectly ()
	{
		// Arrange - value smaller than short.MinValue / 256
		var tag = new VorbisComment ();

		// Act - set a huge negative value
		tag.R128TrackGainDb = -200.0; // -200 * 256 = -51200 < -32768

		// Assert - should be clamped to -32768
		Assert.AreEqual ("-32768", tag.R128TrackGain);
	}

	[TestMethod]
	public void R128TrackGainDb_RoundTrip_PreservesValueWithinPrecision ()
	{
		// Arrange
		var tag = new VorbisComment ();

		// Act - set and get
		tag.R128TrackGainDb = -6.5;
		var result = tag.R128TrackGainDb;

		// Assert - verify round-trip
		Assert.AreEqual (-6.5, result);
	}

	[TestMethod]
	public void R128GainDb_FractionalValue_RoundsCorrectly ()
	{
		// Arrange
		var tag = new VorbisComment ();

		// Act - set value that rounds
		tag.R128TrackGainDb = 1.003; // 1.003 * 256 = 256.768 -> rounds to 257

		// Assert
		Assert.AreEqual ("257", tag.R128TrackGain);

		// Get back
		var result = tag.R128TrackGainDb;
		Assert.AreEqual (257.0 / 256.0, result); // Slightly different due to rounding
	}
}
