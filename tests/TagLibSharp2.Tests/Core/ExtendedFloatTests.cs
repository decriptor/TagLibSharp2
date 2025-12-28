// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Core")]
public class ExtendedFloatTests
{
	// 80-bit IEEE 754 extended precision format (big-endian):
	// - 1 bit sign
	// - 15 bits exponent (bias 16383)
	// - 64 bits mantissa (with explicit integer bit)

	// Known values from AIFF specification and reference implementations
	// These were verified against actual AIFF files created by audio software

	[TestMethod]
	public void ToDouble_44100Hz_ReturnsCorrectValue ()
	{
		// 44100 Hz - most common sample rate
		// 80-bit representation: 400EAC4400000000 0000 (exponent 15 + bias 16383 = 0x400E)
		byte[] bytes = [0x40, 0x0E, 0xAC, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (44100.0, result, 0.001);
	}

	[TestMethod]
	public void ToDouble_48000Hz_ReturnsCorrectValue ()
	{
		// 48000 Hz - professional audio standard
		// 80-bit representation: 400EBB8000000000 0000
		byte[] bytes = [0x40, 0x0E, 0xBB, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (48000.0, result, 0.001);
	}

	[TestMethod]
	public void ToDouble_96000Hz_ReturnsCorrectValue ()
	{
		// 96000 Hz - high-res audio
		// 80-bit representation: 400FBB8000000000 0000
		byte[] bytes = [0x40, 0x0F, 0xBB, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (96000.0, result, 0.001);
	}

	[TestMethod]
	public void ToDouble_192000Hz_ReturnsCorrectValue ()
	{
		// 192000 Hz - ultra high-res
		// 80-bit representation: 4010BB8000000000 0000
		byte[] bytes = [0x40, 0x10, 0xBB, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (192000.0, result, 0.001);
	}

	[TestMethod]
	public void ToDouble_22050Hz_ReturnsCorrectValue ()
	{
		// 22050 Hz - half of CD quality
		// 80-bit representation: 400DAC4400000000 0000 (exponent 14 + bias 16383 = 0x400D)
		byte[] bytes = [0x40, 0x0D, 0xAC, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (22050.0, result, 0.001);
	}

	[TestMethod]
	public void ToDouble_Zero_ReturnsZero ()
	{
		byte[] bytes = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (0.0, result);
	}

	[TestMethod]
	public void ToDouble_One_ReturnsOne ()
	{
		// 1.0 in 80-bit: exponent = 16383 (0x3FFF), mantissa = 0x8000000000000000
		byte[] bytes = [0x3F, 0xFF, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (1.0, result, 0.0001);
	}

	[TestMethod]
	public void ToDouble_NegativeOne_ReturnsNegativeOne ()
	{
		// -1.0 in 80-bit: sign=1, exponent = 16383 (0x3FFF), mantissa = 0x8000000000000000
		byte[] bytes = [0xBF, 0xFF, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (-1.0, result, 0.0001);
	}

	[TestMethod]
	public void ToDouble_FromSpan_WorksCorrectly ()
	{
		byte[] bytes = [0x40, 0x0E, 0xAC, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var result = ExtendedFloat.ToDouble (bytes.AsSpan ());

		Assert.AreEqual (44100.0, result, 0.001);
	}

	[TestMethod]
	public void ToDouble_FromBinaryData_WorksCorrectly ()
	{
		var data = new BinaryData ([0x40, 0x0E, 0xAC, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

		var result = ExtendedFloat.ToDouble (data);

		Assert.AreEqual (44100.0, result, 0.001);
	}

	[TestMethod]
	public void FromDouble_44100Hz_ProducesCorrectBytes ()
	{
		var bytes = ExtendedFloat.FromDouble (44100.0);

		Assert.AreEqual (10, bytes.Length);
		// Verify first 4 bytes which encode the key information
		// Exponent: 15 + 16383 = 16398 = 0x400E
		Assert.AreEqual ((byte)0x40, bytes[0]);
		Assert.AreEqual ((byte)0x0E, bytes[1]);
		Assert.AreEqual ((byte)0xAC, bytes[2]);
		Assert.AreEqual ((byte)0x44, bytes[3]);
	}

	[TestMethod]
	public void FromDouble_Zero_ProducesZeroBytes ()
	{
		var bytes = ExtendedFloat.FromDouble (0.0);

		Assert.AreEqual (10, bytes.Length);
		foreach (var b in bytes)
			Assert.AreEqual (0, b);
	}

	[TestMethod]
	public void FromDouble_RoundTrip_PreservesValue ()
	{
		var values = new[] { 44100.0, 48000.0, 96000.0, 192000.0, 22050.0, 8000.0, 1.0, 0.5 };

		foreach (var original in values) {
			var bytes = ExtendedFloat.FromDouble (original);
			var roundTripped = ExtendedFloat.ToDouble (bytes);

			Assert.AreEqual (original, roundTripped, original * 0.0001,
				$"Round-trip failed for {original}");
		}
	}

	[TestMethod]
	public void FromDouble_NegativeValues_RoundTrip ()
	{
		var original = -44100.0;
		var bytes = ExtendedFloat.FromDouble (original);
		var roundTripped = ExtendedFloat.ToDouble (bytes);

		Assert.AreEqual (original, roundTripped, 0.001);
	}

	[TestMethod]
	public void ToDouble_TooShort_ThrowsArgumentException ()
	{
		byte[] bytes = [0x40, 0x0D, 0xAC]; // Only 3 bytes

		Assert.ThrowsExactly<ArgumentException> (() => ExtendedFloat.ToDouble (bytes));
	}

	[TestMethod]
	public void ToDouble_AtOffset_WorksCorrectly ()
	{
		// Prefix data + 80-bit float at offset 5
		byte[] data = [
			0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 5 bytes prefix
			0x40, 0x0E, 0xAC, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
		];

		var result = ExtendedFloat.ToDouble (data.AsSpan (5));

		Assert.AreEqual (44100.0, result, 0.001);
	}
}
