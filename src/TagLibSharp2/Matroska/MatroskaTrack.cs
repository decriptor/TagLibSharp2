// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace TagLibSharp2.Matroska;

/// <summary>
/// The kind of frames a Matroska track holds. RFC 9559 §5.1.4.1.3, Table 2.
/// The registry can grow, so a value that is not listed here is kept as its number.
/// </summary>
[SuppressMessage ("Design", "CA1027:Mark enums with FlagsAttribute", Justification = "RFC 9559 Table 2 values are codes, not bits")]
public enum MatroskaTrackType
{
	/// <summary>No <c>TrackType</c> was stored, or it does not fit this enum.</summary>
	Unknown = 0,

	/// <summary>An image per frame.</summary>
	Video = 1,

	/// <summary>Audio samples.</summary>
	Audio = 2,

	/// <summary>A mix of other track types.</summary>
	Complex = 3,

	/// <summary>An image rendered over the video.</summary>
	Logo = 16,

	/// <summary>Subtitle or closed caption data.</summary>
	Subtitle = 17,

	/// <summary>Interactive buttons rendered over the video.</summary>
	Buttons = 18,

	/// <summary>Metadata that controls the player.</summary>
	Control = 32,

	/// <summary>Timed metadata passed to the player.</summary>
	Metadata = 33
}

/// <summary>
/// One <c>TrackEntry</c> from the first Segment's <c>Tracks</c>. RFC 9559 §5.1.4.1.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsEnabled"/> can be changed and is written back on save. An unchanged track keeps its bytes.
/// </para>
/// <para>
/// The main audio and video track is the first enabled default track of that type, then the first enabled one.
/// When every track of a type is disabled, the same order runs over the disabled tracks.
/// RFC 9559 §5.1.4.1.4 says a disabled track can be made usable by chapter codecs or control tracks,
/// so its media still describes the file.
/// </para>
/// </remarks>
public sealed class MatroskaTrack
{
	bool _isEnabled;

	internal MatroskaTrack (int ordinal, ulong trackNumber, ulong trackUid, ulong trackType, string? codecId, bool isDefault, bool isEnabled)
	{
		Ordinal = ordinal;
		TrackNumber = trackNumber;
		TrackUid = trackUid;
		RawTrackType = trackType;
		CodecId = codecId;
		IsDefault = isDefault;
		_isEnabled = isEnabled;
	}

	/// <summary>Gets the <c>TrackNumber</c> that blocks use to name this track, or 0 when it is missing.</summary>
	public ulong TrackNumber { get; }

	/// <summary>Gets the <c>TrackUID</c>, or 0 when it is missing.</summary>
	public ulong TrackUid { get; }

	/// <summary>Gets the <c>TrackType</c>.</summary>
	public MatroskaTrackType TrackType =>
		RawTrackType <= int.MaxValue ? (MatroskaTrackType)(int)RawTrackType : MatroskaTrackType.Unknown;

	/// <summary>Gets the <c>CodecID</c>, or null when it is missing.</summary>
	public string? CodecId { get; }

	/// <summary>
	/// Gets the <c>FlagDefault</c> value. A missing or empty element is 1. RFC 9559 §5.1.4.1.5 and §18.1.
	/// </summary>
	public bool IsDefault { get; }

	/// <summary>
	/// Gets or sets the <c>FlagEnabled</c> value. A missing or empty element is 1. RFC 9559 §5.1.4.1.4.
	/// </summary>
	/// <remarks>
	/// Setting this marks the track for writing. On save a stored element is rewritten in place.
	/// A missing or empty element is written only when the stored value differs. It takes its room from the
	/// <c>Void</c> right after <c>Tracks</c> when there is one, so nothing else moves. Otherwise every later
	/// element moves once and every SeekHead, Cues, and Cluster <c>Position</c> value follows, widened when it
	/// no longer fits. The EBML header's <c>DocTypeVersion</c> becomes at least 2. A save fails, and writes
	/// nothing, when the room cannot be made: for example when the only <c>Tracks</c> has an unknown size and
	/// no Void follows it, a second <c>Tracks</c> needs room, a SeekHead or <c>TrackEntry</c> cannot be
	/// bounded, a stored FlagEnabled is longer than 8 octets, or the track's number or UID changed since it
	/// was read.
	/// </remarks>
	public bool IsEnabled {
		get => _isEnabled;
		set {
			_isEnabled = value;
			IsEnabledAssigned = true;
		}
	}

	/// <summary>Gets the zero-based position among the known-size <c>TrackEntry</c> elements.</summary>
	internal int Ordinal { get; }

	internal ulong RawTrackType { get; }

	/// <summary>
	/// Gets a value indicating whether the caller set <see cref="IsEnabled"/>.
	/// It stays set, so a value that was saved and then changed back is written again.
	/// </summary>
	internal bool IsEnabledAssigned { get; private set; }
}
