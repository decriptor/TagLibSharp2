// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Matroska;
using TagLibSharp2.Mp4;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class SaveThroughReadFileSystemTests
{
	[TestMethod]
	public void Aiff_SaveWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var files = Stored ("/song.aiff", TestBuilders.Aiff.CreateMinimal ());
		var opened = AiffFile.ReadFromFile ("/song.aiff", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Id3v2Tag = Titled ("Aiff");

		var saved = opened.File.SaveToFile ("/copy.aiff");

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		var again = AiffFile.ReadFromFile ("/copy.aiff", files);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("Aiff", again.File!.Tag!.Title);
	}

	[TestMethod]
	public async Task Aiff_SaveAsyncWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var files = Stored ("/song.aiff", TestBuilders.Aiff.CreateMinimal ());
		var opened = AiffFile.ReadFromFile ("/song.aiff", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Id3v2Tag = Titled ("Aiff Async");

		var saved = await opened.File.SaveToFileAsync ("/copy.aiff");

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Aiff Async", AiffFile.ReadFromFile ("/copy.aiff", files).File!.Tag!.Title);
	}

	[TestMethod]
	public void Wav_SaveWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var files = Stored ("/song.wav", TestBuilders.Wav.CreateMinimal ());
		var opened = WavFile.ReadFromFile ("/song.wav", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Id3v2Tag = Titled ("Wav");

		var saved = opened.File.SaveToFile ("/copy.wav");

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Wav", WavFile.ReadFromFile ("/copy.wav", files).File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public async Task Wav_SaveAsyncWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var files = Stored ("/song.wav", TestBuilders.Wav.CreateMinimal ());
		var opened = WavFile.ReadFromFile ("/song.wav", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Id3v2Tag = Titled ("Wav Async");

		var saved = await opened.File.SaveToFileAsync ("/copy.wav");

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Wav Async", WavFile.ReadFromFile ("/copy.wav", files).File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void Flac_SaveWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = TestBuilders.Flac.CreateMinimal ();
		var files = Stored ("/song.flac", original);
		var opened = FlacFile.ReadFromFile ("/song.flac", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Title = "Flac";

		var saved = opened.File.SaveToFile ("/copy.flac", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Flac", FlacFile.ReadFromFile ("/copy.flac", files).File!.Title);
	}

	[TestMethod]
	public async Task Flac_SaveAsyncWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = TestBuilders.Flac.CreateMinimal ();
		var files = Stored ("/song.flac", original);
		var opened = FlacFile.ReadFromFile ("/song.flac", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Title = "Flac Async";

		var saved = await opened.File.SaveToFileAsync ("/copy.flac", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Flac Async", FlacFile.ReadFromFile ("/copy.flac", files).File!.Title);
	}

	[TestMethod]
	public void Mp4_SaveWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var files = Stored ("/song.m4a", original);
		var opened = Mp4File.ReadFromFile ("/song.m4a", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Title = "Mp4";

		var saved = opened.File.SaveToFile ("/copy.m4a", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Mp4", Mp4File.ReadFromFile ("/copy.m4a", files).File!.Title);
	}

	[TestMethod]
	public async Task Mp4_SaveAsyncWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var files = Stored ("/song.m4a", original);
		var opened = Mp4File.ReadFromFile ("/song.m4a", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Title = "Mp4 Async";

		var saved = await opened.File.SaveToFileAsync ("/copy.m4a", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Mp4 Async", Mp4File.ReadFromFile ("/copy.m4a", files).File!.Title);
	}

	[TestMethod]
	public void Vorbis_SaveWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = TestBuilders.Ogg.CreateMinimalFile ();
		var files = Stored ("/song.ogg", original);
		var opened = OggVorbisFile.ReadFromFile ("/song.ogg", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Title = "Vorbis";

		var saved = opened.File.SaveToFile ("/copy.ogg", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Vorbis", OggVorbisFile.ReadFromFile ("/copy.ogg", files).File!.Title);
	}

	[TestMethod]
	public async Task Mp3_SaveAsyncWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = TinyMp3 ();
		var files = Stored ("/song.mp3", original);
		var opened = Mp3File.ReadFromFile ("/song.mp3", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Title = "Mp3";

		var saved = await opened.File.SaveToFileAsync ("/copy.mp3", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Mp3", Mp3File.ReadFromFile ("/copy.mp3", files).File!.Title);
	}

	[TestMethod]
	public void Dsf_SaveWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = TestBuilders.Dsf.CreateMinimal ();
		var files = Stored ("/song.dsf", original);
		var opened = DsfFile.ReadFromFile ("/song.dsf", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.EnsureId3v2Tag ().Title = "Dsf";

		var saved = opened.File.SaveToFile ("/copy.dsf", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Dsf", DsfFile.ReadFromFile ("/copy.dsf", files).File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public void Dff_SaveOfOriginalBytesWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = TestBuilders.Dff.CreateMinimal ();
		var files = Stored ("/song.dff", original);
		var opened = DffFile.ReadFromFile ("/song.dff", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.EnsureId3v2Tag ().Title = "Dff";

		var saved = opened.File.SaveToFile ("/copy.dff", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Dff", DffFile.ReadFromFile ("/copy.dff", files).File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public async Task Dff_SaveAsyncWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var files = Stored ("/song.dff", TestBuilders.Dff.CreateMinimal ());
		var opened = DffFile.ReadFromFile ("/song.dff", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.EnsureId3v2Tag ().Title = "Dff Async";

		var saved = await opened.File.SaveToFileAsync ("/copy.dff");

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Dff Async", DffFile.ReadFromFile ("/copy.dff", files).File!.Id3v2Tag!.Title);
	}

	[TestMethod]
	public async Task Matroska_SaveAsyncOfOriginalBytesWithoutAFileSystem_WritesThroughTheOneFromRead ()
	{
		var original = AlbumOnly ();
		var files = Stored ("/song.mkv", original);
		var opened = MatroskaFile.ReadFromFile ("/song.mkv", files);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		opened.File!.Tag.Title = "Matroska";

		var saved = await opened.File.SaveToFileAsync ("/copy.mkv", original);

		Assert.IsTrue (saved.IsSuccess, saved.Error);
		Assert.AreEqual ("Matroska", MatroskaFile.ReadFromFile ("/copy.mkv", files).File!.Tag.Title);
	}

	static MockFileSystem Stored (string path, byte[] data)
	{
		var files = new MockFileSystem ();
		files.AddFile (path, data);
		return files;
	}

	static Id3v2Tag Titled (string title)
	{
		var tag = new Id3v2Tag ();
		tag.Title = title;
		return tag;
	}

	static byte[] TinyMp3 ()
	{
		Assert.IsTrue (MpegFrame.TryParse (new BinaryData (new byte[] { 0xFF, 0xFB, 0x90, 0x00 }), 0, out var frame));
		var audio = new byte[frame!.FrameSize];
		new byte[] { 0xFF, 0xFB, 0x90, 0x00 }.CopyTo (audio, 0);
		return audio;
	}

	static byte[] AlbumOnly ()
	{
		var title = Element (0x67C8, Concat (Element (0x45A3, "TITLE"u8), Element (0x4487, "Album"u8)));
		var targets = Element (0x63C0, Element (0x68CA, new byte[] { 50 }));
		var tag = Element (0x7373, Concat (targets, title));
		var tags = Element (0x1254C367, tag);
		var segment = Element (0x18538067, tags);
		var ebml = Element (0x1A45DFA3, Element (0x4282, "matroska"u8));
		return Concat (ebml, segment);
	}

	static byte[] Element (ulong id, ReadOnlySpan<byte> payload)
	{
		var size = new[] { (byte)(0x80 | payload.Length) };
		var idBytes = new byte[4];
		for (var i = 3; i >= 0; i--) {
			idBytes[i] = (byte)id;
			id >>= 8;
		}

		var width = idBytes[0] == 0 ? idBytes[1] == 0 ? idBytes[2] == 0 ? 1 : 2 : 3 : 4;
		var result = new byte[width + size.Length + payload.Length];
		idBytes.AsSpan (4 - width).CopyTo (result);
		size.CopyTo (result, width);
		payload.CopyTo (result.AsSpan (width + size.Length));
		return result;
	}

	static byte[] Concat (params byte[][] parts)
	{
		var length = 0;
		foreach (var part in parts)
			length += part.Length;
		var result = new byte[length];
		var offset = 0;
		foreach (var part in parts) {
			part.CopyTo (result, offset);
			offset += part.Length;
		}

		return result;
	}
}
