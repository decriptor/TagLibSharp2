// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Tests;

/// <summary>
/// Centralized constants for test data to ensure consistency and self-documentation.
/// </summary>
/// <remarks>
/// Using named constants instead of magic numbers makes tests more readable and maintainable.
/// Any developer can understand what a value represents without needing format knowledge.
/// </remarks>
public static class TestConstants
{
	/// <summary>
	/// File format magic bytes and signatures.
	/// </summary>
	public static class Magic
	{
		/// <summary>ID3v2 tag signature "ID3" (0x49 0x44 0x33).</summary>
		public static readonly byte[] Id3 = [0x49, 0x44, 0x33];

		/// <summary>FLAC format signature "fLaC" (0x66 0x4C 0x61 0x43).</summary>
		public static readonly byte[] Flac = [0x66, 0x4C, 0x61, 0x43];

		/// <summary>Ogg container signature "OggS" (0x4F 0x67 0x67 0x53).</summary>
		public static readonly byte[] Ogg = [0x4F, 0x67, 0x67, 0x53];

		/// <summary>RIFF container signature "RIFF" (0x52 0x49 0x46 0x46).</summary>
		public static readonly byte[] Riff = [0x52, 0x49, 0x46, 0x46];

		/// <summary>WAVE format identifier "WAVE" (0x57 0x41 0x56 0x45).</summary>
		public static readonly byte[] Wave = [0x57, 0x41, 0x56, 0x45];

		/// <summary>IFF/AIFF container signature "FORM" (0x46 0x4F 0x52 0x4D).</summary>
		public static readonly byte[] Form = [0x46, 0x4F, 0x52, 0x4D];

		/// <summary>AIFF format identifier "AIFF" (0x41 0x49 0x46 0x46).</summary>
		public static readonly byte[] Aiff = [0x41, 0x49, 0x46, 0x46];

		/// <summary>AIFC format identifier "AIFC" (0x41 0x49 0x46 0x43).</summary>
		public static readonly byte[] Aifc = [0x41, 0x49, 0x46, 0x43];

		/// <summary>Vorbis packet signature "vorbis" (0x76 0x6F 0x72 0x62 0x69 0x73).</summary>
		public static readonly byte[] Vorbis = [0x76, 0x6F, 0x72, 0x62, 0x69, 0x73];

		/// <summary>Opus identification header signature "OpusHead" (8 bytes).</summary>
		public static readonly byte[] OpusHead = [0x4F, 0x70, 0x75, 0x73, 0x48, 0x65, 0x61, 0x64];

		/// <summary>Opus comment header signature "OpusTags" (8 bytes).</summary>
		public static readonly byte[] OpusTags = [0x4F, 0x70, 0x75, 0x73, 0x54, 0x61, 0x67, 0x73];

		/// <summary>DSF format signature "DSD " (0x44 0x53 0x44 0x20).</summary>
		public static readonly byte[] Dsf = [0x44, 0x53, 0x44, 0x20];

		/// <summary>DFF/DSDIFF container signature "FRM8" (0x46 0x52 0x4D 0x38).</summary>
		public static readonly byte[] Frm8 = [0x46, 0x52, 0x4D, 0x38];

		/// <summary>DFF/DSDIFF type identifier "DSD " at offset 12 (0x44 0x53 0x44 0x20).</summary>
		public static readonly byte[] DsdType = [0x44, 0x53, 0x44, 0x20];

		/// <summary>ASF Header Object GUID (16 bytes) for WMA/WMV/ASF files.</summary>
		public static readonly byte[] Asf = [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];

		/// <summary>Musepack SV7 magic "MP+" (0x4D 0x50 0x2B).</summary>
		public static readonly byte[] MusepackSV7 = [0x4D, 0x50, 0x2B];

		/// <summary>Musepack SV8 magic "MPCK" (0x4D 0x50 0x43 0x4B).</summary>
		public static readonly byte[] MusepackSV8 = [0x4D, 0x50, 0x43, 0x4B];
	}

	/// <summary>
	/// ID3v2 frame identifiers.
	/// </summary>
	public static class FrameIds
	{
		/// <summary>Title/songname/content description.</summary>
		public const string Title = "TIT2";

		/// <summary>Lead performer(s)/Soloist(s).</summary>
		public const string Artist = "TPE1";

		/// <summary>Album/Movie/Show title.</summary>
		public const string Album = "TALB";

		/// <summary>Track number/Position in set.</summary>
		public const string Track = "TRCK";

		/// <summary>Recording year (ID3v2.3).</summary>
		public const string Year = "TYER";

		/// <summary>Recording time (ID3v2.4, replaces TYER).</summary>
		public const string RecordingTime = "TDRC";

		/// <summary>Content type (genre).</summary>
		public const string Genre = "TCON";

