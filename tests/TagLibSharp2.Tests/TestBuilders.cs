// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Ogg;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests;

/// <summary>
/// MP4 codec types for test data generation.
/// </summary>
public enum Mp4CodecType
{
	Aac,
	Alac
}

/// <summary>
/// MP4 picture types for cover art.
/// </summary>
public enum Mp4PictureType
{
	Jpeg = 13,
	Png = 14
}

/// <summary>
/// Centralized test data builders to reduce duplication and ensure consistency.
/// </summary>
/// <remarks>
/// Each format has its own nested builder class. Use these instead of duplicating
/// helper methods in individual test files.
/// </remarks>
public static class TestBuilders
{
	/// <summary>
	/// Builders for ID3v2 tag test data.
	/// </summary>
	public static class Id3v2
	{
		/// <summary>
		/// Creates a minimal valid ID3v2 header.
		/// </summary>
		/// <param name="version">Major version (3 or 4).</param>
		/// <param name="size">Tag body size (syncsafe encoded).</param>
		/// <returns>10-byte ID3v2 header.</returns>
		public static byte[] CreateHeader (byte version, uint size)
		{
			var data = new byte[TestConstants.Id3v2.HeaderSize];
			TestConstants.Magic.Id3.CopyTo (data, 0);
			data[3] = version;
			data[4] = TestConstants.Id3v2.MinorVersion;
			data[5] = 0; // flags

			// Syncsafe size
			data[6] = (byte)((size >> 21) & 0x7F);
			data[7] = (byte)((size >> 14) & 0x7F);
			data[8] = (byte)((size >> 7) & 0x7F);
			data[9] = (byte)(size & 0x7F);

			return data;
		}

		/// <summary>
		/// Creates a text frame (TIT2, TPE1, etc.) with Latin-1 encoding.
		/// </summary>
		/// <param name="frameId">4-character frame ID.</param>
		/// <param name="text">Text content.</param>
		/// <param name="version">ID3v2 major version (3 or 4).</param>
		/// <returns>Complete frame bytes (header + content).</returns>
		public static byte[] CreateTextFrame (string frameId, string text, byte version)
		{
			var textBytes = Encoding.Latin1.GetBytes (text);
			var frameContent = new byte[1 + textBytes.Length];
			frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
			Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

			return CreateFrame (frameId, frameContent, version);
		}

		/// <summary>
		/// Creates a raw frame with the given content.
		/// </summary>
		/// <param name="frameId">4-character frame ID.</param>
		/// <param name="content">Frame content bytes.</param>
		/// <param name="version">ID3v2 major version (3 or 4).</param>
		/// <returns>Complete frame bytes (header + content).</returns>
		public static byte[] CreateFrame (string frameId, byte[] content, byte version)
		{
			var frameSize = content.Length;
			var frame = new byte[TestConstants.Id3v2.FrameHeaderSize + frameSize];

			// Frame ID
			Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);

			// Size (syncsafe for v2.4, big-endian for v2.3)
			if (version == TestConstants.Id3v2.Version4) {
				frame[4] = (byte)((frameSize >> 21) & 0x7F);
				frame[5] = (byte)((frameSize >> 14) & 0x7F);
				frame[6] = (byte)((frameSize >> 7) & 0x7F);
				frame[7] = (byte)(frameSize & 0x7F);
			} else {
				frame[4] = (byte)((frameSize >> 24) & 0xFF);
				frame[5] = (byte)((frameSize >> 16) & 0xFF);
				frame[6] = (byte)((frameSize >> 8) & 0xFF);
				frame[7] = (byte)(frameSize & 0xFF);
			}

			// Flags (2 bytes, zeroes)
			frame[8] = 0;
			frame[9] = 0;

			// Content
			Array.Copy (content, 0, frame, TestConstants.Id3v2.FrameHeaderSize, frameSize);

			return frame;
		}

		/// <summary>
		/// Creates a complete ID3v2 tag with a single text frame.
		/// </summary>
		/// <param name="frameId">4-character frame ID.</param>
		/// <param name="text">Text content.</param>
		/// <param name="version">ID3v2 major version (3 or 4).</param>
		/// <returns>Complete tag bytes (header + frame).</returns>
		public static byte[] CreateTagWithTextFrame (string frameId, string text, byte version)
		{
			var frame = CreateTextFrame (frameId, text, version);
			var header = CreateHeader (version, (uint)frame.Length);

			var result = new byte[header.Length + frame.Length];
			Array.Copy (header, result, header.Length);
			Array.Copy (frame, 0, result, header.Length, frame.Length);

			return result;
		}

		/// <summary>
		/// Creates a URL frame (W*** except WXXX) with the given URL.
		/// </summary>
		/// <param name="frameId">4-character frame ID (e.g., WOAR, WFED).</param>
		/// <param name="url">URL content (Latin-1 encoded).</param>
		/// <param name="version">ID3v2 major version (3 or 4).</param>
		/// <returns>Complete frame bytes (header + content).</returns>
		public static byte[] CreateUrlFrame (string frameId, string url, byte version)
		{
			var urlBytes = Encoding.Latin1.GetBytes (url);
			return CreateFrame (frameId, urlBytes, version);
		}

		/// <summary>
		/// Creates a complete ID3v2 tag with a single URL frame.
		/// </summary>
		/// <param name="frameId">4-character frame ID (e.g., WOAR, WFED).</param>
		/// <param name="url">URL content.</param>
		/// <param name="version">ID3v2 major version (3 or 4).</param>
		/// <returns>Complete tag bytes (header + frame).</returns>
		public static byte[] CreateTagWithUrlFrame (string frameId, string url, byte version)
		{
			var frame = CreateUrlFrame (frameId, url, version);
			var header = CreateHeader (version, (uint)frame.Length);

			var result = new byte[header.Length + frame.Length];
			Array.Copy (header, result, header.Length);
			Array.Copy (frame, 0, result, header.Length, frame.Length);

			return result;
		}

		/// <summary>
		/// Creates a complete ID3v2 tag with multiple text frames.
		/// </summary>
		/// <param name="version">ID3v2 major version (3 or 4).</param>
		/// <param name="frames">Dictionary of frame ID to text value.</param>
		/// <returns>Complete tag bytes.</returns>
		public static byte[] CreateTagWithFrames (byte version, params (string frameId, string text)[] frames)
		{
			var allFrames = frames.SelectMany (f => CreateTextFrame (f.frameId, f.text, version)).ToArray ();
			var header = CreateHeader (version, (uint)allFrames.Length);

			var result = new byte[header.Length + allFrames.Length];
			Array.Copy (header, result, header.Length);
			Array.Copy (allFrames, 0, result, header.Length, allFrames.Length);

			return result;
		}

		/// <summary>
		/// Creates raw text frame content with specified encoding.
		/// Used for testing text frame parsing with different encodings.
		/// </summary>
		/// <param name="encoding">Text encoding type.</param>
		/// <param name="text">Text content.</param>
		/// <returns>Frame content bytes (encoding byte + encoded text).</returns>
		public static byte[] CreateTextFrameData (TextEncodingType encoding, string text) => encoding switch {
			TextEncodingType.Latin1 => [(byte)encoding, .. Encoding.Latin1.GetBytes (text)],
			TextEncodingType.Utf16WithBom => [(byte)encoding, 0xFF, 0xFE, .. Encoding.Unicode.GetBytes (text)],
			TextEncodingType.Utf16BE => [(byte)encoding, .. Encoding.BigEndianUnicode.GetBytes (text)],
			TextEncodingType.Utf8 => [(byte)encoding, .. Encoding.UTF8.GetBytes (text)],
			_ => throw new ArgumentException ($"Unsupported encoding: {encoding}", nameof (encoding))
		};

		/// <summary>
		/// Creates a minimal empty ID3v2 tag (header only, size = 0).
		/// </summary>
		public static byte[] CreateEmptyTag (byte version = 4) => CreateHeader (version, 0);

		/// <summary>
		/// Creates an ID3v2 tag with padding.
		/// </summary>
		public static byte[] CreateTagWithPadding (byte version, int paddingSize)
		{
			var header = CreateHeader (version, (uint)paddingSize);
			var result = new byte[header.Length + paddingSize];
			Array.Copy (header, result, header.Length);
			return result;
		}

		/// <summary>
		/// Creates a minimal valid ID3v2 header with flags.
		/// </summary>
		public static byte[] CreateHeaderWithFlags (byte version, uint size, byte flags)
		{
			var data = CreateHeader (version, size);
			data[5] = flags;
			return data;
		}

		/// <summary>
		/// Creates an ID3v2.2 text frame (3-byte ID, 3-byte size).
		/// </summary>
		/// <param name="frameId">3-character frame ID (e.g., "TT2").</param>
		/// <param name="text">Text content.</param>
		/// <returns>Complete frame bytes (6-byte header + content).</returns>
		public static byte[] CreateTextFrameV22 (string frameId, string text)
		{
			var textBytes = Encoding.Latin1.GetBytes (text);
			var frameContent = new byte[1 + textBytes.Length];
			frameContent[0] = TestConstants.Id3v2.EncodingLatin1;
			Array.Copy (textBytes, 0, frameContent, 1, textBytes.Length);

			return CreateFrameV22 (frameId, frameContent);
		}

		/// <summary>
		/// Creates a raw ID3v2.2 frame (6-byte header).
		/// </summary>
		public static byte[] CreateFrameV22 (string frameId, byte[] content)
		{
			var frameSize = content.Length;
			var frame = new byte[TestConstants.Id3v2.FrameHeaderSizeV22 + frameSize];

			// Frame ID (3 bytes)
			Encoding.ASCII.GetBytes (frameId).CopyTo (frame, 0);

			// Size (3-byte big-endian)
			frame[3] = (byte)((frameSize >> 16) & 0xFF);
			frame[4] = (byte)((frameSize >> 8) & 0xFF);
			frame[5] = (byte)(frameSize & 0xFF);

			// Content
			Array.Copy (content, 0, frame, TestConstants.Id3v2.FrameHeaderSizeV22, frameSize);

			return frame;
		}

		/// <summary>
		/// Creates a complete ID3v2.2 tag with a single text frame.
		/// </summary>
		public static byte[] CreateTagWithTextFrameV22 (string frameId, string text)
		{
			var frame = CreateTextFrameV22 (frameId, text);
			var header = CreateHeader (TestConstants.Id3v2.Version2, (uint)frame.Length);

			var result = new byte[header.Length + frame.Length];
			Array.Copy (header, result, header.Length);
			Array.Copy (frame, 0, result, header.Length, frame.Length);

			return result;
		}

		/// <summary>
		/// Applies unsynchronization to data (inserts 0x00 after each 0xFF).
		/// </summary>
		public static byte[] ApplyUnsynchronization (byte[] data)
		{
			// Count how many 0xFF bytes need unsync
			var count = 0;
			foreach (var b in data) {
				if (b == 0xFF)
					count++;
			}

			if (count == 0)
				return data;

			// Create unsynchronized output
			var output = new byte[data.Length + count];
			var outIndex = 0;
			foreach (var b in data) {
				output[outIndex++] = b;
				if (b == 0xFF)
					output[outIndex++] = 0x00;
			}

			return output;
		}

