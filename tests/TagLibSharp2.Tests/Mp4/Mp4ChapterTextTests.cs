// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// The text checks that decide whether a chapter sample is a title (Apple TN2174: UTF-8, or UTF-16 with a BOM).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
public class Mp4ChapterTextTests
{
	[TestMethod]
	public void Utf8Scan_MatchesTheUnicodeTable ()
	{
		// Unicode §3.9 Table 3-7 boundaries. The manual scan serves netstandard; net8+ uses Utf8.IsValid.
		byte[][] valid = [
			[], [0x41], [0xC2, 0x80], [0xDF, 0xBF], [0xE0, 0xA0, 0x80], [0xED, 0x9F, 0xBF], [0xEE, 0x80, 0x80],
			[0xF0, 0x90, 0x80, 0x80], [0xF4, 0x8F, 0xBF, 0xBF], [0xEF, 0xBB, 0xBF, 0x41],
		];
		byte[][] invalid = [
			[0x80], [0xC0, 0x80], [0xC1, 0xBF], [0xC2], [0xE0, 0x9F, 0x80], [0xED, 0xA0, 0x80], [0xE1, 0x80],
			[0xF0, 0x8F, 0x80, 0x80], [0xF4, 0x90, 0x80, 0x80], [0xF5, 0x80, 0x80, 0x80], [0xFF], [0xC3, 0x28],
		];

		foreach (var bytes in valid) {
			Assert.IsTrue (Mp4ChapterCodec.ScanUtf8 (bytes), Convert.ToHexString (bytes));
			Assert.IsTrue (Mp4ChapterCodec.IsValidUtf8 (bytes), Convert.ToHexString (bytes));
		}

		foreach (var bytes in invalid) {
			Assert.IsFalse (Mp4ChapterCodec.ScanUtf8 (bytes), Convert.ToHexString (bytes));
			Assert.IsFalse (Mp4ChapterCodec.IsValidUtf8 (bytes), Convert.ToHexString (bytes));
		}
	}

	[TestMethod]
	public void Utf16Check_RejectsOddLengthsAndUnpairedSurrogates ()
	{
		Assert.IsTrue (Mp4ChapterCodec.IsValidUtf16 ([0x00, 0x41, 0xD8, 0x3D, 0xDE, 0x00], bigEndian: true));
		Assert.IsTrue (Mp4ChapterCodec.IsValidUtf16 ([0x41, 0x00, 0x3D, 0xD8, 0x00, 0xDE], bigEndian: false));
		Assert.IsFalse (Mp4ChapterCodec.IsValidUtf16 ([0x00, 0x41, 0x00], bigEndian: true), "odd length");
		Assert.IsFalse (Mp4ChapterCodec.IsValidUtf16 ([0xD8, 0x3D], bigEndian: true), "high surrogate at the end");
		Assert.IsFalse (Mp4ChapterCodec.IsValidUtf16 ([0xD8, 0x3D, 0x00, 0x41], bigEndian: true), "high without low");
		Assert.IsFalse (Mp4ChapterCodec.IsValidUtf16 ([0xDE, 0x00], bigEndian: true), "lone low surrogate");
	}
}
