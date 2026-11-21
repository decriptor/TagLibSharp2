// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Matroska;

/// <summary>
/// Element IDs from RFC 8794 and the Matroska element specification.
/// The values include the EBML width marker.
/// </summary>
static class MatroskaIds
{
	public const ulong Ebml = 0x1A45DFA3;
	public const ulong DocType = 0x4282;
	public const ulong DocTypeVersion = 0x4287;
	public const ulong Void = 0xEC;
	public const ulong Crc32 = 0xBF;
	public const ulong Segment = 0x18538067;
	public const ulong SeekHead = 0x114D9B74;
	public const ulong Info = 0x1549A966;
	public const ulong TimestampScale = 0x2AD7B1;
	public const ulong Duration = 0x4489;
	public const ulong SegmentTitle = 0x7BA9;
	public const ulong Tracks = 0x1654AE6B;
	public const ulong TrackEntry = 0xAE;
	public const ulong TrackType = 0x83;
	public const ulong TrackNumber = 0xD7;
	public const ulong TrackUid = 0x73C5;
	public const ulong FlagEnabled = 0xB9;
	public const ulong FlagDefault = 0x88;
	public const ulong CodecId = 0x86;
	public const ulong CodecPrivate = 0x63A2;
	public const ulong CodecName = 0x258688;
	public const ulong DefaultDuration = 0x23E383;
	public const ulong Audio = 0xE1;
	public const ulong SamplingFrequency = 0xB5;
	public const ulong OutputSamplingFrequency = 0x78B5;
	public const ulong Channels = 0x9F;
	public const ulong BitDepth = 0x6264;
	public const ulong Video = 0xE0;
	public const ulong PixelWidth = 0xB0;
	public const ulong PixelHeight = 0xBA;
	public const ulong AttachedFile = 0x61A7;
	public const ulong FileDescription = 0x467E;
	public const ulong FileName = 0x466E;
	public const ulong FileMimeType = 0x4660;
	public const ulong FileData = 0x465C;
	public const ulong FileUid = 0x46AE;
	public const ulong Cluster = 0x1F43B675;
	public const ulong Position = 0xA7;
	public const ulong Cues = 0x1C53BB6B;
	public const ulong Seek = 0x4DBB;
	public const ulong SeekId = 0x53AB;
	public const ulong SeekPosition = 0x53AC;
	public const ulong CuePoint = 0xBB;
	public const ulong CueTrackPositions = 0xB7;
	public const ulong CueClusterPosition = 0xF1;
	public const ulong CueCodecState = 0xEA;
	public const ulong Attachments = 0x1941A469;
	public const ulong Chapters = 0x1043A770;
	public const ulong Tags = 0x1254C367;
	public const ulong Tag = 0x7373;
	public const ulong Targets = 0x63C0;
	public const ulong TargetTypeValue = 0x68CA;
	public const ulong TagTrackUid = 0x63C5;
	public const ulong TagEditionUid = 0x63C9;
	public const ulong TagChapterUid = 0x63C4;
	public const ulong TagAttachmentUid = 0x63C6;
	public const ulong SimpleTag = 0x67C8;
	public const ulong TagName = 0x45A3;
	public const ulong TagLanguage = 0x447A;
	public const ulong TagLanguageBcp47 = 0x447B;
	public const ulong TagDefault = 0x4484;
	public const ulong TagDefaultBogus = 0x44B4;
	public const ulong TagBinary = 0x4485;
	public const ulong TagString = 0x4487;

	public static bool IsSegmentChild (ulong id) =>
		id == SeekHead
		|| id == Info
		|| id == Tracks
		|| id == Cluster
		|| id == Cues
		|| id == Attachments
		|| id == Chapters
		|| id == Tags;
}