		/// <summary>
		/// Creates an ID3v2 tag with unsynchronization applied.
		/// </summary>
		public static byte[] CreateTagWithUnsynchronization (byte version, byte[] frameData)
		{
			var unsyncData = ApplyUnsynchronization (frameData);
			var header = CreateHeaderWithFlags (version, (uint)unsyncData.Length, TestConstants.Id3v2.FlagUnsynchronization);

			var result = new byte[header.Length + unsyncData.Length];
			Array.Copy (header, result, header.Length);
			Array.Copy (unsyncData, 0, result, header.Length, unsyncData.Length);

			return result;
		}
	}

	/// <summary>
	/// Builders for ID3v1 tag test data.
	/// </summary>
	public static class Id3v1
	{
		/// <summary>
		/// Creates a complete ID3v1.0 tag.
		/// </summary>
		public static byte[] CreateTag (
			string title = "",
			string artist = "",
			string album = "",
			string year = "",
			string comment = "",
			byte genre = 0xFF)
		{
			var data = new byte[TestConstants.Id3v1.TagSize];
			TestConstants.Id3v1.Signature.CopyTo (data, 0);

			WriteFixedString (data, 3, title, TestConstants.Id3v1.TitleLength);
			WriteFixedString (data, 33, artist, TestConstants.Id3v1.ArtistLength);
			WriteFixedString (data, 63, album, TestConstants.Id3v1.AlbumLength);
			WriteFixedString (data, 93, year, TestConstants.Id3v1.YearLength);
			WriteFixedString (data, 97, comment, TestConstants.Id3v1.CommentLength);
			data[127] = genre;

			return data;
		}

		/// <summary>
		/// Creates a complete ID3v1.1 tag with track number.
		/// </summary>
		public static byte[] CreateTagV11 (
			string title = "",
			string artist = "",
			string album = "",
			string year = "",
			string comment = "",
			byte track = 0,
			byte genre = 0xFF)
		{
			var data = CreateTag (title, artist, album, year, comment, genre);

			// ID3v1.1: byte 125 = 0, byte 126 = track number
			data[125] = 0;
			data[126] = track;

			return data;
		}

		static void WriteFixedString (byte[] data, int offset, string value, int length)
		{
			var bytes = Encoding.Latin1.GetBytes (value);
			var copyLength = Math.Min (bytes.Length, length);
			Array.Copy (bytes, 0, data, offset, copyLength);
		}
	}

	/// <summary>
	/// Builders for FLAC file test data.
	/// </summary>
	public static class Flac
	{
		// Static readonly arrays to avoid allocations on each call
		static readonly byte[] StreamInfoHeaderLast = [0x80, 0x00, 0x00, 0x22];
		static readonly byte[] StreamInfoHeaderNotLast = [0x00, 0x00, 0x00, 0x22];
		static readonly byte[] DefaultBlockSizes = [0x10, 0x00, 0x10, 0x00]; // 4096/4096
		static readonly byte[] ZeroFrameSizes = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
		static readonly byte[] Default44100StreamInfo = [0x0A, 0xC4, 0x42, 0xF0, 0x00, 0x00, 0x00, 0x00];

		/// <summary>
		/// Creates a minimal valid FLAC file with STREAMINFO.
		/// Default properties: 44100Hz, 2 channels, 16-bit, 0 samples.
		/// </summary>
		public static byte[] CreateMinimal ()
		{
			return CreateWithStreamInfo (44100, 2, 16, 0);
		}

		/// <summary>
		/// Creates a FLAC file with custom STREAMINFO properties.
		/// </summary>
		/// <param name="sampleRate">Sample rate in Hz.</param>
		/// <param name="channels">Number of channels (1-8).</param>
		/// <param name="bitsPerSample">Bits per sample (4-32).</param>
		/// <param name="totalSamples">Total number of samples.</param>
		public static byte[] CreateWithStreamInfo (int sampleRate, int channels, int bitsPerSample, ulong totalSamples)
		{
			var builder = new BinaryDataBuilder ();

			// Magic: "fLaC"
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO block header: last=true, type=0, size=34
			builder.Add (StreamInfoHeaderLast);

			// min/max block size: 4096
			builder.Add (DefaultBlockSizes);
			// min/max frame size: 0 (unknown)
			builder.Add (ZeroFrameSizes);

			// Bytes 10-17: sample rate (20 bits), channels-1 (3 bits), bps-1 (5 bits), total samples (36 bits)
			var sr = sampleRate & 0xFFFFF;
			var ch = (channels - 1) & 0x07;
			var bps = (bitsPerSample - 1) & 0x1F;
			var samplesUpper = (int)((totalSamples >> 32) & 0x0F);
			var samplesLower = (uint)(totalSamples & 0xFFFFFFFF);

			builder.Add ((byte)((sr >> 12) & 0xFF));
			builder.Add ((byte)((sr >> 4) & 0xFF));
			builder.Add ((byte)(((sr & 0x0F) << 4) | ((ch & 0x07) << 1) | ((bps >> 4) & 0x01)));
			builder.Add ((byte)(((bps & 0x0F) << 4) | (samplesUpper & 0x0F)));
			builder.Add ((byte)((samplesLower >> 24) & 0xFF));
			builder.Add ((byte)((samplesLower >> 16) & 0xFF));
			builder.Add ((byte)((samplesLower >> 8) & 0xFF));
			builder.Add ((byte)(samplesLower & 0xFF));

			// MD5 signature (16 bytes - all zeros)
			builder.AddZeros (16);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with a specific MD5 signature.
		/// </summary>
		/// <param name="md5">16-byte MD5 signature, or null for all zeros.</param>
		public static byte[] CreateWithMd5 (byte[]? md5 = null)
		{
			var builder = new BinaryDataBuilder ();

			// Magic: "fLaC"
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO block header: last=true, type=0, size=34
			builder.Add (StreamInfoHeaderLast);

			// min/max block size: 4096
			builder.Add (DefaultBlockSizes);
			// min/max frame size: 0 (unknown)
			builder.Add (ZeroFrameSizes);

			// Sample rate, channels, bps, total samples (using defaults)
			builder.Add ((byte)0xAC); // 44100 Hz upper
			builder.Add ((byte)0x44);
			builder.Add ((byte)0xF0); // 44100 lower + 2 channels
			builder.Add ((byte)0x00); // 16 bits + 0 samples upper
			builder.AddZeros (4); // total samples lower

			// MD5 signature (16 bytes)
			if (md5 is { Length: 16 })
				builder.Add (md5);
			else
				builder.AddZeros (16);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with a Vorbis Comment block.
		/// </summary>
		public static byte[] CreateWithVorbisComment (string? title = null, string? artist = null)
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO block (not last)
			builder.Add (StreamInfoHeaderNotLast);
			AddDefaultStreamInfoData (builder);

			// Vorbis Comment block (last)
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			if (!string.IsNullOrEmpty (title))
				comment.Title = title;
			if (!string.IsNullOrEmpty (artist))
				comment.Artist = artist;

			var commentData = comment.Render ().ToArray ();
			builder.Add ((byte)(TestConstants.Flac.LastBlockFlag | TestConstants.Flac.BlockTypeVorbisComment));
			builder.AddUInt24BE ((uint)commentData.Length);
			builder.Add (commentData);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with a picture block.
		/// </summary>
		public static byte[] CreateWithPicture (PictureType pictureType = PictureType.FrontCover)
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO (not last)
			builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x22 });
			AddDefaultStreamInfoData (builder);

			// PICTURE block
			var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
			var picture = new FlacPicture ("image/jpeg", pictureType, "", new BinaryData (jpegData), 100, 100, 24, 0);
			var pictureData = picture.RenderContent ();

			builder.Add ((byte)(TestConstants.Flac.LastBlockFlag | TestConstants.Flac.BlockTypePicture));
			builder.AddUInt24BE ((uint)pictureData.Length);
			builder.Add (pictureData);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with multiple pictures.
		/// </summary>
		public static byte[] CreateWithMultiplePictures ()
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO (not last)
			builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x22 });
			AddDefaultStreamInfoData (builder);

			// Picture 1 (front cover, not last)
			var pic1Data = new FlacPicture ("image/jpeg", PictureType.FrontCover, "",
				new BinaryData (new byte[] { 0xFF, 0xD8 }), 100, 100, 24, 0).RenderContent ();
			builder.Add (TestConstants.Flac.BlockTypePicture);
			builder.AddUInt24BE ((uint)pic1Data.Length);
			builder.Add (pic1Data);

			// Picture 2 (back cover, last)
			var pic2Data = new FlacPicture ("image/jpeg", PictureType.BackCover, "",
				new BinaryData (new byte[] { 0xFF, 0xD9 }), 200, 200, 24, 0).RenderContent ();
			builder.Add ((byte)(TestConstants.Flac.LastBlockFlag | TestConstants.Flac.BlockTypePicture));
			builder.AddUInt24BE ((uint)pic2Data.Length);
			builder.Add (pic2Data);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with audio data following the metadata.
		/// </summary>
		public static byte[] CreateWithAudioData (byte[] audioData)
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO (last)
			builder.Add (new byte[] { 0x80, 0x00, 0x00, 0x22 });
			AddDefaultStreamInfoData (builder);

			// Audio data
			builder.Add (audioData);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with a SEEKTABLE block.
		/// </summary>
		public static byte[] CreateWithSeekTable ()
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO (not last)
			builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x22 });
			AddDefaultStreamInfoData (builder);

			// SEEKTABLE block (last, type=3)
			const int seekTableSize = 36; // 2 seek points * 18 bytes each
			builder.Add ((byte)(TestConstants.Flac.LastBlockFlag | TestConstants.Flac.BlockTypeSeekTable));
			builder.AddUInt24BE (seekTableSize);

			// Seek point 1: sample 0 at offset 0, 4096 samples
			builder.AddZeros (8);
			builder.AddZeros (8);
			builder.Add (new byte[] { 0x10, 0x00 });

			// Seek point 2: sample 44100 at offset 12345
			builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xAC, 0x44 });
			builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x30, 0x39 });
			builder.Add (new byte[] { 0x10, 0x00 });

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with an APPLICATION block.
		/// </summary>
		public static byte[] CreateWithApplicationBlock (string appId = "TEST")
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO (not last)
			builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x22 });
			AddDefaultStreamInfoData (builder);

			// APPLICATION block (last, type=2)
			var appIdBytes = Encoding.ASCII.GetBytes (appId.PadRight (4)[..4]);
			var appContent = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

			builder.Add ((byte)(TestConstants.Flac.LastBlockFlag | TestConstants.Flac.BlockTypeApplication));
			builder.AddUInt24BE ((uint)(appIdBytes.Length + appContent.Length));
			builder.Add (appIdBytes);
			builder.Add (appContent);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with both SEEKTABLE and APPLICATION blocks.
		/// </summary>
		public static byte[] CreateWithSeekTableAndApplication ()
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO (not last)
			builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x22 });
			AddDefaultStreamInfoData (builder);

			// SEEKTABLE block (not last)
			const int seekTableSize = 18; // 1 seek point
			builder.Add (TestConstants.Flac.BlockTypeSeekTable);
			builder.AddUInt24BE (seekTableSize);
			builder.AddZeros (18);

			// APPLICATION block (last)
			var appId = Encoding.ASCII.GetBytes ("APPL");
			var appContent = new byte[] { 0xAA, 0xBB };
			builder.Add ((byte)(TestConstants.Flac.LastBlockFlag | TestConstants.Flac.BlockTypeApplication));
			builder.AddUInt24BE ((uint)(appId.Length + appContent.Length));
			builder.Add (appId);
			builder.Add (appContent);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a FLAC file with an invalid STREAMINFO size for error testing.
		/// </summary>
		public static byte[] CreateWithInvalidStreamInfoSize (int size)
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO block header: last=true, type=0, with custom size
			builder.Add ((byte)0x80);
			builder.Add ((byte)((size >> 16) & 0xFF));
			builder.Add ((byte)((size >> 8) & 0xFF));
			builder.Add ((byte)(size & 0xFF));

			// Add data with requested size
			builder.AddZeros (size);

			return builder.ToArray ();
		}

		/// <summary>
		/// Adds default STREAMINFO data (34 bytes) for 44100Hz/2ch/16-bit.
		/// </summary>
		static void AddDefaultStreamInfoData (BinaryDataBuilder builder)
		{
			// min/max block size: 4096
			builder.Add (DefaultBlockSizes);
			// min/max frame size: 0
			builder.Add (ZeroFrameSizes);
			// 44100Hz, 2ch, 16-bit, 0 samples
			builder.Add (Default44100StreamInfo);
			// MD5 signature
			builder.AddZeros (16);
		}
	}

	/// <summary>
	/// Builders for Ogg Vorbis file test data.
	/// </summary>
	public static class Ogg
	{
		/// <summary>
		/// Creates a minimal Ogg page with the given data.
		/// </summary>
		/// <param name="data">Page data (will be segmented automatically).</param>
		/// <param name="sequenceNumber">Page sequence number.</param>
		/// <param name="flags">Page flags (BOS, EOS, continuation).</param>
		/// <param name="serialNumber">Stream serial number.</param>
		/// <param name="calculateCrc">If true, calculate proper CRC; if false, use zeroes (for simpler testing).</param>
		/// <returns>Complete Ogg page bytes.</returns>
		public static byte[] CreatePage (
			byte[] data,
			uint sequenceNumber,
			OggPageFlags flags,
			uint serialNumber = 1,
			bool calculateCrc = true)
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Ogg);

			// Version
			builder.Add ((byte)0);

			// Flags
			builder.Add ((byte)flags);

			// Granule position (8 bytes)
			builder.AddUInt64LE (0);

			// Serial number
			builder.AddUInt32LE (serialNumber);

			// Sequence number
			builder.AddUInt32LE (sequenceNumber);

			// CRC placeholder
			builder.AddUInt32LE (0);

			// Segment count and table
			var segments = CalculateSegments (data.Length);
			builder.Add ((byte)segments.Length);
			builder.Add (segments);

			// Page data
			builder.Add (data);

			var page = builder.ToArray ();

			// Calculate and insert CRC if requested
			if (calculateCrc) {
				var crc = OggCrc.Calculate (page);
				page[22] = (byte)(crc & 0xFF);
				page[23] = (byte)((crc >> 8) & 0xFF);
				page[24] = (byte)((crc >> 16) & 0xFF);
				page[25] = (byte)((crc >> 24) & 0xFF);
			}

			return page;
		}

		/// <summary>
		/// Creates a Vorbis identification packet.
		/// </summary>
		public static byte[] CreateIdentificationPacket (
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			int bitrateNominal = 128000)
		{
			var builder = new BinaryDataBuilder ();

			// Packet type + "vorbis"
			builder.Add (TestConstants.Ogg.PacketTypeIdentification);
			builder.Add (TestConstants.Magic.Vorbis);

			// Version (0)
			builder.AddUInt32LE (0);

			// Channels
			builder.Add ((byte)channels);

			// Sample rate
			builder.AddUInt32LE ((uint)sampleRate);

			// Bitrate (max, nominal, min)
			builder.AddUInt32LE (0); // max
			builder.AddUInt32LE ((uint)bitrateNominal);
			builder.AddUInt32LE (0); // min

			// Block sizes (4 bits each)
			builder.Add ((byte)0xB8); // blocksize_0 = 8, blocksize_1 = 11

			// Framing bit
			builder.Add ((byte)0x01);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a Vorbis comment packet with framing bit.
		/// </summary>
		public static byte[] CreateCommentPacket (TagLibSharp2.Xiph.VorbisComment comment, bool validFramingBit = true)
		{
			var commentData = comment.Render ().ToArray ();
			var builder = new BinaryDataBuilder ();

			// Packet type + "vorbis"
			builder.Add (TestConstants.Ogg.PacketTypeComment);
			builder.Add (TestConstants.Magic.Vorbis);

			// Comment data
			builder.Add (commentData);

			// Framing bit (1 = valid, 0 = invalid)
			builder.Add ((byte)(validFramingBit ? 0x01 : 0x00));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a minimal Vorbis setup packet.
		/// </summary>
		public static byte[] CreateSetupPacket ()
		{
			var builder = new BinaryDataBuilder ();
			builder.Add (TestConstants.Ogg.PacketTypeSetup);
			builder.Add (TestConstants.Magic.Vorbis);
			// Minimal setup data placeholder
			builder.Add (new byte[10]);
			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a minimal valid Ogg Vorbis file with metadata.
		/// </summary>
		/// <param name="title">Title metadata (null or empty to skip).</param>
		/// <param name="artist">Artist metadata (null or empty to skip).</param>
		/// <param name="calculateCrc">If true, pages have valid CRCs.</param>
		public static byte[] CreateMinimalFile (string title = "Test", string artist = "Artist", bool calculateCrc = true)
		{
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			if (!string.IsNullOrEmpty (title))
				comment.Title = title;
			if (!string.IsNullOrEmpty (artist))
				comment.Artist = artist;

			return CreateFile (comment, calculateCrc: calculateCrc);
		}

		/// <summary>
		/// Creates an Ogg Vorbis file with custom audio properties.
		/// </summary>
		public static byte[] CreateFileWithProperties (
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			int bitrateNominal = 128000,
			bool calculateCrc = true)
		{
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			return CreateFile (comment, sampleRate, channels, bitrateNominal, calculateCrc: calculateCrc);
		}

		/// <summary>
		/// Creates an Ogg Vorbis file with an invalid framing bit for error testing.
		/// </summary>
		public static byte[] CreateFileWithInvalidFramingBit (bool calculateCrc = false)
		{
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			return CreateFile (comment, validFramingBit: false, calculateCrc: calculateCrc);
		}

		/// <summary>
		/// Creates an Ogg Vorbis file with a multi-page comment spanning 3+ pages.
		/// </summary>
		public static byte[] CreateFileWithMultiPageComment (bool calculateCrc = false)
		{
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			comment.Title = "Multi-Page Title";
			// Create a 70KB+ field to span multiple pages
			comment.SetValue ("LONGFIELD", new string ('X', 70000));

			return CreateFile (comment, splitCommentAcrossPages: true, calculateCrc: calculateCrc);
		}

		/// <summary>
		/// Creates an Ogg page with non-Vorbis data for error testing.
		/// </summary>
		public static byte[] CreatePageWithNonVorbisData (bool calculateCrc = false)
		{
			var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
			return CreatePage (data, 0, OggPageFlags.BeginOfStream, calculateCrc: calculateCrc);
		}

		/// <summary>
		/// Creates an Ogg Vorbis file with an invalid vorbis_version for error testing.
		/// Per spec, vorbis_version must be 0. Any other value is invalid.
		/// </summary>
		public static byte[] CreateFileWithInvalidVorbisVersion (uint version = 1, bool calculateCrc = false)
		{
			var builder = new BinaryDataBuilder ();

			// Create identification packet with invalid version
			var idBuilder = new BinaryDataBuilder ();
			idBuilder.Add (TestConstants.Ogg.PacketTypeIdentification);
			idBuilder.Add (TestConstants.Magic.Vorbis);
			idBuilder.AddUInt32LE (version); // Invalid version
			idBuilder.Add ((byte)2); // channels
			idBuilder.AddUInt32LE (44100); // sample rate
			idBuilder.AddUInt32LE (0); // bitrate max
			idBuilder.AddUInt32LE (128000); // bitrate nominal
			idBuilder.AddUInt32LE (0); // bitrate min
			idBuilder.Add ((byte)0xB8); // block sizes
			idBuilder.Add ((byte)0x01); // framing bit
			var idPacket = idBuilder.ToArray ();

			// Add pages
			builder.Add (CreatePage (idPacket, 0, OggPageFlags.BeginOfStream, calculateCrc: calculateCrc));

			var comment = new TagLibSharp2.Xiph.VorbisComment ("Test");
			var commentPacket = CreateCommentPacket (comment, validFramingBit: true);
			builder.Add (CreatePage (commentPacket, 1, OggPageFlags.None, calculateCrc: calculateCrc));

			var setupPacket = CreateSetupPacket ();
			builder.Add (CreatePage (setupPacket, 2, OggPageFlags.EndOfStream, calculateCrc: calculateCrc));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a complete Ogg Vorbis file with full control over parameters.
		/// </summary>
		public static byte[] CreateFile (
			TagLibSharp2.Xiph.VorbisComment comment,
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			int bitrateNominal = 128000,
			bool validFramingBit = true,
			bool splitCommentAcrossPages = false,
			bool calculateCrc = true)
		{
			var builder = new BinaryDataBuilder ();

			var idPacket = CreateIdentificationPacket (sampleRate, channels, bitrateNominal);
			var commentPacket = CreateCommentPacket (comment, validFramingBit);
			var setupPacket = CreateSetupPacket ();

			// Page 1: BOS with identification header
			builder.Add (CreatePage (idPacket, 0, OggPageFlags.BeginOfStream, calculateCrc: calculateCrc));

			if (splitCommentAcrossPages) {
				// Split comment packet across multiple pages using multi-page aware builder
				var sequenceNum = 1u;
				var pages = CreateMultiPagePacket (commentPacket, sequenceNum, calculateCrc);
				foreach (var page in pages)
					builder.Add (page);
				sequenceNum += (uint)pages.Length;

				// Final page: Setup + EOS
				builder.Add (CreatePage (setupPacket, sequenceNum, OggPageFlags.EndOfStream, calculateCrc: calculateCrc));
			} else {
				// Page 2: Comment header
				builder.Add (CreatePage (commentPacket, 1, OggPageFlags.None, calculateCrc: calculateCrc));

				// Page 3: Setup + EOS
				builder.Add (CreatePage (setupPacket, 2, OggPageFlags.EndOfStream, calculateCrc: calculateCrc));
			}

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates multiple Ogg pages for a packet that spans pages.
		/// Handles segment table correctly: continuation pages have all 255 segments.
		/// </summary>
		static byte[][] CreateMultiPagePacket (byte[] packet, uint startSequence, bool calculateCrc)
		{
			var pages = new List<byte[]> ();
			var offset = 0;
			var sequenceNum = startSequence;

			// Max segments per page: 255, max bytes per segment: 255
			// But we use a more reasonable chunk size for multi-page spanning
			const int maxSegmentsPerPage = 255;
			const int bytesPerSegment = 255;
			const int maxBytesPerPage = maxSegmentsPerPage * bytesPerSegment; // 65025

			while (offset < packet.Length) {
				var remaining = packet.Length - offset;
				var isFirstPage = offset == 0;
				var flags = isFirstPage ? OggPageFlags.None : OggPageFlags.Continuation;

				// For continuation: take up to maxBytesPerPage
				// Make sure we fill exactly maxBytesPerPage for non-final pages
				int pageDataLen;
				bool isFinalPage;
				if (remaining > maxBytesPerPage) {
					pageDataLen = maxBytesPerPage;
					isFinalPage = false;
				} else {
					pageDataLen = remaining;
					isFinalPage = true;
				}

				var pageData = new byte[pageDataLen];
				Array.Copy (packet, offset, pageData, 0, pageDataLen);

				// Create page with proper segment table
				var page = CreatePageWithSegmentTable (pageData, sequenceNum, flags, isFinalPage, calculateCrc);
				pages.Add (page);

				offset += pageDataLen;
				sequenceNum++;
			}

			return pages.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg page with proper segment table for multi-page spanning.
		/// </summary>
		static byte[] CreatePageWithSegmentTable (byte[] data, uint sequenceNumber, OggPageFlags flags, bool isPacketEnd, bool calculateCrc)
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Ogg);

			// Version
			builder.Add ((byte)0);

			// Flags
			builder.Add ((byte)flags);

			// Granule position (8 bytes)
			builder.AddUInt64LE (0);

			// Serial number
			builder.AddUInt32LE (1);

			// Sequence number
			builder.AddUInt32LE (sequenceNumber);

			// CRC placeholder
			builder.AddUInt32LE (0);

			// Build segment table
			// For multi-page spanning: use 255-byte segments
			// If not end of packet: all segments must be 255 (indicating continuation)
			// If end of packet: last segment can be < 255
			var segments = new List<byte> ();
			var remaining = data.Length;
			while (remaining > 0) {
				if (remaining >= 255) {
					segments.Add (255);
					remaining -= 255;
				} else {
					segments.Add ((byte)remaining);
					remaining = 0;
				}
			}
			// If this is end of packet and data length was exactly divisible by 255,
			// we need to add a 0-length segment to indicate packet end
			if (isPacketEnd && data.Length > 0 && data.Length % 255 == 0)
				segments.Add (0);

			// If NOT end of packet, ensure last segment is 255 to indicate continuation
			if (!isPacketEnd && segments.Count > 0 && segments[^1] != 255) {
				// This shouldn't happen if we take maxBytesPerPage, but safety check
				// Pad with zeros if needed (shouldn't be needed with our chunking)
			}

			builder.Add ((byte)segments.Count);
			foreach (var seg in segments)
				builder.Add (seg);

			// Data
			builder.Add (data);

			var page = builder.ToArray ();

			// Calculate and insert CRC if requested
			if (calculateCrc) {
				var crc = OggCrc.Calculate (page);
				page[22] = (byte)(crc & 0xFF);
				page[23] = (byte)((crc >> 8) & 0xFF);
				page[24] = (byte)((crc >> 16) & 0xFF);
				page[25] = (byte)((crc >> 24) & 0xFF);
			}

			return page;
		}

		/// <summary>
		/// Calculates segment table for Ogg page.
		/// </summary>
		static byte[] CalculateSegments (int dataLength)
		{
			var count = (dataLength / 255) + 1;
			var segments = new byte[count];
			for (int i = 0; i < count - 1; i++)
				segments[i] = 255;
			segments[count - 1] = (byte)(dataLength % 255);
			return segments;
		}
	}

	/// <summary>
	/// Builders for Ogg Opus file test data.
	/// </summary>
	public static class Opus
	{
		/// <summary>
		/// Creates an OpusHead identification packet.
		/// </summary>
		public static byte[] CreateOpusHeadPacket (
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			ushort preSkip = 312,
			uint inputSampleRate = TestConstants.AudioProperties.SampleRate48000,
			short outputGain = 0)
		{
			var builder = new BinaryDataBuilder ();

			// Magic "OpusHead" (8 bytes)
			builder.Add (TestConstants.Magic.OpusHead);

			// Version (must be 1)
			builder.Add ((byte)1);

			// Channels
			builder.Add ((byte)channels);

			// Pre-skip (little-endian)
			builder.AddUInt16LE (preSkip);

			// Input sample rate (little-endian)
			builder.AddUInt32LE (inputSampleRate);

			// Output gain (little-endian, signed)
			builder.AddUInt16LE ((ushort)outputGain);

			// Channel mapping family (0 = mono or stereo, no mapping table)
			builder.Add ((byte)0);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an OpusTags packet (no framing bit, unlike Vorbis).
		/// </summary>
		public static byte[] CreateOpusTagsPacket (TagLibSharp2.Xiph.VorbisComment comment)
		{
			var commentData = comment.Render ().ToArray ();
			var builder = new BinaryDataBuilder ();

			// Magic "OpusTags" (8 bytes)
			builder.Add (TestConstants.Magic.OpusTags);

			// Comment data (NO framing bit for Opus!)
			builder.Add (commentData);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a minimal valid Ogg Opus file with metadata.
		/// </summary>
		/// <param name="title">Title metadata (null or empty to skip).</param>
		/// <param name="artist">Artist metadata (null or empty to skip).</param>
		/// <param name="calculateCrc">If true, pages have valid CRCs.</param>
		public static byte[] CreateMinimalFile (string title = "Test", string artist = "Artist", bool calculateCrc = true)
		{
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			if (!string.IsNullOrEmpty (title))
				comment.Title = title;
			if (!string.IsNullOrEmpty (artist))
				comment.Artist = artist;

			return CreateFile (comment, calculateCrc: calculateCrc);
		}

		/// <summary>
		/// Creates an Ogg Opus file with custom audio properties.
		/// </summary>
		public static byte[] CreateFileWithProperties (
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			ushort preSkip = 312,
			uint inputSampleRate = TestConstants.AudioProperties.SampleRate48000,
			ulong granulePosition = 480000, // 10 seconds at 48kHz
			bool calculateCrc = true)
		{
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			return CreateFile (comment, channels, preSkip, inputSampleRate, granulePosition, calculateCrc: calculateCrc);
		}

		/// <summary>
		/// Creates a complete Ogg Opus file with full control over parameters.
		/// </summary>
		public static byte[] CreateFile (
			TagLibSharp2.Xiph.VorbisComment comment,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			ushort preSkip = 312,
			uint inputSampleRate = TestConstants.AudioProperties.SampleRate48000,
			ulong granulePosition = 480000, // 10 seconds at 48kHz
			bool calculateCrc = true)
		{
			var builder = new BinaryDataBuilder ();

			var opusHeadPacket = CreateOpusHeadPacket (channels, preSkip, inputSampleRate);
			var opusTagsPacket = CreateOpusTagsPacket (comment);

			// Page 1: BOS with OpusHead
			builder.Add (Ogg.CreatePage (opusHeadPacket, 0, OggPageFlags.BeginOfStream, calculateCrc: calculateCrc));

			// Page 2: OpusTags
			builder.Add (Ogg.CreatePage (opusTagsPacket, 1, OggPageFlags.None, calculateCrc: calculateCrc));

			// Page 3: EOS (with granule position for duration calculation)
			var audioData = new byte[10]; // Minimal audio data
			builder.Add (CreatePageWithGranule (audioData, 2, OggPageFlags.EndOfStream, granulePosition, calculateCrc: calculateCrc));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg page with a specific granule position for duration testing.
		/// </summary>
		static byte[] CreatePageWithGranule (byte[] data, uint sequenceNumber, OggPageFlags flags, ulong granulePosition, bool calculateCrc = true)
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Ogg);

			// Version
			builder.Add ((byte)0);

			// Flags
			builder.Add ((byte)flags);

			// Granule position (8 bytes)
			builder.AddUInt64LE (granulePosition);

			// Serial number
			builder.AddUInt32LE (1);

			// Sequence number
			builder.AddUInt32LE (sequenceNumber);

			// CRC placeholder
			builder.AddUInt32LE (0);

			// Segment count and table
			var segments = new byte[] { (byte)data.Length };
			builder.Add ((byte)segments.Length);
			builder.Add (segments);

			// Page data
			builder.Add (data);

			var page = builder.ToArray ();

			// Calculate and insert CRC if requested
			if (calculateCrc) {
				var crc = OggCrc.Calculate (page);
				page[22] = (byte)(crc & 0xFF);
				page[23] = (byte)((crc >> 8) & 0xFF);
				page[24] = (byte)((crc >> 16) & 0xFF);
				page[25] = (byte)((crc >> 24) & 0xFF);
			}

			return page;
		}

		/// <summary>
		/// Creates an Ogg page with OpusHead but wrong version for error testing.
		/// </summary>
		public static byte[] CreateFileWithInvalidVersion (byte version = 0)
		{
			var builder = new BinaryDataBuilder ();

			// Create OpusHead with invalid version
			var headBuilder = new BinaryDataBuilder ();
			headBuilder.Add (TestConstants.Magic.OpusHead);
			headBuilder.Add (version); // Invalid version
			headBuilder.Add ((byte)2); // Channels
			headBuilder.AddUInt16LE (312);
			headBuilder.AddUInt32LE (48000);
			headBuilder.AddUInt16LE (0);
			headBuilder.Add ((byte)0);

			builder.Add (Ogg.CreatePage (headBuilder.ToArray (), 0, OggPageFlags.BeginOfStream));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg page with non-Opus data for error testing (Ogg container but not Opus content).
		/// </summary>
		public static byte[] CreatePageWithNonOpusData (bool calculateCrc = false)
		{
			var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
			return Ogg.CreatePage (data, 0, OggPageFlags.BeginOfStream, calculateCrc: calculateCrc);
		}

		/// <summary>
		/// Creates a complete Ogg Opus file with a specific version number.
		/// </summary>
		/// <remarks>
		/// RFC 7845 requires implementations to accept versions 0-15 (treating as version 1)
		/// and reject versions 16 and above.
		/// </remarks>
		public static byte[] CreateFileWithVersion (byte version)
		{
			var builder = new BinaryDataBuilder ();

			// Create OpusHead with specific version
			var headBuilder = new BinaryDataBuilder ();
			headBuilder.Add (TestConstants.Magic.OpusHead);
			headBuilder.Add (version);
			headBuilder.Add ((byte)2); // Channels
			headBuilder.AddUInt16LE (312); // Pre-skip
			headBuilder.AddUInt32LE (48000); // Input sample rate
			headBuilder.AddUInt16LE (0); // Output gain
			headBuilder.Add ((byte)0); // Channel mapping family

			var opusHeadPacket = headBuilder.ToArray ();
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			var opusTagsPacket = CreateOpusTagsPacket (comment);

			// Page 1: BOS with OpusHead
			builder.Add (Ogg.CreatePage (opusHeadPacket, 0, OggPageFlags.BeginOfStream));

			// Page 2: OpusTags
			builder.Add (Ogg.CreatePage (opusTagsPacket, 1, OggPageFlags.None));

			// Page 3: EOS (minimal audio)
			var audioData = new byte[10];
			builder.Add (CreatePageWithGranuleAndEos (audioData, 2, 480000));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg Opus file with a specific output gain value.
		/// </summary>
		/// <param name="outputGain">Output gain in Q7.8 format (divide by 256 to get dB).</param>
		public static byte[] CreateFileWithOutputGain (short outputGain)
		{
			var builder = new BinaryDataBuilder ();

			// Create OpusHead with specific output gain
			var headBuilder = new BinaryDataBuilder ();
			headBuilder.Add (TestConstants.Magic.OpusHead);
			headBuilder.Add ((byte)1); // Version
			headBuilder.Add ((byte)2); // Channels
			headBuilder.AddUInt16LE (312); // Pre-skip
			headBuilder.AddUInt32LE (48000); // Input sample rate
			headBuilder.AddUInt16LE ((ushort)outputGain); // Output gain (signed but stored as ushort)
			headBuilder.Add ((byte)0); // Channel mapping family

			var opusHeadPacket = headBuilder.ToArray ();
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			var opusTagsPacket = CreateOpusTagsPacket (comment);

			// Page 1: BOS with OpusHead
			builder.Add (Ogg.CreatePage (opusHeadPacket, 0, OggPageFlags.BeginOfStream));

			// Page 2: OpusTags
			builder.Add (Ogg.CreatePage (opusTagsPacket, 1, OggPageFlags.None));

			// Page 3: EOS (minimal audio)
			var audioData = new byte[10];
			builder.Add (CreatePageWithGranuleAndEos (audioData, 2, 480000));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg Opus file with an invalid channel count for error testing.
		/// </summary>
		public static byte[] CreateFileWithInvalidChannels (byte channels)
		{
			var builder = new BinaryDataBuilder ();

			// Create OpusHead with invalid channel count
			var headBuilder = new BinaryDataBuilder ();
			headBuilder.Add (TestConstants.Magic.OpusHead);
			headBuilder.Add ((byte)1); // Version
			headBuilder.Add (channels); // Invalid channel count
			headBuilder.AddUInt16LE (312);
			headBuilder.AddUInt32LE (48000);
			headBuilder.AddUInt16LE (0);
			headBuilder.Add ((byte)0);

			builder.Add (Ogg.CreatePage (headBuilder.ToArray (), 0, OggPageFlags.BeginOfStream));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg Opus file without the BOS flag on the first page for error testing.
		/// </summary>
		public static byte[] CreateFileWithoutBosFlag ()
		{
			var builder = new BinaryDataBuilder ();

			var opusHeadPacket = CreateOpusHeadPacket ();

			// Create page WITHOUT BOS flag (this is invalid per RFC 3533)
			builder.Add (Ogg.CreatePage (opusHeadPacket, 0, OggPageFlags.None)); // Missing BOS flag

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg Opus file with a specific channel mapping family.
		/// </summary>
		/// <remarks>
		/// Per RFC 7845 §5.1.1.2:
		/// - Family 0: Only 1 or 2 channels, no mapping table
		/// - Family 1: 1-8 channels, Vorbis order, mapping table required
		/// - Family 255: Discrete channels, mapping table required
		/// </remarks>
		public static byte[] CreateFileWithChannelMapping (byte channels, byte mappingFamily)
		{
			var builder = new BinaryDataBuilder ();

			var headBuilder = new BinaryDataBuilder ();
			headBuilder.Add (TestConstants.Magic.OpusHead);
			headBuilder.Add ((byte)1); // Version
			headBuilder.Add (channels);
			headBuilder.AddUInt16LE (312); // Pre-skip
			headBuilder.AddUInt32LE (48000); // Input sample rate
			headBuilder.AddUInt16LE (0); // Output gain
			headBuilder.Add (mappingFamily);

			// For mapping families > 0, include the channel mapping table
			if (mappingFamily > 0) {
				// Stream count: assume 1 coupled stream per 2 channels
				var coupledStreams = (byte)(channels / 2);
				var totalStreams = (byte)(coupledStreams + (channels % 2));
				headBuilder.Add (totalStreams); // Stream count
				headBuilder.Add (coupledStreams); // Coupled stream count

				// Channel mapping table (N bytes where N = channels)
				for (var i = 0; i < channels; i++)
					headBuilder.Add ((byte)i);
			}

			var opusHeadPacket = headBuilder.ToArray ();
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			var opusTagsPacket = CreateOpusTagsPacket (comment);

			// Page 1: BOS with OpusHead
			builder.Add (Ogg.CreatePage (opusHeadPacket, 0, OggPageFlags.BeginOfStream));

			// Page 2: OpusTags
			builder.Add (Ogg.CreatePage (opusTagsPacket, 1, OggPageFlags.None));

			// Page 3: EOS
			builder.Add (CreatePageWithGranuleAndEos (new byte[10], 2, 480000));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg Opus file with specific stream count and coupled count values for testing
		/// RFC 7845 §5.1.1.2 validation.
		/// </summary>
		/// <param name="channels">Number of channels.</param>
		/// <param name="mappingFamily">Channel mapping family (1 or 255).</param>
		/// <param name="streamCount">Stream count (N).</param>
		/// <param name="coupledCount">Coupled stream count (M).</param>
		/// <returns>The file data.</returns>
		public static byte[] CreateFileWithStreamCounts (byte channels, byte mappingFamily, byte streamCount, byte coupledCount)
		{
			var builder = new BinaryDataBuilder ();

			var headBuilder = new BinaryDataBuilder ();
			headBuilder.Add (TestConstants.Magic.OpusHead);
			headBuilder.Add ((byte)1); // Version
			headBuilder.Add (channels);
			headBuilder.AddUInt16LE (312); // Pre-skip
			headBuilder.AddUInt32LE (48000); // Input sample rate
			headBuilder.AddUInt16LE (0); // Output gain
			headBuilder.Add (mappingFamily);

			// For mapping families > 0, include the channel mapping table with specified counts
			if (mappingFamily > 0) {
				headBuilder.Add (streamCount); // Stream count
				headBuilder.Add (coupledCount); // Coupled stream count

				// Channel mapping table (N bytes where N = channels)
				for (var i = 0; i < channels; i++)
					headBuilder.Add ((byte)(i % Math.Max (1, (int)streamCount)));
			}

			var opusHeadPacket = headBuilder.ToArray ();
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			var opusTagsPacket = CreateOpusTagsPacket (comment);

			// Page 1: BOS with OpusHead
			builder.Add (Ogg.CreatePage (opusHeadPacket, 0, OggPageFlags.BeginOfStream));

			// Page 2: OpusTags
			builder.Add (Ogg.CreatePage (opusTagsPacket, 1, OggPageFlags.None));

			// Page 3: EOS
			builder.Add (CreatePageWithGranuleAndEos (new byte[10], 2, 480000));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg Opus file with multi-page OpusTags (tags spanning multiple pages).
		/// </summary>
		/// <remarks>
		/// This tests handling of large comment packets that span multiple Ogg pages,
		/// which is common when embedded album art is present.
		/// </remarks>
		public static byte[] CreateFileWithMultiPageOpusTags (string title, string artist, int paddingSize = 300)
		{
			var builder = new BinaryDataBuilder ();

			// Create OpusHead packet
			var opusHeadPacket = CreateOpusHeadPacket ();

			// Create a VorbisComment with enough padding to span multiple pages
			// A typical page holds ~250 segments × ~255 bytes = ~64KB
			// We'll create a comment that's larger than that
			var comment = new TagLibSharp2.Xiph.VorbisComment (TestConstants.Vendors.TagLibSharp2);
			if (!string.IsNullOrEmpty (title))
				comment.Title = title;
			if (!string.IsNullOrEmpty (artist))
				comment.Artist = artist;

			// Add large padding comment to force multi-page
			var padding = new string ('X', paddingSize);
			comment.SetValue ("PADDING", padding);

			var opusTagsPacket = CreateOpusTagsPacket (comment);

			// Page 1: BOS with OpusHead
			builder.Add (Ogg.CreatePage (opusHeadPacket, 0, OggPageFlags.BeginOfStream));

			// Pages 2+: OpusTags (may span multiple pages)
			// Split large packet across pages with proper continuation flags
			var pageSequence = 1u;
			var packetOffset = 0;
			const int maxPageDataSize = 255 * 255; // Max segments (255) × max segment size (255)

			while (packetOffset < opusTagsPacket.Length) {
				var isFirstPage = packetOffset == 0;
				var remainingBytes = opusTagsPacket.Length - packetOffset;
				var pageDataSize = Math.Min (remainingBytes, maxPageDataSize);
				var isLastPacketPage = packetOffset + pageDataSize >= opusTagsPacket.Length;

				// Extract this page's portion of the packet
				var pageData = new byte[pageDataSize];
				Array.Copy (opusTagsPacket, packetOffset, pageData, 0, pageDataSize);

				// Set continuation flag if this isn't the first page of the packet
				var flags = isFirstPage ? OggPageFlags.None : OggPageFlags.Continuation;

				builder.Add (CreateMultiPagePacketPage (pageData, pageSequence, flags, !isLastPacketPage));

				pageSequence++;
				packetOffset += pageDataSize;
			}

			// Final page: EOS with audio data
			var audioData = new byte[10];
			builder.Add (CreatePageWithGranuleAndEos (audioData, pageSequence, 480000));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg page for a multi-page packet with proper segment table.
		/// </summary>
		static byte[] CreateMultiPagePacketPage (byte[] data, uint sequenceNumber, OggPageFlags flags, bool packetContinues)
		{
			var builder = new BinaryDataBuilder ();

			// Build segment table
			var segments = new List<byte> ();
			var remaining = data.Length;
			while (remaining >= 255) {
				segments.Add (255);
				remaining -= 255;
			}
			// If packet continues to next page, last segment is 255
			// If packet ends on this page, last segment is < 255 (or 0 for exact multiple)
			if (packetContinues) {
				// Packet continues - ensure we end with 255 if remaining is 0
				if (remaining == 0 && segments.Count == 0)
					segments.Add (0);
				else if (remaining > 0)
					segments.Add ((byte)remaining);
				// Note: if remaining=0 and segments.Count>0, last segment is already 255
			} else {
				// Packet ends here
				segments.Add ((byte)remaining);
			}

			// Magic
			builder.Add (TestConstants.Magic.Ogg);

			// Version
			builder.Add ((byte)0);

			// Flags
			builder.Add ((byte)flags);

			// Granule position (0 for header pages)
			builder.AddUInt64LE (0);

			// Serial number
			builder.AddUInt32LE (1);

			// Sequence number
			builder.AddUInt32LE (sequenceNumber);

			// CRC placeholder
			builder.AddUInt32LE (0);

			// Segment count and table
			builder.Add ((byte)segments.Count);
			builder.Add (segments.ToArray ());

			// Page data
			builder.Add (data);

			var page = builder.ToArray ();

			// Calculate and insert CRC
			var crc = OggCrc.Calculate (page);
			page[22] = (byte)(crc & 0xFF);
			page[23] = (byte)((crc >> 8) & 0xFF);
			page[24] = (byte)((crc >> 16) & 0xFF);
			page[25] = (byte)((crc >> 24) & 0xFF);

			return page;
		}

		/// <summary>
		/// Creates an Ogg page with granule position and EOS flag for complete file testing.
		/// </summary>
		static byte[] CreatePageWithGranuleAndEos (byte[] data, uint sequenceNumber, ulong granulePosition)
		{
			var builder = new BinaryDataBuilder ();

			// Magic
			builder.Add (TestConstants.Magic.Ogg);

			// Version
			builder.Add ((byte)0);

			// Flags: EOS
			builder.Add ((byte)OggPageFlags.EndOfStream);

			// Granule position (8 bytes)
			builder.AddUInt64LE (granulePosition);

			// Serial number
			builder.AddUInt32LE (1);

			// Sequence number
			builder.AddUInt32LE (sequenceNumber);

			// CRC placeholder
			builder.AddUInt32LE (0);

			// Segment count and table
			var segments = new byte[] { (byte)data.Length };
			builder.Add ((byte)segments.Length);
			builder.Add (segments);

			// Page data
			builder.Add (data);

			var page = builder.ToArray ();

			// Calculate and insert CRC
			var crc = OggCrc.Calculate (page);
			page[22] = (byte)(crc & 0xFF);
			page[23] = (byte)((crc >> 8) & 0xFF);
			page[24] = (byte)((crc >> 16) & 0xFF);
			page[25] = (byte)((crc >> 24) & 0xFF);

			return page;
		}
	}

	/// <summary>
	/// Builders for Vorbis Comment test data.
	/// </summary>
	public static class VorbisComment
	{
		/// <summary>
		/// Creates a minimal valid Vorbis Comment.
		/// </summary>
		public static byte[] CreateMinimal (string vendor = "")
		{
			var vendorBytes = Encoding.UTF8.GetBytes (vendor);
			var builder = new BinaryDataBuilder ();

			// Vendor length + string
			builder.AddUInt32LE ((uint)vendorBytes.Length);
			builder.Add (vendorBytes);

			// Field count = 0
			builder.AddUInt32LE (0);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a Vorbis Comment with the given fields.
		/// </summary>
		public static byte[] CreateWithFields (string vendor, params (string key, string value)[] fields)
		{
			var vendorBytes = Encoding.UTF8.GetBytes (vendor);
			var builder = new BinaryDataBuilder ();

			// Vendor length + string
			builder.AddUInt32LE ((uint)vendorBytes.Length);
			builder.Add (vendorBytes);

			// Field count
			builder.AddUInt32LE ((uint)fields.Length);

			// Fields
			foreach (var (key, value) in fields) {
				var field = $"{key}={value}";
				var fieldBytes = Encoding.UTF8.GetBytes (field);
				builder.AddUInt32LE ((uint)fieldBytes.Length);
				builder.Add (fieldBytes);
			}

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates raw Vorbis Comment content (vendor + fields) for embedding in FLAC metadata blocks.
		/// </summary>
		public static byte[] CreateCommentsContent (string? title = null, string? artist = null, string? album = null)
		{
			var fields = new List<(string, string)> ();
			if (!string.IsNullOrEmpty (title))
				fields.Add (("TITLE", title!));
			if (!string.IsNullOrEmpty (artist))
				fields.Add (("ARTIST", artist!));
			if (!string.IsNullOrEmpty (album))
				fields.Add (("ALBUM", album!));

			return CreateWithFields ("TagLibSharp2", [.. fields]);
		}
	}

	/// <summary>
	/// Builders for RIFF/WAV file test data.
	/// </summary>
	public static class Wav
	{
		/// <summary>
		/// Creates a minimal valid WAV file header.
		/// </summary>
		public static byte[] CreateMinimal (
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			int bitsPerSample = TestConstants.AudioProperties.BitsPerSample16)
		{
			var builder = new BinaryDataBuilder ();

			// RIFF header
			builder.Add (TestConstants.Magic.Riff);
			builder.AddUInt32LE (36); // File size - 8 (placeholder)
			builder.Add (TestConstants.Magic.Wave);

			// fmt chunk
			builder.AddStringLatin1 (TestConstants.Riff.FormatChunkId);
			builder.AddUInt32LE (16); // Chunk size
			builder.AddUInt16LE (TestConstants.Riff.FormatPcm);
			builder.AddUInt16LE ((ushort)channels);
			builder.AddUInt32LE ((uint)sampleRate);
			builder.AddUInt32LE ((uint)(sampleRate * channels * bitsPerSample / 8)); // Byte rate
			builder.AddUInt16LE ((ushort)(channels * bitsPerSample / 8)); // Block align
			builder.AddUInt16LE ((ushort)bitsPerSample);

			// data chunk (empty)
			builder.AddStringLatin1 (TestConstants.Riff.DataChunkId);
			builder.AddUInt32LE (0);

			return builder.ToArray ();
		}
	}

	/// <summary>
	/// Builders for AIFF file test data.
	/// </summary>
	public static class Aiff
	{
		/// <summary>
		/// Creates a minimal valid AIFF file header.
		/// </summary>
		public static byte[] CreateMinimal (
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			int bitsPerSample = TestConstants.AudioProperties.BitsPerSample16)
		{
			var builder = new BinaryDataBuilder ();

			// FORM header
			builder.Add (TestConstants.Magic.Form);
			builder.AddUInt32BE (30); // File size - 8 (placeholder)
			builder.Add (TestConstants.Magic.Aiff);

			// COMM chunk
			builder.AddStringLatin1 (TestConstants.Aiff.CommonChunkId);
			builder.AddUInt32BE (TestConstants.Aiff.CommChunkSize);
			builder.AddUInt16BE ((ushort)channels);
			builder.AddUInt32BE (0); // Sample frames
			builder.AddUInt16BE ((ushort)bitsPerSample);
			builder.Add (ConvertToExtended (sampleRate));

			// SSND chunk (empty)
			builder.AddStringLatin1 (TestConstants.Aiff.SoundDataChunkId);
			builder.AddUInt32BE (8);
			builder.AddUInt32BE (0); // Offset
			builder.AddUInt32BE (0); // Block size

			return builder.ToArray ();
		}

		/// <summary>
		/// Converts a sample rate to 80-bit extended precision format.
		/// </summary>
		static byte[] ConvertToExtended (int sampleRate)
		{
			// Simplified conversion for common sample rates
			var result = new byte[10];
			if (sampleRate == 44100) {
				result[0] = 0x40; result[1] = 0x0E;
				result[2] = 0xAC; result[3] = 0x44;
			} else if (sampleRate == 48000) {
				result[0] = 0x40; result[1] = 0x0E;
				result[2] = 0xBB; result[3] = 0x80;
			}
			return result;
		}
	}

	/// <summary>
	/// Builders for MP4/M4A file test data.
	/// </summary>
	/// <remarks>
	/// MP4 uses ISO Base Media File Format (ISO 14496-12).
	/// Structure: ftyp + moov (with metadata in udta/meta/ilst) + mdat
	/// </remarks>
	public static class Mp4
	{
		/// <summary>
		/// Creates a basic MP4 box with type and data.
		/// </summary>
		/// <param name="boxType">4-character box type (e.g., "ftyp", "moov").</param>
		/// <param name="data">Box data content.</param>
		/// <returns>Complete box bytes (8-byte header + data).</returns>
		public static byte[] CreateBox (string boxType, byte[] data)
		{
			var builder = new BinaryDataBuilder ();
			var size = 8 + data.Length;

			// Size (32-bit big-endian)
			builder.AddUInt32BE ((uint)size);

			// Type (4-character code)
			builder.AddStringLatin1 (boxType);

			// Data
			builder.Add (data);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 box with extended size (64-bit).
		/// </summary>
		public static byte[] CreateExtendedSizeBox (string boxType, byte[] data)
		{
			var builder = new BinaryDataBuilder ();
			var size = 16L + data.Length;

			// Size = 1 (indicates extended size follows)
			builder.AddUInt32BE (1);

			// Type
			builder.AddStringLatin1 (boxType);

			// Extended size (64-bit)
			builder.AddUInt64BE ((ulong)size);

			// Data
			builder.Add (data);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 box with size=0 (extends to EOF).
		/// </summary>
		public static byte[] CreateBoxWithSizeZero (string boxType, byte[] data)
		{
			var builder = new BinaryDataBuilder ();

			// Size = 0 (extends to EOF)
			builder.AddUInt32BE (0);

			// Type
			builder.AddStringLatin1 (boxType);

			// Data
			builder.Add (data);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a minimal valid M4A file with specified codec.
		/// </summary>
		public static byte[] CreateMinimalM4a (Mp4CodecType codec)
		{
			var builder = new BinaryDataBuilder ();

			// ftyp box
			builder.Add (CreateFtyp ("M4A "));

			// moov box with minimal structure
			var moovContent = CreateMinimalMoov (codec);
			builder.Add (CreateBox ("moov", moovContent));

			// mdat box (empty)
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an ftyp box with specified major brand.
		/// </summary>
		static byte[] CreateFtyp (string majorBrand)
		{
			var builder = new BinaryDataBuilder ();

			// Major brand (4 bytes)
			builder.AddStringLatin1 (majorBrand.PadRight (4));

			// Minor version
			builder.AddUInt32BE (0);

			// Compatible brands (just major brand for simplicity)
			builder.AddStringLatin1 (majorBrand.PadRight (4));

			return CreateBox ("ftyp", builder.ToArray ());
		}

		/// <summary>
		/// Creates minimal moov box content.
		/// </summary>
		static byte[] CreateMinimalMoov (Mp4CodecType codec)
		{
			var builder = new BinaryDataBuilder ();

			// mvhd box
			builder.Add (CreateMvhd (1000, 44100)); // 1 second duration

			// trak box
			builder.Add (CreateTrak (codec));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates mvhd (movie header) box.
		/// </summary>
		static byte[] CreateMvhd (ulong duration, uint timescale)
		{
			var builder = new BinaryDataBuilder ();

			// Version (1 byte) + flags (3 bytes)
			builder.AddUInt32BE (0);

			// Creation time
			builder.AddUInt32BE (0);

			// Modification time
			builder.AddUInt32BE (0);

			// Timescale
			builder.AddUInt32BE (timescale);

			// Duration
			builder.AddUInt32BE ((uint)duration);

			// Rate (1.0 = 0x00010000)
			builder.AddUInt32BE (0x00010000);

			// Volume (1.0 = 0x0100)
			builder.AddUInt16BE (0x0100);

			// Reserved (10 bytes)
			builder.AddZeros (10);

			// Matrix (9 x 32-bit values - unity matrix)
			builder.AddUInt32BE (0x00010000); builder.AddZeros (4); builder.AddZeros (4);
			builder.AddZeros (4); builder.AddUInt32BE (0x00010000); builder.AddZeros (4);
			builder.AddZeros (4); builder.AddZeros (4); builder.AddUInt32BE (0x40000000);

			// Pre-defined (24 bytes)
			builder.AddZeros (24);

			// Next track ID
			builder.AddUInt32BE (2);

			return CreateBox ("mvhd", builder.ToArray ());
		}

		/// <summary>
		/// Creates trak (track) box.
		/// </summary>
		static byte[] CreateTrak (Mp4CodecType codec)
		{
			var builder = new BinaryDataBuilder ();

			// mdia box
			builder.Add (CreateMdia (codec));

			return CreateBox ("trak", builder.ToArray ());
		}

		/// <summary>
		/// Creates mdia (media) box.
		/// </summary>
		static byte[] CreateMdia (Mp4CodecType codec)
		{
			var builder = new BinaryDataBuilder ();

			// mdhd box
			builder.Add (CreateMdhd ());

			// hdlr box
			builder.Add (CreateHdlr ());

			// minf box
			builder.Add (CreateMinf (codec));

			return CreateBox ("mdia", builder.ToArray ());
		}

		/// <summary>
		/// Creates mdhd (media header) box.
		/// </summary>
		static byte[] CreateMdhd ()
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// Creation time
			builder.AddUInt32BE (0);

			// Modification time
			builder.AddUInt32BE (0);

			// Timescale
			builder.AddUInt32BE (44100);

			// Duration
			builder.AddUInt32BE (44100); // 1 second

			// Language (ISO 639-2/T, 3 x 5 bits, packed into 16 bits)
			builder.AddUInt16BE (0x55C4); // "und" (undetermined)

			// Pre-defined
			builder.AddUInt16BE (0);

			return CreateBox ("mdhd", builder.ToArray ());
		}

		/// <summary>
		/// Creates hdlr (handler) box.
		/// </summary>
		static byte[] CreateHdlr ()
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// Pre-defined
			builder.AddUInt32BE (0);

			// Handler type
			builder.AddStringLatin1 ("soun"); // Sound handler

			// Reserved (12 bytes)
			builder.AddZeros (12);

			// Name (null-terminated)
			builder.Add ((byte)0);

			return CreateBox ("hdlr", builder.ToArray ());
		}

		/// <summary>
		/// Creates minf (media information) box.
		/// </summary>
		static byte[] CreateMinf (Mp4CodecType codec)
		{
			var builder = new BinaryDataBuilder ();

			// smhd box (sound media header)
			builder.Add (CreateSmhd ());

			// dinf box (data information)
			builder.Add (CreateDinf ());

			// stbl box (sample table)
			builder.Add (CreateStbl (codec));

			return CreateBox ("minf", builder.ToArray ());
		}

		/// <summary>
		/// Creates smhd (sound media header) box.
		/// </summary>
		static byte[] CreateSmhd ()
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// Balance
			builder.AddUInt16BE (0);

			// Reserved
			builder.AddUInt16BE (0);

			return CreateBox ("smhd", builder.ToArray ());
		}

		/// <summary>
		/// Creates dinf (data information) box.
		/// </summary>
		static byte[] CreateDinf ()
		{
			var builder = new BinaryDataBuilder ();

			// dref box
			var drefContent = new BinaryDataBuilder ();
			drefContent.AddUInt32BE (0); // Version + flags
			drefContent.AddUInt32BE (1); // Entry count

			// url entry
			var urlContent = new BinaryDataBuilder ();
			urlContent.AddUInt32BE (1); // Version=0, flags=1 (self-contained)
			drefContent.Add (CreateBox ("url ", urlContent.ToArray ()));

			builder.Add (CreateBox ("dref", drefContent.ToArray ()));

			return CreateBox ("dinf", builder.ToArray ());
		}

		/// <summary>
		/// Creates stbl (sample table) box.
		/// </summary>
		static byte[] CreateStbl (Mp4CodecType codec)
		{
			var builder = new BinaryDataBuilder ();

			// stsd box (sample description)
			builder.Add (CreateStsd (codec));

			// stts box (time-to-sample)
			var sttsContent = new BinaryDataBuilder ();
			sttsContent.AddUInt32BE (0); // Version + flags
			sttsContent.AddUInt32BE (0); // Entry count
			builder.Add (CreateBox ("stts", sttsContent.ToArray ()));

			// stsc box (sample-to-chunk)
			var stscContent = new BinaryDataBuilder ();
			stscContent.AddUInt32BE (0);
			stscContent.AddUInt32BE (0);
			builder.Add (CreateBox ("stsc", stscContent.ToArray ()));

			// stsz box (sample size)
			var stszContent = new BinaryDataBuilder ();
			stszContent.AddUInt32BE (0);
			stszContent.AddUInt32BE (0); // Sample size
			stszContent.AddUInt32BE (0); // Sample count
			builder.Add (CreateBox ("stsz", stszContent.ToArray ()));

			// stco box (chunk offset)
			var stcoContent = new BinaryDataBuilder ();
			stcoContent.AddUInt32BE (0);
			stcoContent.AddUInt32BE (0);
			builder.Add (CreateBox ("stco", stcoContent.ToArray ()));

			return CreateBox ("stbl", builder.ToArray ());
		}

		/// <summary>
		/// Creates stsd (sample description) box.
		/// </summary>
		static byte[] CreateStsd (Mp4CodecType codec)
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// Entry count
			builder.AddUInt32BE (1);

			// Sample entry
			if (codec == Mp4CodecType.Aac)
				builder.Add (CreateMp4aSampleEntry ());
			else if (codec == Mp4CodecType.Alac)
				builder.Add (CreateAlacSampleEntry ());

			return CreateBox ("stsd", builder.ToArray ());
		}

		/// <summary>
		/// Creates mp4a (AAC) sample entry.
		/// </summary>
		static byte[] CreateMp4aSampleEntry ()
		{
			var builder = new BinaryDataBuilder ();

			// Reserved (6 bytes)
			builder.AddZeros (6);

			// Data reference index
			builder.AddUInt16BE (1);

			// Reserved (8 bytes)
			builder.AddZeros (8);

			// Channel count
			builder.AddUInt16BE (2);

			// Sample size
			builder.AddUInt16BE (16);

			// Pre-defined
			builder.AddUInt16BE (0);

			// Reserved
			builder.AddUInt16BE (0);

			// Sample rate (16.16 fixed point)
			builder.AddUInt32BE (unchecked((uint)(44100 << 16)));

			// esds box (elementary stream descriptor)
			builder.Add (CreateEsds ());

			return CreateBox ("mp4a", builder.ToArray ());
		}

		/// <summary>
		/// Creates esds (elementary stream descriptor) box for AAC.
		/// </summary>
		static byte[] CreateEsds ()
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// ES_Descriptor tag
			builder.Add ((byte)0x03);
			builder.Add ((byte)0x19); // Size

			// ES_ID
			builder.AddUInt16BE (0);

			// Flags
			builder.Add ((byte)0x00);

			// DecoderConfigDescriptor tag
			builder.Add ((byte)0x04);
			builder.Add ((byte)0x11); // Size

			// Object type (AAC LC = 0x40)
			builder.Add ((byte)0x40);

			// Stream type
			builder.Add ((byte)0x15);

			// Buffer size DB
			builder.Add ((byte)0x00);
			builder.AddUInt16BE (0);

			// Max bitrate
			builder.AddUInt32BE (128000);

			// Avg bitrate
			builder.AddUInt32BE (128000);

			// DecoderSpecificInfo tag
			builder.Add ((byte)0x05);
			builder.Add ((byte)0x02); // Size

			// AAC config (44100 Hz, stereo)
			builder.AddUInt16BE (0x1190);

			// SLConfigDescriptor tag
			builder.Add ((byte)0x06);
			builder.Add ((byte)0x01);
			builder.Add ((byte)0x02);

			return CreateBox ("esds", builder.ToArray ());
		}

		/// <summary>
		/// Creates alac sample entry.
		/// </summary>
		static byte[] CreateAlacSampleEntry ()
		{
			var builder = new BinaryDataBuilder ();

			// Reserved (6 bytes)
			builder.AddZeros (6);

			// Data reference index
			builder.AddUInt16BE (1);

			// Reserved (8 bytes)
			builder.AddZeros (8);

			// Channel count
			builder.AddUInt16BE (2);

			// Sample size
			builder.AddUInt16BE (16);

			// Pre-defined
			builder.AddUInt16BE (0);

			// Reserved
			builder.AddUInt16BE (0);

			// Sample rate (16.16 fixed point)
			builder.AddUInt32BE (unchecked((uint)(44100 << 16)));

			// alac box (magic cookie)
			builder.Add (CreateAlacBox ());

			return CreateBox ("alac", builder.ToArray ());
		}

		/// <summary>
		/// Creates alac magic cookie box.
		/// </summary>
		static byte[] CreateAlacBox ()
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// Frame length
			builder.AddUInt32BE (4096);

			// Compatible version
			builder.Add ((byte)0);

			// Bit depth
			builder.Add ((byte)16);

			// Rice history mult / Rice initial history / Rice parameter limit
			builder.Add ((byte)40);
			builder.Add ((byte)10);
			builder.Add ((byte)14);

			// Channels
			builder.Add ((byte)2);

			// Max run
			builder.AddUInt16BE (255);

			// Max frame bytes
			builder.AddUInt32BE (0);

			// Average bitrate
			builder.AddUInt32BE (0);

			// Sample rate
			builder.AddUInt32BE (44100);

			return CreateBox ("alac", builder.ToArray ());
		}

		/// <summary>
		/// Creates an MP4 file with specified ftyp brand.
		/// </summary>
		public static byte[] CreateWithFtyp (string brand)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp (brand));
			builder.Add (CreateBox ("moov", CreateMinimalMoov (Mp4CodecType.Aac)));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 file without moov box.
		/// </summary>
		public static byte[] CreateWithoutMoov ()
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 file without ilst (metadata) box.
		/// </summary>
		public static byte[] CreateWithoutIlst ()
		{
			return CreateMinimalM4a (Mp4CodecType.Aac);
		}

		/// <summary>
		/// Creates an MP4 file with metadata.
		/// </summary>
		public static byte[] CreateWithMetadata (string? title = null, string? artist = null, string? album = null)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			// moov with udta/meta/ilst
			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (44100, 44100));
			moovContent.Add (CreateTrak (Mp4CodecType.Aac));

			// udta box
			var udtaContent = new BinaryDataBuilder ();

			// meta box
			var metaContent = new BinaryDataBuilder ();
			metaContent.AddUInt32BE (0); // Version + flags

			// hdlr box
			metaContent.Add (CreateMetaHdlr ());

			// ilst box
			var ilstContent = new BinaryDataBuilder ();
			if (title != null)
				ilstContent.Add (CreateTextAtom ("©nam", title));
			if (artist != null)
				ilstContent.Add (CreateTextAtom ("©ART", artist));
			if (album != null)
				ilstContent.Add (CreateTextAtom ("©alb", album));

			metaContent.Add (CreateBox ("ilst", ilstContent.ToArray ()));
			udtaContent.Add (CreateBox ("meta", metaContent.ToArray ()));
			moovContent.Add (CreateBox ("udta", udtaContent.ToArray ()));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates hdlr box for meta.
		/// </summary>
		static byte[] CreateMetaHdlr ()
		{
			var builder = new BinaryDataBuilder ();

			builder.AddUInt32BE (0); // Version + flags
			builder.AddUInt32BE (0); // Pre-defined
			builder.AddStringLatin1 ("mdir");
			builder.AddStringLatin1 ("appl");
			builder.AddZeros (8);
			builder.Add ((byte)0);

			return CreateBox ("hdlr", builder.ToArray ());
		}

		/// <summary>
		/// Creates a text atom (like ©nam, ©ART).
		/// </summary>
		static byte[] CreateTextAtom (string atomName, string value)
		{
			var builder = new BinaryDataBuilder ();

			// data box
			var dataContent = new BinaryDataBuilder ();
			dataContent.AddUInt32BE (1); // Version=0, flags=1 (text)
			dataContent.AddUInt32BE (0); // Reserved
			dataContent.Add (Encoding.UTF8.GetBytes (value));

			builder.Add (CreateBox ("data", dataContent.ToArray ()));

			return CreateBox (atomName, builder.ToArray ());
		}

		/// <summary>
		/// Creates an MP4 file with a single metadata atom.
		/// </summary>
		public static byte[] CreateWithAtom (string atomName, string value)
		{
			if (atomName == "©nam")
				return CreateWithMetadata (title: value);
			if (atomName == "©ART")
				return CreateWithMetadata (artist: value);
			if (atomName == "©alb")
				return CreateWithMetadata (album: value);

			return CreateWithMetadata ();
		}

		/// <summary>
		/// Creates an MP4 file with track number.
		/// </summary>
		public static byte[] CreateWithTrackNumber (uint track, uint total)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (44100, 44100));
			moovContent.Add (CreateTrak (Mp4CodecType.Aac));

			var udtaContent = new BinaryDataBuilder ();
			var metaContent = new BinaryDataBuilder ();
			metaContent.AddUInt32BE (0);
			metaContent.Add (CreateMetaHdlr ());

			var ilstContent = new BinaryDataBuilder ();
			ilstContent.Add (CreateTrackAtom (track, total));

			metaContent.Add (CreateBox ("ilst", ilstContent.ToArray ()));
			udtaContent.Add (CreateBox ("meta", metaContent.ToArray ()));
			moovContent.Add (CreateBox ("udta", udtaContent.ToArray ()));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates trkn atom.
		/// </summary>
		static byte[] CreateTrackAtom (uint track, uint total)
		{
			var builder = new BinaryDataBuilder ();

			var dataContent = new BinaryDataBuilder ();
			dataContent.AddUInt32BE (0); // Version=0, flags=0
			dataContent.AddUInt32BE (0); // Reserved
			dataContent.AddUInt16BE (0); // Padding
			dataContent.AddUInt16BE ((ushort)track);
			dataContent.AddUInt16BE ((ushort)total);
			dataContent.AddUInt16BE (0); // Padding

			builder.Add (CreateBox ("data", dataContent.ToArray ()));

			return CreateBox ("trkn", builder.ToArray ());
		}

		/// <summary>
		/// Creates an MP4 file with disc number.
		/// </summary>
		public static byte[] CreateWithDiscNumber (uint disc, uint total)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (44100, 44100));
			moovContent.Add (CreateTrak (Mp4CodecType.Aac));

			var udtaContent = new BinaryDataBuilder ();
			var metaContent = new BinaryDataBuilder ();
			metaContent.AddUInt32BE (0);
			metaContent.Add (CreateMetaHdlr ());

			var ilstContent = new BinaryDataBuilder ();
			ilstContent.Add (CreateDiscAtom (disc, total));

			metaContent.Add (CreateBox ("ilst", ilstContent.ToArray ()));
			udtaContent.Add (CreateBox ("meta", metaContent.ToArray ()));
			moovContent.Add (CreateBox ("udta", udtaContent.ToArray ()));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates disk atom.
		/// </summary>
		static byte[] CreateDiscAtom (uint disc, uint total)
		{
			var builder = new BinaryDataBuilder ();

			var dataContent = new BinaryDataBuilder ();
			dataContent.AddUInt32BE (0);
			dataContent.AddUInt32BE (0);
			dataContent.AddUInt16BE (0);
			dataContent.AddUInt16BE ((ushort)disc);
			dataContent.AddUInt16BE ((ushort)total);

			builder.Add (CreateBox ("data", dataContent.ToArray ()));

			return CreateBox ("disk", builder.ToArray ());
		}

		/// <summary>
		/// Creates an MP4 file with cover art.
		/// </summary>
		public static byte[] CreateWithCoverArt (byte[] imageData, Mp4PictureType type)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (44100, 44100));
			moovContent.Add (CreateTrak (Mp4CodecType.Aac));

			var udtaContent = new BinaryDataBuilder ();
			var metaContent = new BinaryDataBuilder ();
			metaContent.AddUInt32BE (0);
			metaContent.Add (CreateMetaHdlr ());

			var ilstContent = new BinaryDataBuilder ();
			ilstContent.Add (CreateCovrAtom (imageData, type));

			metaContent.Add (CreateBox ("ilst", ilstContent.ToArray ()));
			udtaContent.Add (CreateBox ("meta", metaContent.ToArray ()));
			moovContent.Add (CreateBox ("udta", udtaContent.ToArray ()));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates covr atom.
		/// </summary>
		static byte[] CreateCovrAtom (byte[] imageData, Mp4PictureType type)
		{
			var builder = new BinaryDataBuilder ();

			var dataContent = new BinaryDataBuilder ();
			var flags = type == Mp4PictureType.Jpeg ? 13u : 14u;
			dataContent.AddUInt32BE (flags); // flags: 13=JPEG, 14=PNG
			dataContent.AddUInt32BE (0);
			dataContent.Add (imageData);

			builder.Add (CreateBox ("data", dataContent.ToArray ()));

			return CreateBox ("covr", builder.ToArray ());
		}

		/// <summary>
		/// Creates an MP4 file with freeform tag.
		/// </summary>
		public static byte[] CreateWithFreeformTag (string domain, string name, string value)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (44100, 44100));
			moovContent.Add (CreateTrak (Mp4CodecType.Aac));

			var udtaContent = new BinaryDataBuilder ();
			var metaContent = new BinaryDataBuilder ();
			metaContent.AddUInt32BE (0);
			metaContent.Add (CreateMetaHdlr ());

			var ilstContent = new BinaryDataBuilder ();
			ilstContent.Add (CreateFreeformAtom (domain, name, value));

			metaContent.Add (CreateBox ("ilst", ilstContent.ToArray ()));
			udtaContent.Add (CreateBox ("meta", metaContent.ToArray ()));
			moovContent.Add (CreateBox ("udta", udtaContent.ToArray ()));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates ---- (freeform) atom.
		/// </summary>
		static byte[] CreateFreeformAtom (string domain, string name, string value)
		{
			var builder = new BinaryDataBuilder ();

			// mean box
			var meanContent = new BinaryDataBuilder ();
			meanContent.AddUInt32BE (0);
			meanContent.Add (Encoding.UTF8.GetBytes (domain));
			builder.Add (CreateBox ("mean", meanContent.ToArray ()));

			// name box
			var nameContent = new BinaryDataBuilder ();
			nameContent.AddUInt32BE (0);
			nameContent.Add (Encoding.UTF8.GetBytes (name));
			builder.Add (CreateBox ("name", nameContent.ToArray ()));

			// data box
			var dataContent = new BinaryDataBuilder ();
			dataContent.AddUInt32BE (1); // Text
			dataContent.AddUInt32BE (0);
			dataContent.Add (Encoding.UTF8.GetBytes (value));
			builder.Add (CreateBox ("data", dataContent.ToArray ()));

			return CreateBox ("----", builder.ToArray ());
		}

		/// <summary>
		/// Creates an MP4 file with audio data.
		/// </summary>
		public static byte[] CreateWithAudioData (byte[] audioData)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));
			builder.Add (CreateBox ("moov", CreateMinimalMoov (Mp4CodecType.Aac)));
			builder.Add (CreateBox ("mdat", audioData));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 file with specified duration.
		/// </summary>
		public static byte[] CreateWithDuration (TimeSpan duration)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var timescale = 44100u;
			var durationUnits = (ulong)(duration.TotalSeconds * timescale);

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (durationUnits, timescale));
			moovContent.Add (CreateTrak (Mp4CodecType.Aac));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 file with audio properties.
		/// </summary>
		public static byte[] CreateWithAudioProperties (Mp4CodecType codec, int sampleRate, int channels, int bitrate)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var timescale = (uint)sampleRate;
			var duration = timescale; // 1 second

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (duration, timescale));
			moovContent.Add (CreateTrakWithProperties (codec, sampleRate, channels, 16, bitrate));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 file with invalid timescale (0).
		/// </summary>
		public static byte[] CreateWithInvalidTimescale ()
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (44100, 0)); // Invalid: timescale=0
			moovContent.Add (CreateTrak (Mp4CodecType.Aac));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 file without mdia box.
		/// </summary>
		public static byte[] CreateWithoutMdia ()
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (44100, 44100));

			// trak without mdia
			var trakContent = new byte[4];
			moovContent.Add (CreateBox ("trak", trakContent));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an MP4 file with bits per sample.
		/// </summary>
		public static byte[] CreateWithBitsPerSample (Mp4CodecType codec, int bitsPerSample)
		{
			var builder = new BinaryDataBuilder ();

			builder.Add (CreateFtyp ("M4A "));

			var timescale = 44100u;
			var duration = timescale; // 1 second

			var moovContent = new BinaryDataBuilder ();
			moovContent.Add (CreateMvhd (duration, timescale));
			moovContent.Add (CreateTrakWithProperties (codec, 44100, 2, bitsPerSample, 0));

			builder.Add (CreateBox ("moov", moovContent.ToArray ()));
			builder.Add (CreateBox ("mdat", new byte[4]));

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a trak box with customizable audio properties.
		/// </summary>
		static byte[] CreateTrakWithProperties (Mp4CodecType codec, int sampleRate, int channels, int bitsPerSample, int bitrate)
		{
			var builder = new BinaryDataBuilder ();

			// mdia box with custom properties
			builder.Add (CreateMdiaWithProperties (codec, sampleRate, channels, bitsPerSample, bitrate));

			return CreateBox ("trak", builder.ToArray ());
		}

		/// <summary>
		/// Creates an mdia box with custom audio properties.
		/// </summary>
		static byte[] CreateMdiaWithProperties (Mp4CodecType codec, int sampleRate, int channels, int bitsPerSample, int bitrate)
		{
			var builder = new BinaryDataBuilder ();

			// mdhd box with custom timescale
			builder.Add (CreateMdhdWithTimescale ((uint)sampleRate));

			// hdlr box
			builder.Add (CreateHdlr ());

			// minf box with custom properties
			builder.Add (CreateMinfWithProperties (codec, sampleRate, channels, bitsPerSample, bitrate));

			return CreateBox ("mdia", builder.ToArray ());
		}

		/// <summary>
		/// Creates an mdhd box with custom timescale.
		/// </summary>
		static byte[] CreateMdhdWithTimescale (uint timescale)
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// Creation time
			builder.AddUInt32BE (0);

			// Modification time
			builder.AddUInt32BE (0);

			// Timescale
			builder.AddUInt32BE (timescale);

			// Duration (1 second)
			builder.AddUInt32BE (timescale);

			// Language
			builder.AddUInt16BE (0x55C4);

			// Pre-defined
			builder.AddUInt16BE (0);

			return CreateBox ("mdhd", builder.ToArray ());
		}

		/// <summary>
		/// Creates a minf box with custom audio properties.
		/// </summary>
		static byte[] CreateMinfWithProperties (Mp4CodecType codec, int sampleRate, int channels, int bitsPerSample, int bitrate)
		{
			var builder = new BinaryDataBuilder ();

			// smhd box
			builder.Add (CreateSmhd ());

			// dinf box
			builder.Add (CreateDinf ());

			// stbl box with custom properties
			builder.Add (CreateStblWithProperties (codec, sampleRate, channels, bitsPerSample, bitrate));

			return CreateBox ("minf", builder.ToArray ());
		}

		/// <summary>
		/// Creates an stbl box with custom audio properties.
		/// </summary>
		static byte[] CreateStblWithProperties (Mp4CodecType codec, int sampleRate, int channels, int bitsPerSample, int bitrate)
		{
			var builder = new BinaryDataBuilder ();

			// stsd box with custom properties
			builder.Add (CreateStsdWithProperties (codec, sampleRate, channels, bitsPerSample, bitrate));

			// stts box
			var sttsContent = new BinaryDataBuilder ();
			sttsContent.AddUInt32BE (0);
			sttsContent.AddUInt32BE (0);
			builder.Add (CreateBox ("stts", sttsContent.ToArray ()));

			// stsc box
			var stscContent = new BinaryDataBuilder ();
			stscContent.AddUInt32BE (0);
			stscContent.AddUInt32BE (0);
			builder.Add (CreateBox ("stsc", stscContent.ToArray ()));

			// stsz box
			var stszContent = new BinaryDataBuilder ();
			stszContent.AddUInt32BE (0);
			stszContent.AddUInt32BE (0);
			stszContent.AddUInt32BE (0);
			builder.Add (CreateBox ("stsz", stszContent.ToArray ()));

			// stco box
			var stcoContent = new BinaryDataBuilder ();
			stcoContent.AddUInt32BE (0);
			stcoContent.AddUInt32BE (0);
			builder.Add (CreateBox ("stco", stcoContent.ToArray ()));

			return CreateBox ("stbl", builder.ToArray ());
		}

		/// <summary>
		/// Creates an stsd box with custom audio properties.
		/// </summary>
		static byte[] CreateStsdWithProperties (Mp4CodecType codec, int sampleRate, int channels, int bitsPerSample, int bitrate)
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// Entry count
			builder.AddUInt32BE (1);

			// Sample entry
			if (codec == Mp4CodecType.Aac)
				builder.Add (CreateMp4aSampleEntryWithProperties (sampleRate, channels, bitrate));
			else if (codec == Mp4CodecType.Alac)
				builder.Add (CreateAlacSampleEntryWithProperties (sampleRate, channels, bitsPerSample));

			return CreateBox ("stsd", builder.ToArray ());
		}

		/// <summary>
		/// Creates an mp4a sample entry with custom properties.
		/// </summary>
		static byte[] CreateMp4aSampleEntryWithProperties (int sampleRate, int channels, int bitrate)
		{
			var builder = new BinaryDataBuilder ();

			// Reserved (6 bytes)
			builder.AddZeros (6);

			// Data reference index
			builder.AddUInt16BE (1);

			// Reserved (8 bytes)
			builder.AddZeros (8);

			// Channel count
			builder.AddUInt16BE ((ushort)channels);

			// Sample size (16 bits for AAC)
			builder.AddUInt16BE (16);

			// Pre-defined
			builder.AddUInt16BE (0);

			// Reserved
			builder.AddUInt16BE (0);

			// Sample rate (16.16 fixed point)
			builder.AddUInt32BE (unchecked((uint)(sampleRate << 16)));

			// esds box with custom bitrate
			builder.Add (CreateEsdsWithBitrate (bitrate));

			return CreateBox ("mp4a", builder.ToArray ());
		}

		/// <summary>
		/// Creates an esds box with custom bitrate.
		/// </summary>
		static byte[] CreateEsdsWithBitrate (int bitrateKbps)
		{
			var builder = new BinaryDataBuilder ();
			var bitrateBps = (uint)(bitrateKbps * 1000);

			// Version + flags
			builder.AddUInt32BE (0);

			// ES_Descriptor tag
			builder.Add ((byte)0x03);
			builder.Add ((byte)0x19); // Size

			// ES_ID
			builder.AddUInt16BE (0);

			// Flags
			builder.Add ((byte)0x00);

			// DecoderConfigDescriptor tag
			builder.Add ((byte)0x04);
			builder.Add ((byte)0x11); // Size

			// Object type (AAC LC = 0x40)
			builder.Add ((byte)0x40);

			// Stream type
			builder.Add ((byte)0x15);

			// Buffer size DB
			builder.Add ((byte)0x00);
			builder.AddUInt16BE (0);

			// Max bitrate
			builder.AddUInt32BE (bitrateBps);

			// Avg bitrate
			builder.AddUInt32BE (bitrateBps);

			// DecoderSpecificInfo tag
			builder.Add ((byte)0x05);
			builder.Add ((byte)0x02); // Size

			// AAC config (44100 Hz, stereo)
			builder.AddUInt16BE (0x1190);

			// SLConfigDescriptor tag
			builder.Add ((byte)0x06);
			builder.Add ((byte)0x01);
			builder.Add ((byte)0x02);

			return CreateBox ("esds", builder.ToArray ());
		}

		/// <summary>
		/// Creates an alac sample entry with custom properties.
		/// </summary>
		static byte[] CreateAlacSampleEntryWithProperties (int sampleRate, int channels, int bitsPerSample)
		{
			var builder = new BinaryDataBuilder ();

			// Reserved (6 bytes)
			builder.AddZeros (6);

			// Data reference index
			builder.AddUInt16BE (1);

			// Reserved (8 bytes)
			builder.AddZeros (8);

			// Channel count
			builder.AddUInt16BE ((ushort)channels);

			// Sample size
			builder.AddUInt16BE ((ushort)bitsPerSample);

			// Pre-defined
			builder.AddUInt16BE (0);

			// Reserved
			builder.AddUInt16BE (0);

			// Sample rate (16.16 fixed point)
			builder.AddUInt32BE (unchecked((uint)(sampleRate << 16)));

			// alac box (magic cookie)
			builder.Add (CreateAlacBoxWithProperties (sampleRate, channels, bitsPerSample));

			return CreateBox ("alac", builder.ToArray ());
		}

		/// <summary>
		/// Creates an alac magic cookie box with custom properties.
		/// </summary>
		static byte[] CreateAlacBoxWithProperties (int sampleRate, int channels, int bitsPerSample)
		{
			var builder = new BinaryDataBuilder ();

			// Version + flags
			builder.AddUInt32BE (0);

			// Frame length
			builder.AddUInt32BE (4096);

			// Compatible version
			builder.Add ((byte)0);

			// Bit depth
			builder.Add ((byte)bitsPerSample);

			// Rice history mult
			builder.Add ((byte)40);

			// Rice initial history
			builder.Add ((byte)10);

			// Rice parameter limit
			builder.Add ((byte)14);

			// Number of channels
			builder.Add ((byte)channels);

			// Max run
			builder.AddUInt16BE (255);

			// Max frame bytes
			builder.AddUInt32BE (0);

			// Avg bit rate
			builder.AddUInt32BE (0);

			// Sample rate
			builder.AddUInt32BE ((uint)sampleRate);

			return CreateBox ("alac", builder.ToArray ());
		}

		/// <summary>
		/// Creates an MP4 file with multiple stsd entries.
		/// </summary>
		public static byte[] CreateWithMultipleStsdEntries ()
		{
			// For now, return minimal file
			return CreateMinimalM4a (Mp4CodecType.Aac);
		}
	}

	/// <summary>
	/// Builders for WavPack (.wv) file test data.
	/// </summary>
	/// <remarks>
	/// WavPack format: "wvpk" magic + 32-byte block header.
	/// Metadata stored in APEv2 tags at end of file.
	/// Spec: www.wavpack.com/file_format.txt
	/// </remarks>
	public static class WavPack
	{
		private const int BlockHeaderSize = 32;

		/// <summary>
		/// Creates a minimal valid WavPack file.
		/// </summary>
		public static byte[] CreateMinimal (
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			int bitsPerSample = TestConstants.AudioProperties.BitsPerSample16)
		{
			var builder = new BinaryDataBuilder ();

			// Magic "wvpk"
			builder.Add (TestConstants.Magic.WavPack);

			// Block size (excluding first 8 bytes) - 24 for minimal header
			builder.AddUInt32LE (24);

			// Version (0x0410 = 4.16)
			builder.AddUInt16LE (0x0410);

			// Track number (unused)
			builder.Add ((byte)0);

			// Index number (unused)
			builder.Add ((byte)0);

			// Total samples
			builder.AddUInt32LE (44100); // 1 second

			// Block index
			builder.AddUInt32LE (0);

			// Block samples
			builder.AddUInt32LE (44100);

			// Flags
			uint flags = 0;
			// Bits 0-1: bytes per sample - 1
			flags |= (uint)((bitsPerSample / 8) - 1) & 0x3;
			// Bit 2: mono
			if (channels == 1)
				flags |= 0x4;
			// Bits 23-26: sample rate index (9 = 44100)
			var sampleRateIndex = GetSampleRateIndex (sampleRate);
			flags |= (uint)(sampleRateIndex << 23);
			// Set initial and final block flags
			flags |= 0x800 | 0x1000; // Initial block, final block
			builder.AddUInt32LE (flags);

			// CRC
			builder.AddUInt32LE (0);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a WavPack file with APE tag metadata.
		/// </summary>
		public static byte[] CreateWithMetadata (
			string? title = null,
			string? artist = null,
			string? album = null,
			int sampleRate = TestConstants.AudioProperties.SampleRate44100)
		{
			var audioData = CreateMinimal (sampleRate);
			var tagData = Ape.CreateTag (title, artist, album);

			var result = new byte[audioData.Length + tagData.Length];
			Array.Copy (audioData, result, audioData.Length);
			Array.Copy (tagData, 0, result, audioData.Length, tagData.Length);
			return result;
		}

		private static int GetSampleRateIndex (int sampleRate) => sampleRate switch {
			6000 => 0,
			8000 => 1,
			9600 => 2,
			11025 => 3,
			12000 => 4,
			16000 => 5,
			22050 => 6,
			24000 => 7,
			32000 => 8,
			44100 => 9,
			48000 => 10,
			64000 => 11,
			88200 => 12,
			96000 => 13,
			192000 => 14,
			_ => 15 // Custom rate
		};
	}

	/// <summary>
	/// Builders for Monkey's Audio (.ape) file test data.
	/// </summary>
	/// <remarks>
	/// Monkey's Audio format: "MAC " magic + version + header.
	/// New format (>=3980) has descriptor + header, old format has different layout.
	/// Metadata stored in APEv2 tags at end of file.
	/// Spec: wiki.hydrogenaud.io/index.php?title=APE
	/// </remarks>
	public static class MonkeysAudio
	{
		private const int NewFormatVersion = 3980;
		private const int DescriptorSize = 52;
		private const int HeaderSize = 24;

		/// <summary>
		/// Creates a minimal valid Monkey's Audio file (new format >=3980).
		/// </summary>
		public static byte[] CreateMinimal (
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			int bitsPerSample = TestConstants.AudioProperties.BitsPerSample16,
			int version = 3990)
		{
			if (version < NewFormatVersion)
				return CreateOldFormat (sampleRate, channels, bitsPerSample, version);

			return CreateNewFormat (sampleRate, channels, bitsPerSample, version);
		}

		private static byte[] CreateNewFormat (int sampleRate, int channels, int bitsPerSample, int version)
		{
			var builder = new BinaryDataBuilder ();

			// Magic "MAC "
			builder.Add (TestConstants.Magic.MonkeysAudio);

			// Version
			builder.AddUInt16LE ((ushort)version);

			// Padding (2 bytes)
			builder.AddUInt16LE (0);

			// Descriptor bytes (offset where header starts - absolute from file start)
			builder.AddUInt32LE ((uint)(4 + DescriptorSize)); // Magic + descriptor

			// Header bytes
			builder.AddUInt32LE (HeaderSize);

			// Seek table bytes
			builder.AddUInt32LE (0);

			// Wave header bytes
			builder.AddUInt32LE (0);

			// Audio data bytes
			builder.AddUInt32LE (1000);

			// Audio data bytes high
			builder.AddUInt32LE (0);

			// Terminating data bytes
			builder.AddUInt32LE (0);

			// MD5 (16 bytes)
			for (var i = 0; i < 16; i++)
				builder.Add ((byte)0);

			// Padding to reach descriptor size
			while (builder.Length < 4 + DescriptorSize)
				builder.Add ((byte)0);

			// APE_HEADER
			// Compression type (2000 = Normal)
			builder.AddUInt16LE (2000);

			// Format flags
			builder.AddUInt16LE (0);

			// Blocks per frame
			builder.AddUInt32LE (73728);

			// Final frame blocks
			builder.AddUInt32LE (44100);

			// Total frames
			builder.AddUInt32LE (1);

			// Bits per sample
			builder.AddUInt16LE ((ushort)bitsPerSample);

			// Channels
			builder.AddUInt16LE ((ushort)channels);

			// Sample rate
			builder.AddUInt32LE ((uint)sampleRate);

			return builder.ToArray ();
		}

		private static byte[] CreateOldFormat (int sampleRate, int channels, int bitsPerSample, int version)
		{
			var builder = new BinaryDataBuilder ();

			// Magic "MAC "
			builder.Add (TestConstants.Magic.MonkeysAudio);

			// Version
			builder.AddUInt16LE ((ushort)version);

			// Compression type
			builder.AddUInt16LE (2000);

			// Format flags
			ushort formatFlags = 0;
			if (bitsPerSample == 8)
				formatFlags |= 0x0001;
			else if (bitsPerSample == 24)
				formatFlags |= 0x0008;
			builder.AddUInt16LE (formatFlags);

			// Channels
			builder.AddUInt16LE ((ushort)channels);

			// Sample rate
			builder.AddUInt32LE ((uint)sampleRate);

			// Header bytes
			builder.AddUInt32LE (0);

			// Terminating bytes
			builder.AddUInt32LE (0);

			// Total frames
			builder.AddUInt32LE (1);

			// Final frame blocks
			builder.AddUInt32LE (44100);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a Monkey's Audio file with APE tag metadata.
		/// </summary>
		public static byte[] CreateWithMetadata (
			string? title = null,
			string? artist = null,
			string? album = null,
			int sampleRate = TestConstants.AudioProperties.SampleRate44100)
		{
			var audioData = CreateMinimal (sampleRate);
			var tagData = Ape.CreateTag (title, artist, album);

			var result = new byte[audioData.Length + tagData.Length];
			Array.Copy (audioData, result, audioData.Length);
			Array.Copy (tagData, 0, result, audioData.Length, tagData.Length);
			return result;
		}
	}

	/// <summary>
	/// Builders for Musepack (.mpc) file test data.
	/// </summary>
	/// <remarks>
	/// Musepack SV8 format: "MPCK" magic + packet-based structure.
	/// Metadata stored in APEv2 tags at end of file.
	/// Spec: wiki.hydrogenaud.io/index.php?title=Musepack_SV8
	/// </remarks>
	public static class Musepack
	{
		private const byte PacketStreamHeader = 0x53; // 'SH'
		private const byte PacketReplayGain = 0x52;   // 'RG'
		private const byte PacketEncoderInfo = 0x45; // 'EI'
		private const byte PacketSeekOffset = 0x53;   // 'SO'
		private const byte PacketAudioPacket = 0x41;  // 'AP'
		private const byte PacketStreamEnd = 0x45;    // 'SE'

		/// <summary>
		/// Creates a minimal valid Musepack SV8 file.
		/// </summary>
		public static byte[] CreateMinimal (
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo)
		{
			var builder = new BinaryDataBuilder ();

			// Magic "MPCK"
			builder.Add (TestConstants.Magic.MusepackSV8);

			// Stream Header packet (SH)
			var shBuilder = new BinaryDataBuilder ();

			// CRC placeholder
			shBuilder.AddUInt32LE (0);

			// Stream version
			shBuilder.Add ((byte)8);

			// Sample count (variable length, just use a simple encoding)
			AddVarInt (shBuilder, 44100); // 1 second

			// Beginning silence
			AddVarInt (shBuilder, 0);

			// Sample frequency (0-3: 44100, 48000, 37800, 32000)
			var freqIndex = GetSampleFrequencyIndex (sampleRate);
			// Max used bands | channel mode (MS stereo) | frequency index
			var flags = (byte)((32 << 3) | (channels == 1 ? 0 : 1 << 2) | freqIndex);
			shBuilder.Add (flags);

			WritePacket (builder, (byte)'S', (byte)'H', shBuilder.ToArray ());

			// Stream End packet (SE)
			WritePacket (builder, (byte)'S', (byte)'E', []);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a Musepack file with APE tag metadata.
		/// </summary>
		public static byte[] CreateWithMetadata (
			string? title = null,
			string? artist = null,
			string? album = null,
			int sampleRate = TestConstants.AudioProperties.SampleRate44100)
		{
			var audioData = CreateMinimal (sampleRate);
			var tagData = Ape.CreateTag (title, artist, album);

			var result = new byte[audioData.Length + tagData.Length];
			Array.Copy (audioData, result, audioData.Length);
			Array.Copy (tagData, 0, result, audioData.Length, tagData.Length);
			return result;
		}

		private static void WritePacket (BinaryDataBuilder builder, byte key1, byte key2, byte[] data)
		{
			builder.Add (key1);
			builder.Add (key2);
			// Packet size (variable length encoding)
			AddVarInt (builder, data.Length + 2); // +2 for key bytes already written...but this is size of remaining
												  // Actually size is total packet size including key, so:
												  // Key (2) + size field (varies) + data
												  // For simplicity, just write length as single byte if small
			builder.Add (data);
		}

		private static void AddVarInt (BinaryDataBuilder builder, long value)
		{
			// SV8 variable-length integer encoding
			if (value < 128) {
				builder.Add ((byte)value);
			} else if (value < 16384) {
				builder.Add ((byte)(0x80 | (value & 0x7F)));
				builder.Add ((byte)((value >> 7) & 0x7F));
			} else {
				// For larger values, use more bytes
				builder.Add ((byte)(0x80 | (value & 0x7F)));
				builder.Add ((byte)(0x80 | ((value >> 7) & 0x7F)));
				builder.Add ((byte)((value >> 14) & 0x7F));
			}
		}

		private static int GetSampleFrequencyIndex (int sampleRate) => sampleRate switch {
			44100 => 0,
			48000 => 1,
			37800 => 2,
			32000 => 3,
			_ => 0
		};
	}

	/// <summary>
	/// Builders for APEv2 tag test data.
	/// </summary>
	/// <remarks>
	/// APEv2 is used by WavPack, Monkey's Audio, Musepack, and OptimFROG.
	/// Structure: Header (optional) + Items + Footer.
	/// Spec: wiki.hydrogenaud.io/index.php?title=APEv2_specification
	/// </remarks>
	public static class Ape
	{
		private const int FooterSize = 32;
		private static readonly byte[] Preamble = "APETAGEX"u8.ToArray ();

		/// <summary>
		/// Creates an APEv2 tag with basic metadata.
		/// </summary>
		public static byte[] CreateTag (
			string? title = null,
			string? artist = null,
			string? album = null)
		{
			var items = new List<byte[]> ();

			if (!string.IsNullOrEmpty (title))
				items.Add (CreateItem ("Title", title!));
			if (!string.IsNullOrEmpty (artist))
				items.Add (CreateItem ("Artist", artist!));
			if (!string.IsNullOrEmpty (album))
				items.Add (CreateItem ("Album", album!));

			return CreateTagFromItems ([.. items]);
		}

		/// <summary>
		/// Creates a single APE tag item.
		/// </summary>
		public static byte[] CreateItem (string key, string value)
		{
			var keyBytes = Encoding.ASCII.GetBytes (key);
			var valueBytes = Encoding.UTF8.GetBytes (value);

			var builder = new BinaryDataBuilder ();

			// Value size
			builder.AddUInt32LE ((uint)valueBytes.Length);

			// Flags (0 = UTF-8 text)
			builder.AddUInt32LE (0);

			// Key + null terminator
			builder.Add (keyBytes);
			builder.Add ((byte)0);

			// Value
			builder.Add (valueBytes);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an APE tag from a list of items.
		/// </summary>
		public static byte[] CreateTagFromItems (byte[][] items)
		{
			var builder = new BinaryDataBuilder ();

			// Calculate items total size
			var itemsSize = 0;
			foreach (var item in items)
				itemsSize += item.Length;

			// Header (optional - we include it)
			builder.Add (Preamble);
			builder.AddUInt32LE (2000); // Version 2.0
			builder.AddUInt32LE ((uint)(itemsSize + FooterSize)); // Tag size (includes footer, excludes header)
			builder.AddUInt32LE ((uint)items.Length); // Item count
			builder.AddUInt32LE (0x80000000); // Flags: header present, this is header
			builder.AddUInt32LE (0); builder.AddUInt32LE (0); // Reserved

			// Items
			foreach (var item in items)
				builder.Add (item);

			// Footer
			builder.Add (Preamble);
			builder.AddUInt32LE (2000); // Version 2.0
			builder.AddUInt32LE ((uint)(itemsSize + FooterSize)); // Tag size
			builder.AddUInt32LE ((uint)items.Length); // Item count
			builder.AddUInt32LE (0x80000000); // Flags: header present, this is footer (bit 29 = 0)
			builder.AddUInt32LE (0); builder.AddUInt32LE (0); // Reserved

			return builder.ToArray ();
		}
	}

	/// <summary>
	/// Builders for Ogg FLAC file test data.
	/// </summary>
	/// <remarks>
	/// Ogg FLAC wraps native FLAC in an Ogg container.
	/// First packet contains 0x7F + "FLAC" + mapping version + header count + STREAMINFO.
	/// Metadata stored in VorbisComment within second packet.
	/// Spec: xiph.org/flac/ogg_mapping.html
	/// </remarks>
	public static class OggFlac
	{
		/// <summary>
		/// Creates a minimal valid Ogg FLAC file.
		/// </summary>
		public static byte[] CreateMinimal (
			int sampleRate = TestConstants.AudioProperties.SampleRate44100,
			int channels = TestConstants.AudioProperties.ChannelsStereo,
			int bitsPerSample = TestConstants.AudioProperties.BitsPerSample16)
		{
			var builder = new BinaryDataBuilder ();
			var serialNumber = 1u;

			// First page: Ogg FLAC identification header
			var idPacket = CreateIdentificationPacket (sampleRate, channels, bitsPerSample);
			var page1 = Ogg.CreatePage (idPacket, 0, OggPageFlags.BeginOfStream, serialNumber);
			builder.Add (page1);

			// Second page: VorbisComment
			var commentPacket = CreateCommentPacket ();
			var page2 = Ogg.CreatePage (commentPacket, 1, OggPageFlags.EndOfStream, serialNumber);
			builder.Add (page2);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates an Ogg FLAC file with VorbisComment metadata.
		/// </summary>
		public static byte[] CreateWithMetadata (
			string? title = null,
			string? artist = null,
			string? album = null,
			int sampleRate = TestConstants.AudioProperties.SampleRate44100)
		{
			var builder = new BinaryDataBuilder ();
			var serialNumber = 1u;

			// First page: Ogg FLAC identification header
			var idPacket = CreateIdentificationPacket (sampleRate, 2, 16);
			var page1 = Ogg.CreatePage (idPacket, 0, OggPageFlags.BeginOfStream, serialNumber);
			builder.Add (page1);

			// Second page: VorbisComment with metadata
			var commentPacket = CreateCommentPacket (title, artist, album);
			var page2 = Ogg.CreatePage (commentPacket, 1, OggPageFlags.EndOfStream, serialNumber);
			builder.Add (page2);

			return builder.ToArray ();
		}

		private static byte[] CreateIdentificationPacket (int sampleRate, int channels, int bitsPerSample)
		{
			var builder = new BinaryDataBuilder ();

			// Packet type (0x7F for FLAC)
			builder.Add ((byte)0x7F);

			// "FLAC" signature
			builder.AddStringLatin1 ("FLAC");

			// Ogg FLAC mapping version (1.0)
			builder.Add ((byte)1);
			builder.Add ((byte)0);

			// Number of header packets (non-audio, excluding this one)
			builder.AddUInt16BE (1);

			// Native FLAC signature "fLaC"
			builder.Add (TestConstants.Magic.Flac);

			// STREAMINFO metadata block (type 0, last = false, length = 34)
			builder.Add ((byte)0x00); // Type 0 (STREAMINFO), not last
									  // Size (3 bytes big-endian) = 34
			builder.Add ((byte)0);
			builder.Add ((byte)0);
			builder.Add ((byte)34);

			// STREAMINFO content (34 bytes)
			// Min block size (16-bit)
			builder.AddUInt16BE (4096);
			// Max block size (16-bit)
			builder.AddUInt16BE (4096);
			// Min frame size (24-bit)
			builder.Add ((byte)0); builder.Add ((byte)0); builder.Add ((byte)0);
			// Max frame size (24-bit)
			builder.Add ((byte)0); builder.Add ((byte)0); builder.Add ((byte)0);

			// Sample rate (20 bits) + channels-1 (3 bits) + bits per sample-1 (5 bits) + total samples MSB (4 bits)
			var srChannelsBits = ((uint)sampleRate << 12) | ((uint)(channels - 1) << 9) | ((uint)(bitsPerSample - 1) << 4);
			builder.Add ((byte)(srChannelsBits >> 24));
			builder.Add ((byte)(srChannelsBits >> 16));
			builder.Add ((byte)(srChannelsBits >> 8));
			builder.Add ((byte)srChannelsBits);

			// Total samples (low 32 bits)
			builder.AddUInt32BE ((uint)sampleRate);

			// MD5 (16 bytes)
			for (var i = 0; i < 16; i++)
				builder.Add ((byte)0);

			return builder.ToArray ();
		}

		private static byte[] CreateCommentPacket (string? title = null, string? artist = null, string? album = null)
		{
			var commentBuilder = new BinaryDataBuilder ();

			// VORBIS_COMMENT metadata block (type 4, last = true)
			commentBuilder.Add ((byte)0x84); // Type 4, last block

			// Build comments content
			var commentsData = VorbisComment.CreateCommentsContent (title, artist, album);

			// Size (3 bytes big-endian)
			commentBuilder.Add ((byte)(commentsData.Length >> 16));
			commentBuilder.Add ((byte)(commentsData.Length >> 8));
			commentBuilder.Add ((byte)commentsData.Length);

			commentBuilder.Add (commentsData);

			return commentBuilder.ToArray ();
		}
	}

	/// <summary>
	/// Builders for DSF (DSD Stream File) test data.
	/// </summary>
	/// <remarks>
	/// DSF format: "DSD " chunk + "fmt " chunk + "data" chunk.
	/// Metadata stored in ID3v2 tag at offset specified in DSD chunk.
	/// Spec: dsd-guide.com/sites/default/files/white-papers/DSFFileFormatSpec_E.pdf
	/// </remarks>
	public static class Dsf
	{
		private const int DsdChunkSize = 28;
		private const int FmtChunkSize = 52;

		/// <summary>
		/// Creates a minimal valid DSF file.
		/// </summary>
		public static byte[] CreateMinimal (
			int sampleRate = 2822400, // Standard DSD64 rate
			int channels = 2,
			int bitsPerSample = 1)
		{
			var builder = new BinaryDataBuilder ();

			var dataChunkSize = (ulong)12 + 4096; // Chunk header + minimal data
			var totalFileSize = (ulong)DsdChunkSize + FmtChunkSize + dataChunkSize;

			// DSD chunk
			builder.Add (TestConstants.Magic.Dsf);
			builder.AddUInt64LE (DsdChunkSize);
			builder.AddUInt64LE (totalFileSize);
			builder.AddUInt64LE (0); // No ID3v2 tag

			// fmt chunk
			builder.AddStringLatin1 ("fmt ");
			builder.AddUInt64LE (FmtChunkSize);
			builder.AddUInt32LE (1); // Format version
			builder.AddUInt32LE (0); // Format ID (0 = DSD raw)
			builder.AddUInt32LE ((uint)channels); // Channel type
			builder.AddUInt32LE ((uint)channels); // Channel count
			builder.AddUInt32LE ((uint)sampleRate);
			builder.AddUInt32LE ((uint)bitsPerSample);
			builder.AddUInt64LE ((ulong)sampleRate); // Sample count (1 second)
			builder.AddUInt32LE (4096); // Block size per channel

			// Reserved
			builder.AddUInt32LE (0);

			// data chunk
			builder.AddStringLatin1 ("data");
			builder.AddUInt64LE (dataChunkSize);

			// Minimal audio data (silence)
			for (var i = 0; i < 4096; i++)
				builder.Add ((byte)0x69); // DSD silence pattern

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a DSF file with ID3v2 metadata.
		/// </summary>
		public static byte[] CreateWithMetadata (
			string? title = null,
			string? artist = null,
			string? album = null,
			int sampleRate = 2822400)
		{
			// Build ID3v2 tag
			var tagBuilder = new BinaryDataBuilder ();
			if (!string.IsNullOrEmpty (title))
				tagBuilder.Add (Id3v2.CreateTextFrame (TestConstants.FrameIds.Title, title!, TestConstants.Id3v2.Version4));
			if (!string.IsNullOrEmpty (artist))
				tagBuilder.Add (Id3v2.CreateTextFrame (TestConstants.FrameIds.Artist, artist!, TestConstants.Id3v2.Version4));
			if (!string.IsNullOrEmpty (album))
				tagBuilder.Add (Id3v2.CreateTextFrame (TestConstants.FrameIds.Album, album!, TestConstants.Id3v2.Version4));

			var frames = tagBuilder.ToArray ();
			var header = Id3v2.CreateHeader (TestConstants.Id3v2.Version4, (uint)frames.Length);
			var id3Tag = new byte[header.Length + frames.Length];
			Array.Copy (header, id3Tag, header.Length);
			Array.Copy (frames, 0, id3Tag, header.Length, frames.Length);

			// Build DSF with tag
			var builder = new BinaryDataBuilder ();
			var dataChunkSize = (ulong)12 + 4096;
			var audioSize = (ulong)DsdChunkSize + FmtChunkSize + dataChunkSize;
			var totalFileSize = audioSize + (ulong)id3Tag.Length;

			// DSD chunk with ID3v2 pointer
			builder.Add (TestConstants.Magic.Dsf);
			builder.AddUInt64LE (DsdChunkSize);
			builder.AddUInt64LE (totalFileSize);
			builder.AddUInt64LE (audioSize); // ID3v2 tag offset

			// fmt chunk
			builder.AddStringLatin1 ("fmt ");
			builder.AddUInt64LE (FmtChunkSize);
			builder.AddUInt32LE (1);
			builder.AddUInt32LE (0);
			builder.AddUInt32LE (2);
			builder.AddUInt32LE (2);
			builder.AddUInt32LE ((uint)sampleRate);
			builder.AddUInt32LE (1);
			builder.AddUInt64LE ((ulong)sampleRate);
			builder.AddUInt32LE (4096);
			builder.AddUInt32LE (0);

			// data chunk
			builder.AddStringLatin1 ("data");
			builder.AddUInt64LE (dataChunkSize);
			for (var i = 0; i < 4096; i++)
				builder.Add ((byte)0x69);

			// ID3v2 tag at end
			builder.Add (id3Tag);

			return builder.ToArray ();
		}
	}

	/// <summary>
	/// Builders for DFF (DSDIFF) file test data.
	/// </summary>
	/// <remarks>
	/// DFF format: "FRM8" container + "DSD " form type + property chunks.
	/// Metadata stored in DITI (title), DIAR (artist), ID3 chunks.
	/// Spec: dsd-guide.com/sites/default/files/white-papers/DSDIFF_1.5_Spec.pdf
	/// </remarks>
	public static class Dff
	{
		/// <summary>
		/// Creates a minimal valid DFF file.
		/// </summary>
		public static byte[] CreateMinimal (
			int sampleRate = 2822400,
			int channels = 2)
		{
			var builder = new BinaryDataBuilder ();

			// Calculate chunk sizes (each sub-chunk: 4-byte ID + 8-byte size + data)
			var fverChunkSize = 12 + 4; // FVER header + version (required first chunk per DSDIFF spec)
			var fsChunkDataSize = 4; // sample rate (4 bytes)
			var chnlChunkDataSize = 2 + channels * 4; // channel count (2) + channel IDs (4 each)
			var cmprChunkDataSize = 5; // compression type (4) + count (1)

			// PROP content size (excludes PROP header itself)
			// = "SND " + FS chunk + CHNL chunk + CMPR chunk
			var propContentSize = 4 + (12 + fsChunkDataSize) + (12 + chnlChunkDataSize) + (12 + cmprChunkDataSize);
			var propChunkSize = 12 + propContentSize; // PROP header + content

			var dsdChunkSize = 12 + 4096; // DSD header + minimal audio data
			var formSize = 4 + fverChunkSize + propChunkSize + dsdChunkSize; // "DSD " + all chunks

			// FRM8 container
			builder.Add (TestConstants.Magic.Frm8);
			builder.AddUInt64BE ((ulong)formSize);

			// Form type "DSD "
			builder.Add (TestConstants.Magic.DsdType);

			// FVER chunk (format version - MUST be first chunk per DSDIFF spec)
			builder.AddStringLatin1 ("FVER");
			builder.AddUInt64BE (4); // Size is just the 4-byte version
			builder.AddUInt32BE (0x01050000); // Version 1.5.0.0

			// PROP chunk (properties)
			builder.AddStringLatin1 ("PROP");
			builder.AddUInt64BE ((ulong)propContentSize); // Size of PROP content (excluding header)

			// Property type "SND "
			builder.AddStringLatin1 ("SND ");

			// FS chunk (sample rate)
			builder.AddStringLatin1 ("FS  ");
			builder.AddUInt64BE ((ulong)fsChunkDataSize);
			builder.AddUInt32BE ((uint)sampleRate);

			// CHNL chunk (channels)
			builder.AddStringLatin1 ("CHNL");
			builder.AddUInt64BE ((ulong)chnlChunkDataSize);
			builder.AddUInt16BE ((ushort)channels);
			// Channel IDs (simplified - just use SLFT/SRGT for stereo)
			if (channels >= 1)
				builder.AddStringLatin1 ("SLFT");
			if (channels >= 2)
				builder.AddStringLatin1 ("SRGT");

			// CMPR chunk (compression type)
			builder.AddStringLatin1 ("CMPR");
			builder.AddUInt64BE ((ulong)cmprChunkDataSize);
			builder.AddStringLatin1 ("DSD ");
			builder.Add ((byte)0); // Count byte

			// Padding for 8-byte alignment
			while (builder.Length % 8 != 0)
				builder.Add ((byte)0);

			// DSD chunk (audio data)
			builder.AddStringLatin1 ("DSD ");
			builder.AddUInt64BE (4096);
			for (var i = 0; i < 4096; i++)
				builder.Add ((byte)0x69);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a DFF file with metadata in DITI and DIAR chunks.
		/// </summary>
		public static byte[] CreateWithMetadata (
			string? title = null,
			string? artist = null,
			int sampleRate = 2822400)
		{
			var builder = new BinaryDataBuilder ();

			// Calculate chunk sizes (each sub-chunk: 4-byte ID + 8-byte size + data)
			var titleBytes = string.IsNullOrEmpty (title) ? [] : Encoding.ASCII.GetBytes (title);
			var artistBytes = string.IsNullOrEmpty (artist) ? [] : Encoding.ASCII.GetBytes (artist);

			var fverChunkSize = 12 + 4; // FVER header + version (required first chunk per DSDIFF spec)
			var fsChunkDataSize = 4; // sample rate (4 bytes)
			var chnlChunkDataSize = 2 + 2 * 4; // channel count (2) + 2 channel IDs (4 each)
			var cmprChunkDataSize = 5; // compression type (4) + count (1)

			// PROP content size (excludes PROP header itself)
			var propContentSize = 4 + (12 + fsChunkDataSize) + (12 + chnlChunkDataSize) + (12 + cmprChunkDataSize);
			var propChunkSize = 12 + propContentSize; // PROP header + content

			var ditiChunkSize = titleBytes.Length > 0 ? 12 + 4 + titleBytes.Length : 0;
			var diarChunkSize = artistBytes.Length > 0 ? 12 + 4 + artistBytes.Length : 0;
			var dsdChunkSize = 12 + 4096;

			// Add padding for alignment
			ditiChunkSize = (ditiChunkSize + 7) & ~7;
			diarChunkSize = (diarChunkSize + 7) & ~7;
			var formSize = 4 + fverChunkSize + propChunkSize + ditiChunkSize + diarChunkSize + dsdChunkSize;

			// FRM8 container
			builder.Add (TestConstants.Magic.Frm8);
			builder.AddUInt64BE ((ulong)formSize);
			builder.Add (TestConstants.Magic.DsdType);

			// FVER chunk (format version - MUST be first chunk per DSDIFF spec)
			builder.AddStringLatin1 ("FVER");
			builder.AddUInt64BE (4); // Size is just the 4-byte version
			builder.AddUInt32BE (0x01050000); // Version 1.5.0.0

			// PROP chunk
			builder.AddStringLatin1 ("PROP");
			builder.AddUInt64BE ((ulong)propContentSize); // Size of PROP content (excluding header)
			builder.AddStringLatin1 ("SND ");
			builder.AddStringLatin1 ("FS  ");
			builder.AddUInt64BE ((ulong)fsChunkDataSize);
			builder.AddUInt32BE ((uint)sampleRate);
			builder.AddStringLatin1 ("CHNL");
			builder.AddUInt64BE ((ulong)chnlChunkDataSize);
			builder.AddUInt16BE (2);
			builder.AddStringLatin1 ("SLFT");
			builder.AddStringLatin1 ("SRGT");
			builder.AddStringLatin1 ("CMPR");
			builder.AddUInt64BE ((ulong)cmprChunkDataSize);
			builder.AddStringLatin1 ("DSD ");
			builder.Add ((byte)0);
			while (builder.Length % 8 != 0)
				builder.Add ((byte)0);

			// DITI chunk (title)
			if (titleBytes.Length > 0) {
				builder.AddStringLatin1 ("DITI");
				builder.AddUInt64BE ((ulong)(4 + titleBytes.Length));
				builder.AddUInt32BE ((uint)titleBytes.Length);
				builder.Add (titleBytes);
				while (builder.Length % 8 != 0)
					builder.Add ((byte)0);
			}

			// DIAR chunk (artist)
			if (artistBytes.Length > 0) {
				builder.AddStringLatin1 ("DIAR");
				builder.AddUInt64BE ((ulong)(4 + artistBytes.Length));
				builder.AddUInt32BE ((uint)artistBytes.Length);
				builder.Add (artistBytes);
				while (builder.Length % 8 != 0)
					builder.Add ((byte)0);
			}

			// DSD chunk
			builder.AddStringLatin1 ("DSD ");
			builder.AddUInt64BE (4096);
			for (var i = 0; i < 4096; i++)
				builder.Add ((byte)0x69);

			return builder.ToArray ();
		}

		/// <summary>
		/// Creates a DFF file with ID3v2 metadata chunk (the writable format).
		/// </summary>
		public static byte[] CreateWithId3v2 (
			string? title = null,
			string? artist = null,
			string? album = null,
			int sampleRate = 2822400,
			int channels = 2)
		{
			// First create base file without ID3
			var baseFile = CreateMinimal (sampleRate, channels);

			// If no metadata requested, return base file
			if (string.IsNullOrEmpty (title) && string.IsNullOrEmpty (artist) && string.IsNullOrEmpty (album))
				return baseFile;

			// Parse and add ID3v2 tag
			var parseResult = TagLibSharp2.Dff.DffFile.Read (baseFile);
			if (!parseResult.IsSuccess)
				throw new InvalidOperationException ($"Failed to parse DFF: {parseResult.Error}");

			var file = parseResult.File!;
			file.EnsureId3v2Tag ();

			if (!string.IsNullOrEmpty (title))
				file.Id3v2Tag!.Title = title;
			if (!string.IsNullOrEmpty (artist))
				file.Id3v2Tag!.Artist = artist;
			if (!string.IsNullOrEmpty (album))
				file.Id3v2Tag!.Album = album;

			return file.Render ().ToArray ();
		}

		/// <summary>
		/// Creates a DFF file with a known audio pattern for byte-level verification.
		/// The audio pattern repeats 0xAA, 0xBB, 0xCC throughout the DSD data.
		/// </summary>
		public static byte[] CreateWithKnownAudioPattern (
			int sampleRate = 2822400,
			int channels = 2,
			int audioSize = 4096)
		{
			var builder = new BinaryDataBuilder ();

			// Calculate chunk sizes
			var fverChunkSize = 12 + 4;
			var fsChunkDataSize = 4;
			var chnlChunkDataSize = 2 + channels * 4;
			var cmprChunkDataSize = 5;
			var propContentSize = 4 + (12 + fsChunkDataSize) + (12 + chnlChunkDataSize) + (12 + cmprChunkDataSize);
			var propChunkSize = 12 + propContentSize;
			var dsdChunkSize = 12 + audioSize;
			var formSize = 4 + fverChunkSize + propChunkSize + dsdChunkSize;

			// FRM8 container
			builder.Add (TestConstants.Magic.Frm8);
			builder.AddUInt64BE ((ulong)formSize);
			builder.Add (TestConstants.Magic.DsdType);

			// FVER chunk
			builder.AddStringLatin1 ("FVER");
			builder.AddUInt64BE (4);
			builder.AddUInt32BE (0x01050000);

			// PROP chunk
			builder.AddStringLatin1 ("PROP");
			builder.AddUInt64BE ((ulong)propContentSize);
			builder.AddStringLatin1 ("SND ");
			builder.AddStringLatin1 ("FS  ");
			builder.AddUInt64BE ((ulong)fsChunkDataSize);
			builder.AddUInt32BE ((uint)sampleRate);
			builder.AddStringLatin1 ("CHNL");
			builder.AddUInt64BE ((ulong)chnlChunkDataSize);
			builder.AddUInt16BE ((ushort)channels);
			if (channels >= 1)
				builder.AddStringLatin1 ("SLFT");
			if (channels >= 2)
				builder.AddStringLatin1 ("SRGT");
			builder.AddStringLatin1 ("CMPR");
			builder.AddUInt64BE ((ulong)cmprChunkDataSize);
			builder.AddStringLatin1 ("DSD ");
			builder.Add ((byte)0);
			while (builder.Length % 8 != 0)
				builder.Add ((byte)0);

			// DSD chunk with known pattern (0xAA, 0xBB, 0xCC repeating)
			builder.AddStringLatin1 ("DSD ");
			builder.AddUInt64BE ((ulong)audioSize);
			for (var i = 0; i < audioSize; i++)
				builder.Add ((byte)(0xAA + (i % 3) * 0x11));

			return builder.ToArray ();
		}
	}
}
