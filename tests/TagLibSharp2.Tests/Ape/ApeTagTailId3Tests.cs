// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
using TagLibSharp2.Core;
using TagLibSharp2.Musepack;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.WavPack;

namespace TagLibSharp2.Tests.Ape;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Ape")]
public class ApeTagTailId3Tests
{
	[TestMethod]
	[DataRow ("wv")]
	[DataRow ("ape")]
	[DataRow ("mpc")]
	public async Task Open_ApeBeforeId3v1_KeepsArtistWhenAlbumIsSaved (string extension)
	{
		var data = AudioWithApeAndId3 (extension);
		var name = "/song." + extension;

		var viaStream = MediaFile.Read (new MemoryStream (data), name);
		Assert.AreEqual ("Legacy Artist", viaStream.Tag?.Artist, viaStream.Error);

		var viaAsync = await MediaFile.ReadAsync (new MemoryStream (data), name);
		Assert.AreEqual ("Legacy Artist", viaAsync.Tag?.Artist, viaAsync.Error);

		var fs = new MockFileSystem ();
		fs.AddFile (name, data);
		var opened = MediaFile.Read (name, fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual ("Legacy Artist", opened.Tag!.Artist);

		opened.Tag.Album = "New Album";
		var save = Save (opened, fs);
		Assert.IsTrue (save.IsSuccess, save.Error);

		var again = MediaFile.Read (name, fs);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("Legacy Artist", again.Tag!.Artist);
		Assert.AreEqual ("New Album", again.Tag.Album);

		var bytes = fs.ReadAllBytes (name);
		Assert.IsTrue (bytes.AsSpan (bytes.Length - 128, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (bytes.AsSpan (bytes.Length - 125, 6).SequenceEqual ("KeepMe"u8));
		CollectionAssert.AreEqual (data[..AudioLength (extension)], bytes[..AudioLength (extension)]);
	}

	[TestMethod]
	[DataRow ("wv", "v2")]
	[DataRow ("wv", "v1")]
	[DataRow ("ape", "v2")]
	[DataRow ("ape", "v1")]
	[DataRow ("mpc", "v2")]
	[DataRow ("mpc", "v1")]
	public async Task Open_Lyrics3BetweenApeAndId3_KeepsArtistWhenAlbumIsSaved (string extension, string lyricsVersion)
	{
		var lyrics = lyricsVersion == "v1" ? Lyrics3V1 () : Lyrics3V2 ();
		var data = AudioWithApeLyricsAndId3 (extension, lyrics);
		var name = "/song." + extension;

		var viaStream = MediaFile.Read (new MemoryStream (data), name);
		Assert.AreEqual ("Legacy Artist", viaStream.Tag?.Artist, viaStream.Error);

		var viaAsync = await MediaFile.ReadAsync (new MemoryStream (data), name);
		Assert.AreEqual ("Legacy Artist", viaAsync.Tag?.Artist, viaAsync.Error);

		var fs = new MockFileSystem ();
		fs.AddFile (name, data);
		var opened = MediaFile.Read (name, fs);
		Assert.IsTrue (opened.IsSuccess, opened.Error);
		Assert.AreEqual ("Legacy Artist", opened.Tag!.Artist);

		opened.Tag.Album = "New Album";
		var save = Save (opened, fs);
		Assert.IsTrue (save.IsSuccess, save.Error);

		var again = MediaFile.Read (name, fs);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("Legacy Artist", again.Tag!.Artist);
		Assert.AreEqual ("New Album", again.Tag.Album);

		var bytes = fs.ReadAllBytes (name);
		Assert.IsTrue (bytes.AsSpan (bytes.Length - 128, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (bytes.AsSpan (bytes.Length - 125, 6).SequenceEqual ("KeepMe"u8));
		Assert.IsTrue (bytes.AsSpan ().IndexOf (lyrics) >= 0);
		CollectionAssert.AreEqual (data[..AudioLength (extension)], bytes[..AudioLength (extension)]);
	}

	[TestMethod]
	[DataRow ("wv")]
	[DataRow ("ape")]
	[DataRow ("mpc")]
	public void Open_Footer160BytesBeforeEofWithoutId3v1_ReadsArtist (string extension)
	{
		// EOF-160 is the no-Lyrics3 footer. The 128 bytes after it are not "TAG".
		var audio = Audio (extension);
		var ape = new ApeTag { Artist = "Legacy Artist" }.RenderWithOptions (includeHeader: true).ToArray ();
		var gap = new byte[ApeTagLocation.Id3v1Size];
		gap[0] = (byte)'N';
		var data = new byte[audio.Length + ape.Length + gap.Length];
		audio.CopyTo (data, 0);
		ape.CopyTo (data, audio.Length);
		gap.CopyTo (data, audio.Length + ape.Length);

		Assert.IsTrue (data.AsSpan (data.Length - 160, 8).SequenceEqual ("APETAGEX"u8));
		Assert.IsFalse (data.AsSpan (data.Length - ApeTagLocation.Id3v1Size, 3).SequenceEqual ("TAG"u8));

		var parsed = ApeTag.Parse (data);
		Assert.IsTrue (parsed.IsSuccess, parsed.Error);
		Assert.AreEqual ("Legacy Artist", parsed.Tag!.Artist);

		var opened = MediaFile.Read (new MemoryStream (data), "/song." + extension);
		Assert.AreEqual ("Legacy Artist", opened.Tag?.Artist, opened.Error);

		var fs = new MockFileSystem ();
		fs.AddFile ("/song." + extension, data);
		var fromPath = MediaFile.Read ("/song." + extension, fs);
		Assert.AreEqual ("Legacy Artist", fromPath.Tag?.Artist, fromPath.Error);
	}

	static FileWriteResult Save (MediaFileResult opened, MockFileSystem fs) =>
		opened.File switch {
			WavPackFile wav => wav.SaveToFile (fs),
			MonkeysAudioFile ape => ape.SaveToFile (fs),
			MusepackFile mpc => mpc.SaveToFile (fs),
			_ => FileWriteResult.Failure ("unexpected file")
		};

	static byte[] AudioWithApeAndId3 (string extension) =>
		AudioWithApeLyricsAndId3 (extension, []);

	static byte[] AudioWithApeLyricsAndId3 (string extension, byte[] lyrics)
	{
		var audio = Audio (extension);
		var ape = new ApeTag { Artist = "Legacy Artist" }.RenderWithOptions (includeHeader: true).ToArray ();
		var id3 = new byte[128];
		"TAG"u8.CopyTo (id3);
		"KeepMe"u8.CopyTo (id3.AsSpan (3));

		var data = new byte[audio.Length + ape.Length + lyrics.Length + id3.Length];
		audio.CopyTo (data, 0);
		ape.CopyTo (data, audio.Length);
		lyrics.CopyTo (data, audio.Length + ape.Length);
		id3.CopyTo (data, audio.Length + ape.Length + lyrics.Length);
		return data;
	}

	/// <summary>Lyrics3 v2: LYRICSBEGIN, a 6-digit body size, and LYRICS200.</summary>
	static byte[] Lyrics3V2 ()
	{
		ReadOnlySpan<byte> begin = "LYRICSBEGIN"u8;
		ReadOnlySpan<byte> end = "LYRICS200"u8;
		var block = new byte[begin.Length + 6 + end.Length];
		begin.CopyTo (block);
		var size = begin.Length;
		for (var i = 5; i >= 0; i--) {
			block[begin.Length + i] = (byte)('0' + (size % 10));
			size /= 10;
		}

		end.CopyTo (block.AsSpan (begin.Length + 6));
		return block;
	}

	/// <summary>Lyrics3 v1: LYRICSBEGIN, the text, and LYRICSEND. There is no size field.</summary>
	static byte[] Lyrics3V1 ()
	{
		ReadOnlySpan<byte> text = "Line"u8;
		var block = new byte[11 + text.Length + 9];
		"LYRICSBEGIN"u8.CopyTo (block);
		text.CopyTo (block.AsSpan (11));
		"LYRICSEND"u8.CopyTo (block.AsSpan (11 + text.Length));
		return block;
	}

	static int AudioLength (string extension) => Audio (extension).Length;

	static byte[] Audio (string extension) => extension switch {
		"wv" => TestBuilders.WavPack.CreateMinimal (),
		"ape" => TestBuilders.MonkeysAudio.CreateMinimal (),
		"mpc" => TestBuilders.Musepack.CreateMinimal (),
		_ => throw new ArgumentOutOfRangeException (nameof (extension))
	};
}
