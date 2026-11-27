// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
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
		var (file, fs) = OpenEditedScan (80 * 1024);
		fs.ResetSourceLengthGets ();
		var rendered = file.Render ().ToArray ();

		var save = saveAsync
			? await file.SaveToFileAsync ("/out.jpg", fs)
			: file.SaveToFile ("/out.jpg", fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets);
		var written = fs.Bytes ("/out.jpg");
		CollectionAssert.AreEqual (rendered, written);
		Assert.AreEqual ("45C76CF7112DCCD5B2595DE1CFA0374CCD05E60FF8B1A0A7B147B3FB160A3937", Convert.ToHexString (SHA256.HashData (written)));
	}

	/// <summary>
	/// A path save stays under a fixed ceiling at three scan sizes. Render of the same edit grows with the scan.
	/// </summary>
	[TestMethod]
	public void JpegPathSave_AllocationStaysUnderTheCeilingWhileRenderGrows ()
	{
		const int n = 1024 * 1024;
		const long ceiling = 256 * 1024;
		// SOI 2 + SOF0 13 + COM "PathSave" 12 + SOS 10 + EOI 2.
		const int framing = 39;
		var saves = new long[3];
		var renders = new long[3];
		for (var size = 0; size < 3; size++) {
			var scanLength = n << size;
			var (file, fs) = OpenEditedScan (scanLength);
			var path = $"/out-{scanLength}.jpg";
			var warm = file.SaveToFile (path, fs);
			Assert.IsTrue (warm.IsSuccess, warm.Error);
			_ = file.Render ();

			FileWriteResult save = default;
			saves[size] = LeastAllocated (() => save = file.SaveToFile (path, fs));
			Assert.IsTrue (save.IsSuccess, save.Error);
			Assert.AreEqual (scanLength + framing, fs.Bytes (path).Length);
			Assert.IsLessThanOrEqualTo (ceiling, saves[size]);

			ReadOnlyMemory<byte> rendered = default;
			renders[size] = LeastAllocated (() => rendered = file.Render ());
			GC.KeepAlive (rendered);
		}

		var high = Math.Max (saves[0], Math.Max (saves[1], saves[2]));
		var low = Math.Min (saves[0], Math.Min (saves[1], saves[2]));
		Assert.IsLessThan (64 * 1024, high - low, $"path save allocated {saves[0]}, {saves[1]}, {saves[2]}");
		Assert.IsGreaterThanOrEqualTo (n, renders[2] - renders[0], $"render allocated {renders[0]} then {renders[2]}");
	}

	[TestMethod]
	[DataRow ("noop", "52B843CD828B9210F16992640E8C8BA9704B65E8F1C6F1D46492DCF130D6A55B")]
	[DataRow ("title", "E9CF8A0A5A10EBA6CBF52534CB14F8F4E91764FE6CA2CA1D43E12AD83F0BD060")]
	[DataRow ("comment", "9C0BB256C7E77EDFB89A1B83F2058F7C775881C389EA783A93752E7C10B01DEA")]
	[DataRow ("comment-same", "0B09E5A56B0EDE41FCF1C6C6E835F80CD87AE103BBE932F4F6E2E315FB7E4B59")]
	[DataRow ("comment-null", "67C719D3156D1CC53B47E4BCD0374DE29B16AEAB7747C9762BFD85172905891A")]
	[DataRow ("clear", "67C719D3156D1CC53B47E4BCD0374DE29B16AEAB7747C9762BFD85172905891A")]
	[DataRow ("xmp-title", "33C2B1D6DE01593E899AE1BD9A30E42388AB253C6A6E533289248A2C47F91BD3")]
	[DataRow ("exif-artist", "AF12D9DE982466B70759290B53AE91276A86EDE9C5C56F8FDFFD7E0EB5C2D547")]
	[DataRow ("raw-title", "927B1D44F059523F1CC166920FA476701A8EDC234BEDFF816FC74F035E89BA80")]
	[DataRow ("idot-title", "F777A6B4481C719D125F9A369F3CCC412B754A0D072982B68776D5CC5679A1FF")]
	[DataRow ("idot-comment", "CD31C8775AC912BECDB0EDABC7C0396431472F9649ACF506738B233712C23C77")]
	public void PngPathSave_BytesEqualRender (string op, string expectedHash)
	{
		var data = PngInput (op);
		var fs = new MockFileSystem ();
		fs.AddFile ("/in.png", data);
		var read = StillImageFile.ReadFromFile ("/in.png", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		ApplyPng (read.File!, op);
		var rendered = read.File!.Render ().ToArray ();

		var save = read.File.SaveToFile ("/out.png", fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		var written = fs.ReadAllBytes ("/out.png");
		CollectionAssert.AreEqual (rendered, written, op);
		Assert.AreEqual (expectedHash, Convert.ToHexString (SHA256.HashData (written)), op);
	}

	/// <summary>The IDAT chunk is 80 KB. The hash is the path save of that title edit at 1322374a.</summary>
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public async Task PngPathSave_OverOneChunk_CopiesTheImageDataInChunks (bool saveAsync)
	{
		var payload = new byte[80 * 1024 - 12];
		var data = Concat (
			PngSignature,
			PngChunk ("IHDR", [0, 0, 0, 2, 0, 0, 0, 3, 8, 0, 0, 0, 0]),
			Text ("Title", "Old"),
			PngChunk ("IDAT", payload),
			PngChunk ("IEND", []));
		var fs = new ChunkBoundedFileSystem (data.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.png", data);
		fs.MaxRead = data.Length;
		var read = StillImageFile.ReadFromFile ("/in.png", fs);
		fs.MaxRead = ChunkBoundedFileSystem.MaxChunk;
		Assert.IsTrue (read.IsSuccess, read.Error);
		fs.ResetSourceLengthGets ();
		read.File!.Tag!.Title = "PathSave";
		var rendered = read.File.Render ().ToArray ();

		var save = saveAsync
			? await read.File.SaveToFileAsync ("/out.png", fs)
			: read.File.SaveToFile ("/out.png", fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets);
		var written = fs.Bytes ("/out.png");
		CollectionAssert.AreEqual (rendered, written);
		const string expected = "6911679D573A41B74F5D62F67F8E7861D80EF89E84BC57A7A43229FB2D2C7E24";
		Assert.AreEqual (expected, Convert.ToHexString (SHA256.HashData (written)));
	}

	[TestMethod]
	[DataRow ("tiff", "59753EFEE53644237F9F0F4299A3B7178C949D9447626418365C7D4E15005DBC")]
	[DataRow ("dng", "96507B9AA0101B7949BE6B6E9761FE0B57B58BB977DED0232CD9096A1374BE5C")]
	public void TiffPathSave_BytesEqualRender (string format, string expectedHash)
	{
		var data = TiffHeader (format);
		var fs = new MockFileSystem ();
		var path = "/in." + format;
		fs.AddFile (path, data);
		var read = StillImageFile.ReadFromFile (path, fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		var rendered = read.File!.Render ().ToArray ();

		var save = read.File.SaveToFile ("/out." + format, fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		var written = fs.ReadAllBytes ("/out." + format);
		CollectionAssert.AreEqual (rendered, written, format);
		Assert.AreEqual (expectedHash, Convert.ToHexString (SHA256.HashData (written)), format);
	}

	/// <summary>The file is a TIFF or DNG header followed by 80 KB. A path save copies that range in chunks.</summary>
	[TestMethod]
	[DataRow ("tiff", false)]
	[DataRow ("tiff", true)]
	[DataRow ("dng", false)]
	[DataRow ("dng", true)]
	public async Task TiffPathSave_OverOneChunk_CopiesTheFileInChunks (string format, bool saveAsync)
	{
		var header = TiffHeader (format);
		var data = new byte[80 * 1024];
		header.CopyTo (data, 0);
		var fs = new ChunkBoundedFileSystem (data.Length + ChunkBoundedFileSystem.MaxChunk);
		var source = "/in." + format;
		var destination = "/out." + format;
		fs.Add (source, data);
		fs.MaxRead = data.Length;
		var read = StillImageFile.ReadFromFile (source, fs);
		fs.MaxRead = ChunkBoundedFileSystem.MaxChunk;
		Assert.IsTrue (read.IsSuccess, read.Error);
		fs.ResetSourceLengthGets ();

		var save = saveAsync
			? await read.File!.SaveToFileAsync (destination, fs)
			: read.File!.SaveToFile (destination, fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets);
		CollectionAssert.AreEqual (data, fs.Bytes (destination));
	}

	[TestMethod]
	[DataRow ("noop", "5B86097573C47754ABA72DC155E0C91A7862DD79EE5E83CA5A0C0D0CB533F4AE")]
	[DataRow ("comment", "675CA34F2FA30837B29FEEE2FA9EF5A9788FF5759D567A80B93765CD9C3048D2")]
	[DataRow ("comment-same", "5B86097573C47754ABA72DC155E0C91A7862DD79EE5E83CA5A0C0D0CB533F4AE")]
	[DataRow ("comment-null", "C08F79AFFAC974D0C787A60E8E07EC2434A587AA7A1AEAAF030A89792307C272")]
	[DataRow ("clear", "C08F79AFFAC974D0C787A60E8E07EC2434A587AA7A1AEAAF030A89792307C272")]
	[DataRow ("xmp-comment", "8A5AF0623A623754A1F5FA84B55846FF5AF5DB7E568C7C487780E0FBB77DD391")]
	public void GifPathSave_BytesEqualRender (string op, string expectedHash)
	{
		var data = GifInput (op);
		var fs = new MockFileSystem ();
		fs.AddFile ("/in.gif", data);
		var read = StillImageFile.ReadFromFile ("/in.gif", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		ApplyGif (read.File!, op);
		var rendered = read.File!.Render ().ToArray ();

		var save = read.File.SaveToFile ("/out.gif", fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		var written = fs.ReadAllBytes ("/out.gif");
		CollectionAssert.AreEqual (rendered, written, op);
		Assert.AreEqual (expectedHash, Convert.ToHexString (SHA256.HashData (written)), op);
	}

	/// <summary>The image block is 80 KB. The hash is the path save of that comment edit at 1322374a.</summary>
	[TestMethod]
	[DataRow (false)]
	[DataRow (true)]
	public async Task GifPathSave_OverOneChunk_CopiesTheImageInChunks (bool saveAsync)
	{
		var data = Concat (GifScreen (1, 1, flags: 0), GifComment ("Old"), GifImage (80 * 1024), [0x3B]);
		var fs = new ChunkBoundedFileSystem (data.Length + ChunkBoundedFileSystem.MaxChunk);
		fs.Add ("/in.gif", data);
		fs.MaxRead = data.Length;
		var read = StillImageFile.ReadFromFile ("/in.gif", fs);
		fs.MaxRead = ChunkBoundedFileSystem.MaxChunk;
		Assert.IsTrue (read.IsSuccess, read.Error);
		fs.ResetSourceLengthGets ();
		read.File!.Tag!.Comment = "PathSave";
		var rendered = read.File.Render ().ToArray ();

		var save = saveAsync
			? await read.File.SaveToFileAsync ("/out.gif", fs)
			: read.File.SaveToFile ("/out.gif", fs);

		Assert.IsTrue (save.IsSuccess, save.Error);
		Assert.AreEqual (1, fs.SourceLengthGets);
		var written = fs.Bytes ("/out.gif");
		CollectionAssert.AreEqual (rendered, written);
		const string expected = "742A2C24C2F5A7CC72B01607DCB6B3517A46B7FD146388CF59F68A8AE175D287";
		Assert.AreEqual (expected, Convert.ToHexString (SHA256.HashData (written)));
	}

	static (StillImageFile File, ChunkBoundedFileSystem Fs) OpenEditedScan (int scanLength)
	{
		var scan = new byte[scanLength];
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
		read.File!.Tag!.Comment = "PathSave";
		return (read.File, fs);
	}

	static long LeastAllocated (Action action)
	{
		var least = long.MaxValue;
		for (var run = 0; run < 3; run++)
			least = Math.Min (least, Bounds.AllocatedBytes (action));
		return least;
	}

	static byte[] JpegInput (string op) => op switch {
		"exif-comment" => Jpeg (JpegExif (ExifTiff ([(0x010F, "Canon")], [])), JpegSegment (0xFE, "Old"u8.ToArray ())),
		"xmp-comment" => Jpeg (JpegXmp (XmpPacket (Alt ("exif:UserComment", "Old")))),
		"mpf-comment" => MpfJpeg (JpegSegment (0xFE, "Old"u8.ToArray ()), [], out _),
		_ => Jpeg (JpegSegment (0xFE, "Old"u8.ToArray ()))
	};

	static byte[] TiffHeader (string format) => format == "dng" ? TiffImage (2, 3, dng: true) : TiffImage (2, 3);

	static byte[] GifInput (string op) => op switch {
		"clear" => Gif (GifComment ("Old")),
		"xmp-comment" => Gif (GifXmp (XmpPacket (Alt ("exif:UserComment", "Old")))),
		_ => Gif (GifComment ("Old"))
	};

	static void ApplyGif (StillImageFile file, string op)
	{
		if (op is "comment" or "xmp-comment")
			file.Tag!.Comment = "PathSave";
		else if (op == "comment-same")
			file.Tag!.Comment = file.Tag.Comment;
		else if (op == "comment-null")
			file.Tag!.Comment = null;
		else if (op == "clear")
			file.Tag!.Clear ();
	}

	static byte[] GifImage (int length)
	{
		var data = new byte[length];
		data[0] = 0x2C;
		data[6] = 1;
		data[8] = 1;
		var at = 11;
		var remain = length - 12;
		while (remain > 0) {
			var take = Math.Min (255, remain - 1);
			if (take <= 0)
				break;
			data[at++] = (byte)take;
			at += take;
			remain -= 1 + take;
		}

		return data;
	}

	static byte[] PngInput (string op) => op switch {
		"noop" or "clear" => Png (Text ("Title", "Old"), Text ("Comment", "Old")),
		"title" => Png (Text ("Title", "Old")),
		"xmp-title" => Png (Xmp (XmpPacket (Alt ("dc:title", "Old")))),
		"exif-artist" => Png (
			PngChunk ("eXIf", ExifTiff ([(0x010F, "Canon"), (0x013B, "Jane")], [])),
			Text ("Author", "Jane")),
		"raw-title" => Png (RawProfile ("xmp", "abcd"u8.ToArray ()), Text ("Title", "Old")),
		"idot-title" => IdotPng (),
		"idot-comment" => IdotPng (Text ("Comment", "Old")),
		_ => Png (Text ("Comment", "Old"))
	};

	static byte[] IdotPng (params byte[][] between) =>
		Concat (
			PngSignature,
			PngChunk ("IHDR", [0, 0, 0, 2, 0, 0, 0, 3, 8, 0, 0, 0, 0]),
			Text ("Title", "Old"),
			PngChunk ("iDOT", new byte[28]),
			Concat (between),
			PngChunk ("IDAT", [0x78, 0x9C, 0x03, 0x00]),
			PngChunk ("IEND", []));

	static void ApplyPng (StillImageFile file, string op)
	{
		if (op is "title" or "xmp-title" or "raw-title" or "idot-title")
			file.Tag!.Title = "PathSave";
		else if (op is "comment" or "idot-comment")
			file.Tag!.Comment = op == "idot-comment" ? "A longer comment" : "PathSave";
		else if (op == "comment-same")
			file.Tag!.Comment = file.Tag.Comment;
		else if (op == "comment-null")
			file.Tag!.Comment = null;
		else if (op == "exif-artist")
			file.Tag!.Artist = "New";
		else if (op == "clear")
			file.Tag!.Clear ();
	}

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
