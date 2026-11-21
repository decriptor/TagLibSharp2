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

	static FileWriteResult Save (MediaFileResult opened, MockFileSystem fs) =>
		opened.File switch {
			WavPackFile wav => wav.SaveToFile (fs),
			MonkeysAudioFile ape => ape.SaveToFile (fs),
			MusepackFile mpc => mpc.SaveToFile (fs),
			_ => FileWriteResult.Failure ("unexpected file")
		};

	static byte[] AudioWithApeAndId3 (string extension)
	{
		var audio = Audio (extension);
		var ape = new ApeTag { Artist = "Legacy Artist" }.RenderWithOptions (includeHeader: true).ToArray ();
		var id3 = new byte[128];
		"TAG"u8.CopyTo (id3);
		"KeepMe"u8.CopyTo (id3.AsSpan (3));

		var data = new byte[audio.Length + ape.Length + id3.Length];
		audio.CopyTo (data, 0);
		ape.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + ape.Length);
		return data;
	}

	static int AudioLength (string extension) => Audio (extension).Length;

	static byte[] Audio (string extension) => extension switch {
		"wv" => TestBuilders.WavPack.CreateMinimal (),
		"ape" => TestBuilders.MonkeysAudio.CreateMinimal (),
		"mpc" => TestBuilders.Musepack.CreateMinimal (),
		_ => throw new ArgumentOutOfRangeException (nameof (extension))
	};
}
