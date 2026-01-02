// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfFilePropertiesTests
{
	// ═══════════════════════════════════════════════════════════════
	// Parsing Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Parse_ExtractsPlayDuration ()
	{
		// 3 seconds = 30_000_000 100-nanosecond units
		var data = CreateFilePropertiesData (durationNs: 30_000_000, prerollMs: 0);

		var result = AsfFileProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (30_000_000UL, result.Value.PlayDurationNs);
	}

	[TestMethod]
	public void Parse_ExtractsPreroll ()
	{
		var data = CreateFilePropertiesData (durationNs: 30_000_000, prerollMs: 500);

		var result = AsfFileProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (500UL, result.Value.PrerollMs);
	}

	[TestMethod]
	public void Parse_ExtractsMaxBitrate ()
	{
		var data = CreateFilePropertiesData (bitrate: 256000);

		var result = AsfFileProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (256000u, result.Value.MaxBitrate);
	}

	[TestMethod]
	public void Parse_ExtractsFileSize ()
	{
		var data = CreateFilePropertiesData (fileSize: 1024 * 1024);

		var result = AsfFileProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1024UL * 1024, result.Value.FileSize);
	}

	[TestMethod]
	public void Duration_CalculatesCorrectly ()
	{
		// Duration = (PlayDuration - Preroll) / 10_000_000
		// PlayDuration = 35 seconds in 100ns units = 350_000_000
		// Preroll = 5 seconds in ms = 5000
		// Preroll in 100ns units = 50_000_000
		// Duration = (350_000_000 - 50_000_000) / 10_000_000 = 30 seconds
		var data = CreateFilePropertiesData (durationNs: 350_000_000, prerollMs: 5000);

		var result = AsfFileProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.FromSeconds (30), result.Value.Duration);
	}

	[TestMethod]
	public void Duration_ZeroPreroll_UsesFullDuration ()
	{
		// 10 seconds = 100_000_000 100-nanosecond units
		var data = CreateFilePropertiesData (durationNs: 100_000_000, prerollMs: 0);

		var result = AsfFileProperties.Parse (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (TimeSpan.FromSeconds (10), result.Value.Duration);
	}

	[TestMethod]
	public void Parse_TruncatedInput_ReturnsFailure ()
	{
		var data = new byte[10]; // Way too short

		var result = AsfFileProperties.Parse (data);

		Assert.IsFalse (result.IsSuccess);
	}

	// ═══════════════════════════════════════════════════════════════
	// Helper Methods
	// ═══════════════════════════════════════════════════════════════

	static byte[] CreateFilePropertiesData (
		ulong durationNs = 30_000_000,
		ulong prerollMs = 0,
		uint bitrate = 128000,
		ulong fileSize = 0)
	{
		// File Properties Object content is 80 bytes per spec
		var data = new byte[80];
		var offset = 0;

		// File ID (GUID) - 16 bytes
		offset += 16;

		// File size - 8 bytes
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (offset), fileSize);
		offset += 8;

		// Creation date - 8 bytes
		offset += 8;

		// Data packets count - 8 bytes
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (offset), 1);
		offset += 8;

		// Play duration (100-nanosecond units) - 8 bytes
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (offset), durationNs);
		offset += 8;

		// Send duration - 8 bytes
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (offset), durationNs);
		offset += 8;

		// Preroll (milliseconds) - 8 bytes
		BinaryPrimitives.WriteUInt64LittleEndian (data.AsSpan (offset), prerollMs);
		offset += 8;

		// Flags - 4 bytes
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (offset), 0x02);
		offset += 4;

		// Minimum data packet size - 4 bytes
		offset += 4;

		// Maximum data packet size - 4 bytes
		offset += 4;

		// Maximum bitrate - 4 bytes
		BinaryPrimitives.WriteUInt32LittleEndian (data.AsSpan (offset), bitrate);

		return data;
	}
}
