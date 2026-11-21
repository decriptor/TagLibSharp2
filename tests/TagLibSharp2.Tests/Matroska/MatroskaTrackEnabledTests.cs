// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Matroska;

/// <summary>
/// <c>TrackEntry\FlagEnabled</c> (0xB9, default 1). RFC 9559 §5.1.4.1.4.
/// Invariant: a track whose stored flag is 0 never wins over an enabled track of its type.
/// The builders here also serve <c>MatroskaTrackEnabledSaveTests</c>.
/// </summary>
public partial class MatroskaFileTests
{
	static readonly byte[] ClusterMarker = { 0xC1, 0x05, 0x7E, 0x12, 0x34, 0x56, 0x78, 0x9A };

	[TestMethod]
	public void Read_DisabledDefaultAudio_LosesToAnEnabledTrack ()
	{
		var disabled = AudioEntry (1, 22050, Enabled (0));
		var enabled = AudioEntry (2, 48000, Default (0));
		IMediaFile media = MatroskaFile.Read (FileWith (Master (0x1654AE6B, Concat (disabled, enabled)))).File!;

		Assert.AreEqual (48000, media.AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Read_DisabledDefaultVideo_LosesToAnEnabledTrack ()
	{
		var disabled = VideoEntry (1, 320, Enabled (0));
		var enabled = VideoEntry (2, 640, Default (0));
		IMediaFile media = MatroskaFile.Read (FileWith (Master (0x1654AE6B, Concat (disabled, enabled)))).File!;

		Assert.AreEqual (640, media.VideoProperties!.Value.Width);
	}

	[TestMethod]
	public void Read_EnabledDefault_WinsOverEnabledAndDisabledTracks ()
	{
		var disabledDefault = AudioEntry (1, 8000, Enabled (0));
		var enabledPlain = AudioEntry (2, 16000, Default (0));
		var enabledDefault = AudioEntry (3, 32000);
		var later = AudioEntry (4, 44100);
		IMediaFile media = MatroskaFile.Read (FileWith (Master (0x1654AE6B, Concat (disabledDefault, enabledPlain, enabledDefault, later)))).File!;

		Assert.AreEqual (32000, media.AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Read_EveryAudioTrackDisabled_FallsBackToTheDisabledDefault ()
	{
		var plain = AudioEntry (1, 22050, Enabled (0), Default (0));
		var preferred = AudioEntry (2, 44100, Enabled (0));
		var video = VideoEntry (3, 640);
		var parsed = MatroskaFile.Read (FileWith (Master (0x1654AE6B, Concat (plain, preferred, video)))).File!;
		IMediaFile media = parsed;

		Assert.IsTrue ((parsed.MediaTypes & MediaTypes.Audio) != 0);
		Assert.AreEqual (44100, media.AudioProperties!.SampleRate);
		Assert.IsFalse (parsed.Tracks[0].IsEnabled);
		Assert.IsFalse (parsed.Tracks[1].IsEnabled);
	}

	[TestMethod]
	public void Read_MissingOrEmptyFlag_IsEnabled ()
	{
		var missing = AudioEntry (1, 22050);
		var empty = AudioEntry (2, 44100, Element (0xB9, Array.Empty<byte> ()));
		var one = AudioEntry (3, 48000, Enabled (1));
		var zero = AudioEntry (4, 96000, Element (0xB9, new byte[] { 0, 0 }));
		var tracks = MatroskaFile.Read (FileWith (Master (0x1654AE6B, Concat (missing, empty, one, zero)))).File!.Tracks;

		Assert.AreEqual (4, tracks.Count);
		Assert.IsTrue (tracks[0].IsEnabled);
		Assert.IsTrue (tracks[1].IsEnabled);
		Assert.IsTrue (tracks[2].IsEnabled);
		Assert.IsFalse (tracks[3].IsEnabled);
	}

	[TestMethod]
	public void Read_Tracks_ExposeEachEntryInFileOrder ()
	{
		var video = VideoEntry (1, 640, Element (0x86, "V_VP9"u8));
		var audio = AudioEntry (2, 48000, Element (0x86, "A_OPUS"u8), Default (0));
		var subtitle = Master (0xAE, Concat (
			Element (0xD7, new byte[] { 3 }),
			Element (0x73C5, new byte[] { 0x01, 0x02, 0x03 }),
			Element (0x83, new byte[] { 17 }),
			Element (0x86, "S_TEXT/UTF8"u8),
			Enabled (0)));
		var tracks = MatroskaFile.Read (FileWith (Master (0x1654AE6B, Concat (video, audio, subtitle)))).File!.Tracks;

		Assert.AreEqual (3, tracks.Count);
		Assert.AreEqual (1ul, tracks[0].TrackNumber);
		Assert.AreEqual (MatroskaTrackType.Video, tracks[0].TrackType);
		Assert.AreEqual ("V_VP9", tracks[0].CodecId);
		Assert.IsTrue (tracks[0].IsDefault);
		Assert.AreEqual (MatroskaTrackType.Audio, tracks[1].TrackType);
		Assert.AreEqual (102ul, tracks[1].TrackUid);
		Assert.IsFalse (tracks[1].IsDefault);
		Assert.IsTrue (tracks[1].IsEnabled);
		Assert.AreEqual (3ul, tracks[2].TrackNumber);
		Assert.AreEqual (0x010203ul, tracks[2].TrackUid);
		Assert.AreEqual (MatroskaTrackType.Subtitle, tracks[2].TrackType);
		Assert.IsFalse (tracks[2].IsEnabled);
	}

	[TestMethod]
	public void Read_UnlistedTrackType_KeepsItsNumber ()
	{
		var odd = Master (0xAE, Concat (Element (0xD7, new byte[] { 1 }), Element (0x83, new byte[] { 0x40 })));
		var missing = Master (0xAE, Element (0xD7, new byte[] { 2 }));
		var huge = Master (0xAE, Concat (Element (0xD7, new byte[] { 3 }), Element (0x83, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })));
		var tracks = MatroskaFile.Read (FileWith (Master (0x1654AE6B, Concat (odd, missing, huge)))).File!.Tracks;

		Assert.AreEqual ((MatroskaTrackType)0x40, tracks[0].TrackType);
		Assert.AreEqual (MatroskaTrackType.Unknown, tracks[1].TrackType);
		Assert.AreEqual (0ul, tracks[1].TrackUid);
		Assert.IsNull (tracks[1].CodecId);
		Assert.AreEqual (MatroskaTrackType.Unknown, tracks[2].TrackType);
	}

	[TestMethod]
	public void Read_NoTracks_IsEmpty ()
	{
		var parsed = MatroskaFile.Read (FileWith (Tags (Tag (50, Simple ("TITLE", "Movie"))))).File!;

		Assert.AreEqual (0, parsed.Tracks.Count);
	}

	[TestMethod]
	public void Read_StreamWithClusters_SeesTheFlag ()
	{
		var original = TracksFile (Concat (AudioEntry (1, 22050, Enabled (0)), AudioEntry (2, 44100)), withVoid: 0);
		using var stream = new MemoryStream (original);

		var opened = MediaFile.Read (stream, "/stream.mkv");

		Assert.IsTrue (opened.IsSuccess, opened.Error);
		var file = opened.GetFileAs<MatroskaFile> ()!;
		Assert.IsFalse (file.Tracks[0].IsEnabled);
		Assert.AreEqual (44100, ((IMediaFile)file).AudioProperties!.SampleRate);
	}

	[TestMethod]
	public void Read_TruncatedTracks_NeverThrows ()
	{
		var original = TracksFile (Concat (AudioEntry (1, 22050, Enabled (0)), VideoEntry (2, 640, Enabled (1))), withVoid: 0);
		for (var length = 0; length < original.Length; length++) {
			var result = MatroskaFile.Read (original.AsSpan (0, length));
			if (result.IsSuccess)
				Assert.IsTrue (result.File!.Tracks.Count <= 2);
		}
	}

	static byte[] Enabled (byte value) => Element (0xB9, new[] { value });

	static byte[] Default (byte value) => Element (0x88, new[] { value });

	static byte[] AudioBody (ulong number, int rate, params byte[][] extra) => Concat (
		Element (0xD7, new[] { (byte)number }),
		Element (0x73C5, new[] { (byte)(100 + number) }),
		Element (0x83, new byte[] { 2 }),
		Concat (extra),
		Master (0xE1, Element (0xB5, Float32 (rate))));

	static byte[] AudioEntry (ulong number, int rate, params byte[][] extra) =>
		Master (0xAE, AudioBody (number, rate, extra));

	static byte[] VideoEntry (ulong number, int width, params byte[][] extra) => Master (0xAE, Concat (
		Element (0xD7, new[] { (byte)number }),
		Element (0x73C5, new[] { (byte)(100 + number) }),
		Element (0x83, new byte[] { 1 }),
		Concat (extra),
		Master (0xE0, Concat (Element (0xB0, UIntBytes ((ulong)width)), Element (0xBA, new byte[] { 0xF0 })))));

	static byte[] TracksFile (byte[] entries, int withVoid) =>
		TracksFileWith (Master (0x1654AE6B, entries), withVoid);

	/// <summary>
	/// SeekHead, Info, Tracks, an optional Void, Tags, Cues, Cluster. The SeekHead and Cues carry CRC-32s.
	/// </summary>
	static byte[] TracksFileWith (byte[] tracks, int withVoid)
	{
		var info = Master (0x1549A966, Element (0x2AD7B1, new byte[] { 0x0F, 0x42, 0x40 }));
		var hole = withVoid == 0 ? Array.Empty<byte> () : MakeVoid (withVoid);
		var tags = Tags (Tag (50, Simple ("TITLE", "Movie")));
		var cluster = Master (0x1F43B675, ClusterMarker);
		return Layout (new[] { MatroskaIds.Info, MatroskaIds.Tracks, MatroskaIds.Tags, MatroskaIds.Cues, MatroskaIds.Cluster },
			(seekHead, positions) => Concat (seekHead, info, tracks, hole, tags, CueElement (positions[4]), cluster));
	}

	static byte[] MakeVoid (int total) =>
		total == 2 ? new byte[] { 0xEC, 0x80 } : Element (0xEC, new byte[total - 2]);

	/// <summary>
	/// Lays out a segment twice: once to learn each element's position, then with a SeekHead that points at them.
	/// The seek order follows <paramref name="ids"/>, and each id is the first element of that kind.
	/// </summary>
	static byte[] Layout (ulong[] ids, Func<byte[], ulong[], byte[]> build)
	{
		var positions = new ulong[ids.Length];
		for (var pass = 0; pass < 3; pass++) {
			var seeks = new byte[ids.Length][];
			for (var i = 0; i < ids.Length; i++)
				seeks[i] = SeekEntry (ids[i], positions[i]);
			var body = build (WithCrc (MatroskaIds.SeekHead, Concat (seeks)), positions);
			var found = new ulong[ids.Length];
			for (var i = 0; i < ids.Length; i++)
				found[i] = (ulong)FirstChild (body, ids[i]);
			if (pass > 0 && found.AsSpan ().SequenceEqual (positions))
				return FileWith (body);
			positions = found;
		}

		throw new InvalidOperationException ("layout did not settle");
	}

	static int FirstChild (byte[] body, ulong id)
	{
		var offset = 0;
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id == id)
				return offset;
			offset = element.End;
		}

		return 0;
	}

}
