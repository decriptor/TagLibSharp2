// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Aac;
using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Audible;
using TagLibSharp2.Core;
using TagLibSharp2.Dsf;
using TagLibSharp2.Image;
using TagLibSharp2.Mp4;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Aac;
using TagLibSharp2.Tests.Asf;
using TagLibSharp2.Tests.Audible;
using TagLibSharp2.Tests.Mpeg;
using TagLibSharp2.WavPack;
using TagLibSharp2.Xiph;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// A token that is already cancelled loses to a missing file, a source that cannot be
/// read, and a source that cannot be rendered. It still cancels a save that got that far,
/// and it does not replace the destination. WAV and AIFF report the open failure without
/// the re-read prefix. A copy that runs out of source bytes keeps that message.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class PathSaveCancellationTests
{
	const string Destination = "/out.bin";
	const string SourceEnded = "The source ended before the requested bytes were copied";

	[TestMethod]
	[DynamicData (nameof (StreamingKinds))]
	public async Task MissingSource_BeatsAnAlreadyCancelledToken (string kind)
	{
		var fs = new ProbeFileSystem ();
		var file = Open (kind, fs);
		var source = SourcePath (kind);
		fs.Delete (source);

		var save = await Save (file, Destination, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (MissingMessage (kind, source), save.Error);
		Assert.IsFalse (fs.FileExists (Destination));
	}

	[TestMethod]
	[DynamicData (nameof (StreamingKinds))]
	public async Task UnseekableSource_BeatsAnAlreadyCancelledToken (string kind)
	{
		var fs = new ProbeFileSystem ();
		var file = Open (kind, fs);
		fs.Seekable = false;

		var save = await Save (file, Destination, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (SeekMessage (kind), save.Error);
		Assert.IsFalse (fs.FileExists (Destination));
	}

	[TestMethod]
	[DataRow ("wav", "denied", "Access denied: Access denied")]
	[DataRow ("aiff", "denied", "Access denied: Access denied")]
	[DataRow ("wav", "io", "Failed to read file: disk fault")]
	[DataRow ("aiff", "io", "Failed to read file: disk fault")]
	public async Task WavAndAiffOpenFault_BeatsAnAlreadyCancelledToken (string kind, string fault, string expected)
	{
		var fs = new ProbeFileSystem ();
		var file = Open (kind, fs);
		fs.OpenReadFault = fault;

		var save = await Save (file, Destination, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (expected, save.Error);
		Assert.IsFalse (fs.FileExists (Destination));
	}

	[TestMethod]
	[DynamicData (nameof (StreamingKinds))]
	public async Task CancelledToken_OnAReadableSource_DoesNotReplaceTheDestination (string kind)
	{
		var fs = new ProbeFileSystem ();
		var file = Open (kind, fs);

		var save = await Save (file, Destination, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (CancelMessage (kind), save.Error);
		Assert.IsFalse (fs.FileExists (Destination));
	}

	[TestMethod]
	[DataRow ("vorbis")]
	[DataRow ("opus")]
	[DataRow ("oggflac")]
	public async Task EmptySourcePath_BeatsAnAlreadyCancelledToken (string kind)
	{
		var bytes = Bytes (kind);
		var fs = new ProbeFileSystem ();
		using var stream = new MemoryStream (bytes);
		var file = ReadWithoutPath (kind, stream, fs);

		var save = await Save (file, Destination, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("No source path available. File was not read from disk.", save.Error);
		Assert.IsFalse (fs.FileExists (Destination));
	}

	[TestMethod]
	[DynamicData (nameof (ShortSources))]
	public async Task ShortSource_BeatsAnAlreadyCancelledToken (string kind, string expected)
	{
		var fs = new ProbeFileSystem ();
		var file = Open (kind, fs);
		fs.Store.AddFile (SourcePath (kind), [1]);

		var save = await Save (file, Destination, fs, Cancelled ());

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (expected, save.Error);
		Assert.IsFalse (fs.FileExists (Destination));
	}

	[TestMethod]
	public async Task AiffDeclaredSizePastMemory_BeatsAnAlreadyCancelledToken ()
	{
		var fs = new ProbeFileSystem ();
		var file = Open ("aiff-huge", fs);

		var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException> (
			() => Save (file, Destination, fs, Cancelled ()));

		Assert.AreEqual ("Cannot render an AIFF file larger than supported memory.", thrown.Message);
		Assert.IsFalse (fs.FileExists (Destination));
	}

	[TestMethod]
	public void WriteStream_ShortCopy_ReturnsTheSourceEndedMessageAndLeavesTheDestination ()
	{
		var fs = new MockFileSystem ();
		var original = new byte[] { 1, 2, 3, 4 };
		fs.AddFile ("/dest.bin", original);

		var save = AtomicFileWriter.WriteStream ("/dest.bin", fs, _ => throw new EndOfStreamException (SourceEnded));

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual (SourceEnded, save.Error);
		Assert.IsTrue (original.AsSpan ().SequenceEqual (fs.ReadAllBytes ("/dest.bin")));
	}

	[TestMethod]
	public void WriteStream_IoException_StaysAnIoErrorAndLeavesTheDestination ()
	{
		var fs = new MockFileSystem ();
		var original = new byte[] { 9, 8, 7 };
		fs.AddFile ("/dest.bin", original);

		var save = AtomicFileWriter.WriteStream ("/dest.bin", fs, _ => throw new IOException ("disk full"));

		Assert.IsFalse (save.IsSuccess);
		Assert.AreEqual ("I/O error writing file: disk full", save.Error);
		Assert.IsTrue (original.AsSpan ().SequenceEqual (fs.ReadAllBytes ("/dest.bin")));
	}

	[TestMethod]
	public void WriteStream_OversizedRead_Escapes ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/dest.bin", [1]);

		var thrown = Assert.ThrowsExactly<InvalidOperationException> (
			() => AtomicFileWriter.WriteStream (
				"/dest.bin",
				fs,
				_ => throw new InvalidOperationException ("read larger than the copy buffer")));

		Assert.AreEqual ("read larger than the copy buffer", thrown.Message);
		Assert.IsTrue (new byte[] { 1 }.AsSpan ().SequenceEqual (fs.ReadAllBytes ("/dest.bin")));
	}

	public static IEnumerable<object[]> StreamingKinds ()
	{
		foreach (var kind in Kinds)
			yield return new object[] { kind };
	}

	public static IEnumerable<object[]> ShortSources ()
	{
		yield return ["flac", "Failed to render FLAC file"];
		yield return ["dsf", "Failed to render DSF file"];
		yield return ["vorbis", "Failed to render Ogg Vorbis file"];
		yield return ["opus", "Failed to render Ogg Opus file"];
		yield return ["oggflac", "Failed to render Ogg FLAC file"];
		yield return ["m4a", "Failed to render MP4 file"];
		yield return ["mpg", "The original data is not the MPEG stream that was read"];
		foreach (var image in new[] { "jpeg", "png", "gif", "tiff", "dng", "bmp", "pcx", "netpbm", "svg" })
			yield return new object[] { image, "The source ended before the requested bytes were copied" };
	}

	static readonly string[] Kinds = [
		"vorbis", "opus", "oggflac", "mp3", "flac", "asf", "dsf", "ape", "wv", "mpc", "aac", "mpg", "aa", "m4a", "wav", "aiff",
		"jpeg", "png", "gif", "tiff", "dng", "bmp", "pcx", "netpbm", "svg"
	];

	static CancellationToken Cancelled ()
	{
		var source = new CancellationTokenSource ();
		source.Cancel ();
		return source.Token;
	}

	static string SourcePath (string kind) => "/in." + kind;

	static string MissingMessage (string kind, string path) =>
		kind is "wav" or "aiff"
			? $"File not found: {path}"
			: $"Failed to re-read source file: File not found: {path}";

	static string SeekMessage (string kind) =>
		kind is "wav" or "aiff"
			? "source is not seekable"
			: "Failed to re-read source file: source is not seekable";

	static string CancelMessage (string kind) =>
		kind is "vorbis" or "opus" or "oggflac" or "wav" or "aiff"
			? "Operation was cancelled"
			: "Failed to re-read source file: Operation was cancelled";

	static object Open (string kind, ProbeFileSystem fs)
	{
		var path = SourcePath (kind);
		fs.Add (path, Bytes (kind));
		var read = MediaFile.Read (path, fs);
		Assert.IsTrue (read.IsSuccess, kind + ": " + read.Error);
		return read.File!;
	}

	static object ReadWithoutPath (string kind, Stream stream, IFileSystem fs)
	{
		switch (kind) {
			case "vorbis":
				var vorbis = OggVorbisFile.Read (stream, "", fs);
				Assert.IsTrue (vorbis.IsSuccess, vorbis.Error);
				return vorbis.File!;
			case "opus":
				var opus = OggOpusFile.Read (stream, "", fs);
				Assert.IsTrue (opus.IsSuccess, opus.Error);
				return opus.File!;
			default:
				var flac = OggFlacFile.Read (stream, "", fs);
				Assert.IsTrue (flac.IsSuccess, flac.Error);
				return flac.File!;
		}
	}

	static Task<FileWriteResult> Save (object file, string path, IFileSystem fs, CancellationToken token) =>
		file switch {
			OggVorbisFile vorbis => vorbis.SaveToFileAsync (path, fs, token),
			OggOpusFile opus => opus.SaveToFileAsync (path, fs, token),
			OggFlacFile oggFlac => oggFlac.SaveToFileAsync (path, fs, token),
			Mp3File mp3 => mp3.SaveToFileAsync (path, fs, token),
			FlacFile flac => flac.SaveToFileAsync (path, fs, token),
			AsfFile asf => asf.SaveToFileAsync (path, fs, token),
			DsfFile dsf => dsf.SaveToFileAsync (path, fs, token),
			MonkeysAudioFile ape => ape.SaveToFileAsync (path, fs, token),
			WavPackFile wv => wv.SaveToFileAsync (path, fs, token),
			MusepackFile mpc => mpc.SaveToFileAsync (path, fs, token),
			AacFile aac => aac.SaveToFileAsync (path, fs, token),
			MpegPsFile mpg => mpg.SaveToFileAsync (path, fs, token),
			AudibleFile audible => audible.SaveToFileAsync (path, fs, token),
			Mp4File mp4 => mp4.SaveToFileAsync (path, fs, token),
			WavFile wav => wav.SaveToFileAsync (path, fs, token),
			AiffFile aiff => aiff.SaveToFileAsync (path, fs, token),
			StillImageFile image => image.SaveToFileAsync (path, fs, token),
			_ => throw new InvalidOperationException (file.GetType ().FullName)
		};

	static byte[] Bytes (string kind) => kind switch {
		"vorbis" => TestBuilders.Ogg.CreateMinimalFile (),
		"opus" => TestBuilders.Opus.CreateMinimalFile (),
		"oggflac" => TestBuilders.OggFlac.CreateMinimal (),
		"mp3" => Mp3 (),
		"flac" => TestBuilders.Flac.CreateWithAudioData (new byte[32]),
		"asf" => AsfTestBuilder.CreateMinimalWma (title: "Original"),
		"dsf" => TestBuilders.Dsf.CreateMinimal (),
		"ape" => TestBuilders.MonkeysAudio.CreateMinimal (),
		"wv" => TestBuilders.WavPack.CreateMinimal (),
		"mpc" => TestBuilders.Musepack.CreateMinimal (),
		"aac" => AacTestBuilder.Adts (2),
		"mpg" => MpegPsTestBuilder.ProgramStream (new MpegPsTestBuilder.Options {
			VideoFrames = 1,
			AudioKind = MpegPsTestBuilder.Audio.None,
			AudioFrames = 0,
			PaddingBytes = 64
		}),
		"aa" => AudibleTestBuilder.Create (audio: AudibleTestBuilder.Audio (64)),
		"m4a" => TestBuilders.Mp4.CreateWithAudioData (new byte[32]),
		"wav" => Wav (8),
		"aiff" => Aiff (8, ssndSize: 8),
		"aiff-huge" => Aiff (8, ssndSize: 0x80000000),
		"jpeg" => Jpeg (),
		"png" => Png (),
		"gif" => Gif (),
		"tiff" => TiffImage (2, 3),
		"dng" => TiffImage (2, 3, dng: true),
		"bmp" => Bmp (4, 2),
		"pcx" => Pcx (0, 0, 1, 1, planes: 1),
		"netpbm" => "P4\n1 1\n"u8.ToArray (),
		"svg" => "<svg width=\"2\" height=\"3\"></svg>"u8.ToArray (),
		_ => throw new InvalidOperationException (kind)
	};

	static byte[] Mp3 ()
	{
		var id3v2 = new TagLibSharp2.Id3.Id3v2.Id3v2Tag { Title = "Original" }.Render ().ToArray ();
		var frame = new byte[417];
		frame[0] = 0xFF;
		frame[1] = 0xFB;
		frame[2] = 0x90;
		var id3v1 = TestBuilders.Id3v1.CreateTag (title: "Original");
		var file = new byte[id3v2.Length + frame.Length + id3v1.Length];
		id3v2.CopyTo (file, 0);
		frame.CopyTo (file, id3v2.Length);
		id3v1.CopyTo (file, id3v2.Length + frame.Length);
		return file;
	}

	static byte[] Wav (int audioLength)
	{
		var audio = new byte[audioLength];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)(i + 1);
		var pad = audio.Length & 1;
		var riffSize = 4 + (8 + 16) + (8 + audio.Length + pad);
		using var data = new MemoryStream ();
		WriteAscii (data, "RIFF");
		WriteU32 (data, (uint)riffSize, bigEndian: false);
		WriteAscii (data, "WAVE");
		WriteAscii (data, "fmt ");
		WriteU32 (data, 16, bigEndian: false);
		WriteU16 (data, 1, bigEndian: false);
		WriteU16 (data, 1, bigEndian: false);
		WriteU32 (data, 44100, bigEndian: false);
		WriteU32 (data, 88200, bigEndian: false);
		WriteU16 (data, 2, bigEndian: false);
		WriteU16 (data, 16, bigEndian: false);
		WriteAscii (data, "data");
		WriteU32 (data, (uint)audio.Length, bigEndian: false);
		data.Write (audio, 0, audio.Length);
		if (pad == 1)
			data.WriteByte (0);
		return data.ToArray ();
	}

	static byte[] Aiff (int audioLength, uint ssndSize)
	{
		var audio = new byte[audioLength];
		for (var i = 0; i < audio.Length; i++)
			audio[i] = (byte)(i + 1);
		using var data = new MemoryStream ();
		WriteAscii (data, "FORM");
		WriteU32 (data, 0, bigEndian: true);
		WriteAscii (data, "AIFF");
		WriteAscii (data, "COMM");
		WriteU32 (data, 18, bigEndian: true);
		WriteU16 (data, 1, bigEndian: true);
		WriteU32 (data, (uint)audio.Length, bigEndian: true);
		WriteU16 (data, 8, bigEndian: true);
		data.Write (new byte[] { 0x40, 0x0E, 0xAC, 0x44, 0, 0, 0, 0, 0, 0 }, 0, 10);
		WriteAscii (data, "SSND");
		WriteU32 (data, ssndSize, bigEndian: true);
		WriteU32 (data, 0, bigEndian: true);
		WriteU32 (data, 0, bigEndian: true);
		data.Write (audio, 0, audio.Length);
		var bytes = data.ToArray ();
		BinaryPrimitives.WriteUInt32BigEndian (bytes.AsSpan (4), (uint)(bytes.Length - 8));
		return bytes;
	}

	static void WriteAscii (Stream stream, string text)
	{
		var bytes = Encoding.ASCII.GetBytes (text);
		stream.Write (bytes, 0, bytes.Length);
	}

	static void WriteU16 (Stream stream, ushort value, bool bigEndian)
	{
		Span<byte> bytes = stackalloc byte[2];
		if (bigEndian)
			BinaryPrimitives.WriteUInt16BigEndian (bytes, value);
		else
			BinaryPrimitives.WriteUInt16LittleEndian (bytes, value);
		stream.Write (bytes.ToArray (), 0, 2);
	}

	static void WriteU32 (Stream stream, uint value, bool bigEndian)
	{
		var bytes = new byte[4];
		if (bigEndian)
			BinaryPrimitives.WriteUInt32BigEndian (bytes, value);
		else
			BinaryPrimitives.WriteUInt32LittleEndian (bytes, value);
		stream.Write (bytes, 0, 4);
	}

	[TestMethod]
	public async Task Probe_ForwardsTheStore_AndAnUnseekableSourceHidesItsLength ()
	{
		var fs = new ProbeFileSystem ();
		fs.Add ("/in.bin", new byte[] { 1, 2, 3, 4 });
		CollectionAssert.AreEqual (new byte[] { 1, 2, 3, 4 }, fs.ReadAllBytes ("/in.bin"));
		CollectionAssert.AreEqual (new byte[] { 1, 2, 3, 4 }, await fs.ReadAllBytesAsync ("/in.bin"));
		fs.WriteAllBytes ("/out.bin", new byte[] { 5 });
		await fs.WriteAllBytesAsync ("/out-async.bin", new byte[] { 6 });
		using (var readWrite = fs.OpenReadWrite ("/out.bin"))
			Assert.IsTrue (readWrite.CanRead);
		using (var created = fs.Create ("/new.bin"))
			created.Write (new byte[] { 7 }, 0, 1);
		fs.Move ("/out.bin", "/moved.bin");
		Assert.IsTrue (fs.FileExists ("/moved.bin"));
		fs.Delete ("/moved.bin");
		Assert.IsFalse (fs.FileExists ("/moved.bin"));
		Assert.AreEqual ("a.bin", fs.GetFileName ("/dir/a.bin"));
		Assert.AreEqual (Path.GetDirectoryName ("/dir/a.bin"), fs.GetDirectoryName ("/dir/a.bin"));
		Assert.AreEqual (Path.Combine ("a", "b"), fs.CombinePath ("a", "b"));

		fs.Seekable = false;
		using var source = fs.OpenRead ("/in.bin");
		Assert.IsFalse (source.CanSeek);
		Assert.IsFalse (source.CanWrite);
		Assert.IsTrue (source.CanRead);
		Assert.ThrowsExactly<NotSupportedException> (() => _ = source.Length);
		Assert.ThrowsExactly<NotSupportedException> (() => _ = source.Position);
		Assert.ThrowsExactly<NotSupportedException> (() => source.Position = 0);
		Assert.ThrowsExactly<NotSupportedException> (() => source.Seek (0, SeekOrigin.Begin));
		Assert.ThrowsExactly<NotSupportedException> (() => source.SetLength (1));
		Assert.ThrowsExactly<NotSupportedException> (() => source.Write (new byte[1], 0, 1));
		source.Flush ();
		Assert.AreEqual (1, source.Read (new byte[1], 0, 1));
	}
}