		/// <summary>Band/orchestra/accompaniment.</summary>
		public const string AlbumArtist = "TPE2";

		/// <summary>Part of a set (disc number).</summary>
		public const string DiscNumber = "TPOS";

		/// <summary>Composer.</summary>
		public const string Composer = "TCOM";

		/// <summary>BPM (beats per minute).</summary>
		public const string Bpm = "TBPM";

		/// <summary>Attached picture.</summary>
		public const string Picture = "APIC";

		/// <summary>Comments.</summary>
		public const string Comment = "COMM";

		/// <summary>User-defined text information.</summary>
		public const string UserText = "TXXX";

		/// <summary>Unsynchronized lyrics/text transcription.</summary>
		public const string Lyrics = "USLT";

		/// <summary>Unique file identifier.</summary>
		public const string UniqueFileId = "UFID";
	}

	/// <summary>
	/// Vorbis Comment field names (case-insensitive per spec).
	/// </summary>
	public static class VorbisFields
	{
		public const string Title = "TITLE";
		public const string Artist = "ARTIST";
		public const string Album = "ALBUM";
		public const string Date = "DATE";
		public const string Genre = "GENRE";
		public const string TrackNumber = "TRACKNUMBER";
		public const string AlbumArtist = "ALBUMARTIST";
		public const string DiscNumber = "DISCNUMBER";
		public const string Composer = "COMPOSER";
		public const string Comment = "COMMENT";
	}

	/// <summary>
	/// Common test metadata values.
	/// </summary>
	public static class Metadata
	{
		public const string Title = "Test Title";
		public const string Artist = "Test Artist";
		public const string Album = "Test Album";
		public const string Genre = "Test Genre";
		public const string Year = "2025";
		public const string Comment = "Test Comment";

		/// <summary>Track number for single-value tests.</summary>
		public const int TrackNumber = 5;

		/// <summary>Total tracks for disc tests.</summary>
		public const int TotalTracks = 12;

		/// <summary>Track string in "N/M" format.</summary>
		public const string TrackString = "5/12";

		/// <summary>Disc number for multi-disc tests.</summary>
		public const int DiscNumber = 1;

		/// <summary>Total discs for multi-disc tests.</summary>
		public const int TotalDiscs = 2;
	}

	/// <summary>
	/// Vendor strings used in Vorbis Comments.
	/// </summary>
	public static class Vendors
	{
		/// <summary>Standard vendor string for TagLibSharp2.</summary>
		public const string TagLibSharp2 = "TagLibSharp2";

		/// <summary>Test vendor string for unit tests.</summary>
		public const string Test = "TagLibSharp2 Test";
	}

	/// <summary>
	/// ID3v2 format constants.
	/// </summary>
	public static class Id3v2
	{
		/// <summary>Size of ID3v2 header in bytes.</summary>
		public const int HeaderSize = 10;

		/// <summary>Major version for ID3v2.2.</summary>
		public const byte Version2 = 2;

		/// <summary>Major version for ID3v2.3.</summary>
		public const byte Version3 = 3;

		/// <summary>Major version for ID3v2.4.</summary>
		public const byte Version4 = 4;

		/// <summary>Minor version (always 0).</summary>
		public const byte MinorVersion = 0;

		/// <summary>Size of frame header in ID3v2.3/2.4.</summary>
		public const int FrameHeaderSize = 10;

		/// <summary>Size of frame header in ID3v2.2.</summary>
		public const int FrameHeaderSizeV22 = 6;

		/// <summary>Latin-1 text encoding byte.</summary>
		public const byte EncodingLatin1 = 0x00;

		/// <summary>UTF-16 with BOM text encoding byte.</summary>
		public const byte EncodingUtf16 = 0x01;

		/// <summary>UTF-16BE text encoding byte.</summary>
		public const byte EncodingUtf16Be = 0x02;

		/// <summary>UTF-8 text encoding byte (ID3v2.4 only).</summary>
		public const byte EncodingUtf8 = 0x03;

		/// <summary>Unsynchronization flag (bit 7 of flags byte).</summary>
		public const byte FlagUnsynchronization = 0x80;
	}

	/// <summary>
	/// ID3v2.2 3-character frame identifiers.
	/// </summary>
	public static class FrameIdsV22
	{
		/// <summary>Title/songname/content description (v2.2).</summary>
		public const string Title = "TT2";

		/// <summary>Lead performer(s)/Soloist(s) (v2.2).</summary>
		public const string Artist = "TP1";

		/// <summary>Album/Movie/Show title (v2.2).</summary>
		public const string Album = "TAL";

		/// <summary>Track number (v2.2).</summary>
		public const string Track = "TRK";

		/// <summary>Year (v2.2).</summary>
		public const string Year = "TYE";

