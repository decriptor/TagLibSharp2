// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>
/// A still-image path save writes the same bytes as <see cref="StillImageFile.Render"/>.
/// The hashes are the bytes a path save wrote at 1322374a.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class ImagePathSaveTests
{
	[TestMethod]
	[DataRow ("noop", "E8B358D41E5ED04327921256E0EC031B72CC47AE60BBCF45B0EB7C739D88967E")]
	[DataRow ("comment", "0E5079562AE988F2A93272CCC5DD0E82BD92727095EF7359A9AD6CDD7743CDEC")]
	[DataRow ("comment-same", "E8B358D41E5ED04327921256E0EC031B72CC47AE60BBCF45B0EB7C739D88967E")]
	[DataRow ("comment-null", "062950C028B973C6C06C3A17B0F838301A5539EAF7D1C9938378B46541A53078")]
	[DataRow ("clear", "062950C028B973C6C06C3A17B0F838301A5539EAF7D1C9938378B46541A53078")]
	[DataRow ("exif-comment", "358C2938529D405CCB82B5CE947C93ACAE3BCEAA67CFFBF27C902731C49D5E6B")]
	[DataRow ("xmp-comment", "234F5E081BF9BCFF60BFF5A985102CF3C64DA5FE37B0951E3E1C73077469DA53")]
	[DataRow ("mpf-comment", "1C29BCB8D4CBF18C45909542F66327247C0FE6A0D7223CCF869EB16322A1FD9A")]
	public void JpegPathSave_BytesEqualRender (string op, string expectedHash)
	{
		var data = JpegInput (op);
		var fs = new MockFileSystem ();
		fs.AddFile ("/in.jpg", data);
		var read = StillImageFile.ReadFromFile ("/in.jpg", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Apply (read.File!, op);
		var rendered = read.File!.Render ().ToArray ();

		var save = read.File.SaveToFile ("/out.jpg", fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		var written = fs.ReadAllBytes ("/out.jpg");
		CollectionAssert.AreEqual (rendered, written, op);
		Assert.AreEqual (expectedHash, Convert.ToHexString (SHA256.HashData (written)), op);
	}

	/// <summary>The scan is 80 KB of 0x11. The hash is the path save of that edit at 1322374a.</summary>
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public async Task JpegPathSave_OverOneChunk_CopiesTheScanInChunks (bool saveAsync)
	{
		var scan = new byte[80 * 1024];
		Array.Fill (scan, (byte)0x11);
		var data = Concat (
			[0xFF, 0xD8],
			JpegSegment (0xFE, "Old"u8.ToArray ()),
			JpegSegment (0xC0, [0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00]),
			JpegSegment (0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]),
			scan,
			[0xFF, 0xD9]);
		var fs = new ChunkBoundedFileSystem (data.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.jpg", data);
		fs.MaxRead = data.Length;
		var read = StillImageFile.ReadFromFile ("/in.jpg", fs);
		fs.MaxRead = ChunkBoundedFileSystem.MaxChunk;
		Assert.IsTrue (read.IsSuccess, read.Error);
		fs.ResetSourceLengthGets ();
		read.File!.Tag!.Comment = "PathSave";
		var rendered = read.File.Render ().ToArray ();

		var save = saveAsync
			? await read.File.SaveToFileAsync ("/out.jpg", fs)
			: read.File.SaveToFile ("/out.jpg", fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets);
		var written = fs.Bytes ("/out.jpg");
		CollectionAssert.AreEqual (rendered, written);
		Assert.AreEqual ("45C76CF7112DCCD5B2595DE1CFA0374CCD05E60FF8B1A0A7B147B3FB160A3937", Convert.ToHexString (SHA256.HashData (written)));
	}

	static byte[] JpegInput (string op) => op switch {
		"exif-comment" => Jpeg (JpegExif (ExifTiff ([(0x010F, "Canon")], [])), JpegSegment (0xFE, "Old"u8.ToArray ())),
		"xmp-comment" => Jpeg (JpegXmp (XmpPacket (Alt ("exif:UserComment", "Old")))),
		"mpf-comment" => MpfJpeg (JpegSegment (0xFE, "Old"u8.ToArray ()), [], out _),
		_ => Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ()))
	};

	static void Apply (StillImageFile file, string op)
	{
		if (op is "comment" or "exif-comment" or "xmp-comment" or "mpf-comment")
			file.Tag!.Comment = "PathSave";
		else if (op == "comment-same")
			file.Tag!.Comment = file.Tag.Comment;
		else if (op == "comment-null")
			file.Tag!.Comment = null;
		else if (op == "clear")
			file.Tag!.Clear ();
	}
}