		/// <summary>Content type/genre (v2.2).</summary>
		public const string Genre = "TCO";
	}

	/// <summary>
	/// ID3v1 format constants.
	/// </summary>
	public static class Id3v1
	{
		/// <summary>Total size of ID3v1 tag in bytes.</summary>
		public const int TagSize = 128;

		/// <summary>ID3v1 tag signature "TAG" (0x54 0x41 0x47).</summary>
		public static readonly byte[] Signature = [0x54, 0x41, 0x47];

		/// <summary>Maximum length of title field.</summary>
		public const int TitleLength = 30;

		/// <summary>Maximum length of artist field.</summary>
		public const int ArtistLength = 30;

		/// <summary>Maximum length of album field.</summary>
		public const int AlbumLength = 30;

		/// <summary>Length of year field.</summary>
		public const int YearLength = 4;

		/// <summary>Maximum length of comment field (ID3v1.0).</summary>
		public const int CommentLength = 30;

		/// <summary>Maximum length of comment field (ID3v1.1, leaves room for track).</summary>
		public const int CommentLengthV11 = 28;
	}

	/// <summary>
	/// FLAC format constants.
	/// </summary>
	public static class Flac
	{
		/// <summary>Size of FLAC magic signature.</summary>
		public const int MagicSize = 4;

		/// <summary>Block type for STREAMINFO (always first).</summary>
		public const byte BlockTypeStreamInfo = 0;

		/// <summary>Block type for PADDING.</summary>
		public const byte BlockTypePadding = 1;

		/// <summary>Block type for APPLICATION.</summary>
		public const byte BlockTypeApplication = 2;

		/// <summary>Block type for SEEKTABLE.</summary>
		public const byte BlockTypeSeekTable = 3;

		/// <summary>Block type for VORBIS_COMMENT.</summary>
		public const byte BlockTypeVorbisComment = 4;

		/// <summary>Block type for PICTURE.</summary>
		public const byte BlockTypePicture = 6;

		/// <summary>Size of STREAMINFO block data.</summary>
		public const int StreamInfoSize = 34;

		/// <summary>Flag indicating last metadata block (bit 7 set).</summary>
		public const byte LastBlockFlag = 0x80;
	}

	/// <summary>
	/// Ogg format constants.
	/// </summary>
	public static class Ogg
	{
		/// <summary>Size of Ogg page header (excluding segment table).</summary>
		public const int PageHeaderSize = 27;

		/// <summary>Flag for beginning of stream (BOS).</summary>
		public const byte FlagBos = 0x02;

		/// <summary>Flag for end of stream (EOS).</summary>
		public const byte FlagEos = 0x04;

		/// <summary>Flag for continued packet.</summary>
		public const byte FlagContinuation = 0x01;

		/// <summary>Vorbis identification header packet type.</summary>
		public const byte PacketTypeIdentification = 1;

		/// <summary>Vorbis comment header packet type.</summary>
		public const byte PacketTypeComment = 3;

		/// <summary>Vorbis setup header packet type.</summary>
		public const byte PacketTypeSetup = 5;
	}

	/// <summary>
	/// RIFF/WAV format constants.
	/// </summary>
	public static class Riff
	{
		/// <summary>Size of chunk header (ID + size).</summary>
		public const int ChunkHeaderSize = 8;

		/// <summary>Format chunk identifier "fmt ".</summary>
		public const string FormatChunkId = "fmt ";

		/// <summary>Data chunk identifier "data".</summary>
		public const string DataChunkId = "data";

		/// <summary>LIST chunk identifier "LIST".</summary>
		public const string ListChunkId = "LIST";

		/// <summary>INFO list type identifier "INFO".</summary>
		public const string InfoListType = "INFO";

		/// <summary>ID3 chunk identifier "id3 ".</summary>
		public const string Id3ChunkId = "id3 ";

		/// <summary>PCM audio format code.</summary>
		public const ushort FormatPcm = 1;

		/// <summary>IEEE float audio format code.</summary>
		public const ushort FormatFloat = 3;

		/// <summary>Extensible audio format code.</summary>
		public const ushort FormatExtensible = 0xFFFE;
	}

	/// <summary>
	/// AIFF format constants.
	/// </summary>
	public static class Aiff
	{
		/// <summary>Common chunk identifier "COMM".</summary>
		public const string CommonChunkId = "COMM";

		/// <summary>Sound data chunk identifier "SSND".</summary>
		public const string SoundDataChunkId = "SSND";

		/// <summary>ID3 chunk identifier "ID3 ".</summary>
		public const string Id3ChunkId = "ID3 ";

		/// <summary>Annotation chunk identifier "ANNO".</summary>
		public const string AnnotationChunkId = "ANNO";

		/// <summary>Size of standard AIFF COMM chunk data.</summary>
		public const int CommChunkSize = 18;

		/// <summary>Size of AIFC COMM chunk data (includes compression info).</summary>
		public const int CommChunkSizeAifc = 22;
	}

	/// <summary>
	/// Audio property test values.
	/// </summary>
	public static class AudioProperties
	{
		/// <summary>Standard sample rate for CD audio.</summary>
		public const int SampleRate44100 = 44100;

		/// <summary>High-definition sample rate.</summary>
		public const int SampleRate48000 = 48000;

		/// <summary>Stereo channel count.</summary>
		public const int ChannelsStereo = 2;

		/// <summary>Mono channel count.</summary>
		public const int ChannelsMono = 1;

		/// <summary>Standard CD bit depth.</summary>
		public const int BitsPerSample16 = 16;

		/// <summary>High-resolution bit depth.</summary>
		public const int BitsPerSample24 = 24;

		/// <summary>Common MP3 bitrate.</summary>
		public const int Bitrate128 = 128;

		/// <summary>High-quality MP3 bitrate.</summary>
		public const int Bitrate320 = 320;
	}

	/// <summary>
	/// Picture/image test constants.
	/// </summary>
	public static class Pictures
	{
		/// <summary>JPEG MIME type.</summary>
		public const string MimeTypeJpeg = "image/jpeg";

		/// <summary>PNG MIME type.</summary>
		public const string MimeTypePng = "image/png";

		/// <summary>Default picture description for tests.</summary>
		public const string DefaultDescription = "Test Picture";

		/// <summary>Front cover description.</summary>
		public const string FrontCoverDescription = "Front Cover";

		/// <summary>Minimal valid JPEG header for tests (SOI + APP0).</summary>
		public static readonly byte[] MinimalJpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];

		/// <summary>PNG signature for tests.</summary>
		public static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
	}

	/// <summary>
	/// MP4/M4A format constants.
	/// </summary>
	public static class Mp4
	{
		/// <summary>Minimum box size (4-byte size + 4-byte type).</summary>
		public const int MinBoxSize = 8;

		/// <summary>Extended size marker (size field = 1 indicates 64-bit size follows).</summary>
		public const uint ExtendedSizeMarker = 1;

		/// <summary>Size to end of file marker (size field = 0).</summary>
		public const uint SizeToEndOfFile = 0;

		/// <summary>M4A brand identifier.</summary>
		public const string BrandM4A = "M4A ";

		/// <summary>ISO base media brand identifier.</summary>
		public const string BrandIsom = "isom";

		/// <summary>MP4 version 2 brand identifier.</summary>
		public const string BrandMp42 = "mp42";

		/// <summary>Protected AAC brand identifier.</summary>
		public const string BrandM4P = "M4P ";
	}

	/// <summary>
	/// MP4 metadata atom identifiers.
	/// </summary>
	public static class Mp4Atoms
	{
		/// <summary>Title.</summary>
		public const string Title = "©nam";

		/// <summary>Artist.</summary>
		public const string Artist = "©ART";

		/// <summary>Album artist.</summary>
		public const string AlbumArtist = "aART";

		/// <summary>Album.</summary>
		public const string Album = "©alb";

		/// <summary>Year/Date.</summary>
		public const string Date = "©day";

		/// <summary>Genre (text).</summary>
		public const string Genre = "©gen";

		/// <summary>Genre (binary ID3v1 code).</summary>
		public const string GenreBinary = "gnre";

		/// <summary>Track number (binary).</summary>
		public const string TrackNumber = "trkn";

		/// <summary>Disc number (binary).</summary>
		public const string DiscNumber = "disk";

		/// <summary>Composer.</summary>
		public const string Composer = "©wrt";

		/// <summary>Comment.</summary>
		public const string Comment = "©cmt";

		/// <summary>Lyrics.</summary>
		public const string Lyrics = "©lyr";

		/// <summary>Cover art.</summary>
		public const string CoverArt = "covr";

		/// <summary>BPM (binary).</summary>
		public const string Bpm = "tmpo";

		/// <summary>Compilation flag (binary boolean).</summary>
		public const string Compilation = "cpil";

		/// <summary>Grouping.</summary>
		public const string Grouping = "©grp";

		/// <summary>Encoding tool.</summary>
		public const string EncodingTool = "©too";

		/// <summary>Copyright.</summary>
		public const string Copyright = "cprt";
	}

	/// <summary>
	/// MP4 cover art type codes.
	/// </summary>
	public static class Mp4CoverArtTypes
	{
		/// <summary>JPEG image type code.</summary>
		public const uint Jpeg = 0x0D;

		/// <summary>PNG image type code.</summary>
		public const uint Png = 0x0E;

		/// <summary>BMP image type code.</summary>
		public const uint Bmp = 0x1B;
	}
}
