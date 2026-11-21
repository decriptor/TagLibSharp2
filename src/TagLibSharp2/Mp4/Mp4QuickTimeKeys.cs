// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Where the meta atom of an <see cref="Mp4Tag"/> sits.
/// </summary>
internal enum Mp4MetaPlacement
{
	/// <summary>moov/udta/meta, where iTunes and ffmpeg write it.</summary>
	UserData,

	/// <summary>moov/meta, where QuickTime and Apple devices write it.</summary>
	Movie
}

/// <summary>
/// What a QuickTime metadata key stands for in the iTunes-style model of <see cref="Mp4Tag"/>.
/// </summary>
internal enum Mp4KeyedKind
{
	/// <summary>A standard ilst atom such as ©nam or trkn.</summary>
	Atom,

	/// <summary>A freeform (----) field, matched by its name.</summary>
	Freeform,

	/// <summary>Cover art (covr).</summary>
	Artwork
}

/// <summary>
/// A metadata field that a QuickTime key names.
/// </summary>
internal readonly struct Mp4KeyedField
{
	Mp4KeyedField (Mp4KeyedKind kind, string atomId, string? mean, string? name)
	{
		Kind = kind;
		AtomId = atomId;
		Mean = mean;
		Name = name;
	}

	public Mp4KeyedKind Kind { get; }

	/// <summary>The iTunes atom (covr for artwork, ---- for freeform fields).</summary>
	public string AtomId { get; }

	public string? Mean { get; }

	public string? Name { get; }

	public static Mp4KeyedField Atom (string atomId) => new (Mp4KeyedKind.Atom, atomId, null, null);

	public static Mp4KeyedField Freeform (string mean, string name) => new (Mp4KeyedKind.Freeform, Mp4AtomMapping.FreeformAtom, mean, name);

	public static Mp4KeyedField Artwork () => new (Mp4KeyedKind.Artwork, Mp4AtomMapping.CoverArt, null, null);
}

/// <summary>
/// The keys of a QuickTime metadata atom whose handler is "mdta".
/// </summary>
/// <remarks>
/// <para>
/// In such a meta, each ilst item's type is a 1-based index into the keys atom, and 0 is
/// reserved (https://developer.apple.com/documentation/quicktime-file-format/metadata_item_keys_atom).
/// A reader confirms the handler is "mdta" before interpreting the other atoms
/// (https://developer.apple.com/documentation/quicktime-file-format/metadata_handler_atom).
/// A key is key_size (4), key_namespace (4) and key_value. An "mdta" namespace value is a
/// key name; a "udta" namespace value is a 4-byte user data atom type.
/// </para>
/// <para>
/// Key names are Apple's com.apple.quicktime.* keys
/// (https://developer.apple.com/documentation/quicktime-file-format/quicktime_metadata_keys) and
/// the names ffmpeg writes with -movflags use_metadata_tags, which are its own metadata names.
/// Those carry text, so track, disc and flag values are "n/m" or "1" in UTF-8.
/// </para>
/// </remarks>
internal sealed class Mp4QuickTimeKeys
{
	public const uint MdtaNamespace = 0x6D647461; // "mdta"
	public const uint UdtaNamespace = 0x75647461; // "udta"
	const string ApplePrefix = "com.apple.quicktime.";

	// Apple's keys, without the prefix.
	static readonly Dictionary<string, string> AppleKeys = new (StringComparer.Ordinal) {
		["title"] = Mp4AtomMapping.Title,
		["displayname"] = Mp4AtomMapping.Title,
		["artist"] = Mp4AtomMapping.Artist,
		["album"] = Mp4AtomMapping.Album,
		["comment"] = Mp4AtomMapping.Comment,
		["copyright"] = Mp4AtomMapping.Copyright,
		["description"] = Mp4AtomMapping.Description,
		["genre"] = Mp4AtomMapping.Genre,
		["publisher"] = Mp4AtomMapping.Publisher,
		["year"] = Mp4AtomMapping.Year,
		["software"] = Mp4AtomMapping.Encoder,
		["keywords"] = Mp4AtomMapping.Keywords,
	};

	// The names ffmpeg gives these atoms when it writes them as keys.
	static readonly Dictionary<string, string> PlainKeys = new (StringComparer.Ordinal) {
		["title"] = Mp4AtomMapping.Title,
		["artist"] = Mp4AtomMapping.Artist,
		["album"] = Mp4AtomMapping.Album,
		["album_artist"] = Mp4AtomMapping.AlbumArtist,
		["composer"] = Mp4AtomMapping.Composer,
		["date"] = Mp4AtomMapping.Year,
		["comment"] = Mp4AtomMapping.Comment,
		["genre"] = Mp4AtomMapping.Genre,
		["copyright"] = Mp4AtomMapping.Copyright,
		["description"] = Mp4AtomMapping.Description,
		["grouping"] = Mp4AtomMapping.Grouping,
		["lyrics"] = Mp4AtomMapping.Lyrics,
		["encoder"] = Mp4AtomMapping.Encoder,
		["encoded_by"] = Mp4AtomMapping.EncodedBy,
		["publisher"] = Mp4AtomMapping.Publisher,
		["sort_name"] = Mp4AtomMapping.TitleSort,
		["sort_artist"] = Mp4AtomMapping.ArtistSort,
		["sort_album"] = Mp4AtomMapping.AlbumSort,
		["sort_album_artist"] = Mp4AtomMapping.AlbumArtistSort,
		["sort_composer"] = Mp4AtomMapping.ComposerSort,
		["category"] = Mp4AtomMapping.Category,
		["keywords"] = Mp4AtomMapping.Keywords,
		["track"] = Mp4AtomMapping.TrackNumber,
		["disc"] = Mp4AtomMapping.DiscNumber,
		["compilation"] = Mp4AtomMapping.Compilation,
		["gapless_playback"] = Mp4AtomMapping.GaplessPlayback,
		["podcast"] = Mp4AtomMapping.PodcastFlag,
		["rating"] = Mp4AtomMapping.ContentRating,
	};

	// Freeform fields are keyed by their name alone; the mean is the one the property uses.
	static readonly Dictionary<string, string> FreeformMeans = BuildFreeformMeans ();

	static readonly Dictionary<string, string> AppleKeyByAtom = Invert (AppleKeys);
	static readonly Dictionary<string, string> PlainKeyByAtom = Invert (PlainKeys);

	readonly uint _versionAndFlags;
	int _readCount = -1;
	readonly List<(uint Namespace, BinaryData Value)> _keys = new ();
	readonly Dictionary<(uint Namespace, string Value), uint> _indexByKey = new ();

	Mp4QuickTimeKeys (uint versionAndFlags) => _versionAndFlags = versionAndFlags;

	/// <summary>
	/// Gets the number of keys.
	/// </summary>
	public int Count => _keys.Count;

	/// <summary>
	/// True when the file names keys the way Apple does, so a new key does too.
	/// </summary>
	bool AppleStyle { get; set; }

	/// <summary>
	/// Reads the keys of a meta atom. Returns null when its handler is not "mdta", or when the keys
	/// atom is damaged: then no item can be resolved, and a save keeps the items and the keys atom
	/// as they are rather than give the items a meaning they did not have.
	/// </summary>
	public static Mp4QuickTimeKeys? Read (Mp4Box meta)
	{
		var hdlr = meta.FindChild ("hdlr");
		if (hdlr is null || hdlr.Data.Length < 12 || !hdlr.Data.Span.Slice (8, 4).SequenceEqual ("mdta"u8))
			return null;

		var keysBox = meta.FindChild ("keys");
		if (keysBox is null)
			return meta.FindChild ("ilst") is null ? new Mp4QuickTimeKeys (0) : null;

		var span = keysBox.Data.Span;
		if (span.Length < 8)
			return null;

		var keys = new Mp4QuickTimeKeys (BinaryPrimitives.ReadUInt32BigEndian (span));
		var count = BinaryPrimitives.ReadUInt32BigEndian (span.Slice (4, 4));
		var offset = 8;
		while (keys.Count < count) {
			// An entry smaller than its own 8-byte header, or past the end, is damage.
			if (span.Length - offset < 8)
				return null;

			var size = BinaryPrimitives.ReadUInt32BigEndian (span.Slice (offset, 4));
			if (size < 8 || size > span.Length - offset)
				return null;

			var ns = BinaryPrimitives.ReadUInt32BigEndian (span.Slice (offset + 4, 4));
			keys.Add (ns, new BinaryData (span.Slice (offset + 8, (int)size - 8)));
			offset += (int)size;
		}

		return keys;
	}

	/// <summary>
	/// Indexes added since the keys were read.
	/// </summary>
	public bool IsAdded (uint index) => _readCount >= 0 && index > (uint)_readCount && index <= (uint)_keys.Count;

	/// <summary>
	/// Resolves an ilst item type to the field its key names.
	/// </summary>
	public bool TryResolve (uint itemType, out Mp4KeyedField field)
	{
		field = default;
		if (itemType == 0 || itemType > (uint)_keys.Count)
			return false;

		var (ns, value) = _keys[(int)itemType - 1];
		return TryMap (ns, value.Span, out field);
	}

	/// <summary>
	/// True when the key holds text (an "mdta" name), so numbers and flags are written as text.
	/// </summary>
	public bool IsTextKey (uint index) =>
		index >= 1 && index <= (uint)_keys.Count && _keys[(int)index - 1].Namespace == MdtaNamespace;

	/// <summary>
	/// Returns the index of the key for a field, adding a key when the file has none for it.
	/// </summary>
	public uint IndexFor (Mp4KeyedField field)
	{
		var (ns, value) = KeyFor (field);
		if (_indexByKey.TryGetValue ((ns, value), out var index))
			return index;

		if (_readCount < 0)
			_readCount = _keys.Count;

		var bytes = ns == MdtaNamespace ? Encoding.UTF8.GetBytes (value) : Polyfills.Latin1.GetBytes (value);
		return Add (ns, new BinaryData (bytes));
	}

	/// <summary>
	/// Drops every key. Only valid when no ilst item still refers to one.
	/// </summary>
	public void Clear ()
	{
		_readCount = 0;
		_keys.Clear ();
		_indexByKey.Clear ();
	}

	/// <summary>
	/// Renders the keys atom payload: version and flags, entry count, then the entries.
	/// </summary>
	public BinaryData Render ()
	{
		using var builder = new BinaryDataBuilder ();
		builder.AddUInt32BE (_versionAndFlags);
		builder.AddUInt32BE ((uint)_keys.Count);
		foreach (var (ns, value) in _keys) {
			builder.AddUInt32BE ((uint)(8 + value.Length));
			builder.AddUInt32BE (ns);
			builder.Add (value);
		}

		return builder.ToBinaryData ();
	}

	uint Add (uint ns, BinaryData value)
	{
		_keys.Add ((ns, value));
		var index = (uint)_keys.Count;
		var name = ns == MdtaNamespace ? Encoding.UTF8.GetString (value.ToArray ()) : Polyfills.Latin1.GetString (value.ToArray ());
		if (!_indexByKey.TryGetValue ((ns, name), out _))
			_indexByKey[(ns, name)] = index;
		if (ns == MdtaNamespace && name.StartsWith (ApplePrefix, StringComparison.Ordinal))
			AppleStyle = true;
		return index;
	}

	(uint Namespace, string Value) KeyFor (Mp4KeyedField field)
	{
		switch (field.Kind) {
			case Mp4KeyedKind.Artwork:
				return (MdtaNamespace, ApplePrefix + "artwork");
			case Mp4KeyedKind.Freeform:
				return (MdtaNamespace, field.Name!);
		}

		if (AppleStyle && AppleKeyByAtom.TryGetValue (field.AtomId, out var apple))
			return (MdtaNamespace, ApplePrefix + apple);
		if (PlainKeyByAtom.TryGetValue (field.AtomId, out var plain))
			return (MdtaNamespace, plain);
		if (AppleKeyByAtom.TryGetValue (field.AtomId, out apple))
			return (MdtaNamespace, ApplePrefix + apple);

		// No name is known, so name the iTunes atom itself through the "udta" namespace.
		return (UdtaNamespace, field.AtomId);
	}

	static bool TryMap (uint ns, ReadOnlySpan<byte> value, out Mp4KeyedField field)
	{
		field = default;
		if (ns == UdtaNamespace) {
			if (value.Length != 4)
				return false;

			// A user data atom type: the key stands for that iTunes atom, when the tag reads it.
			var atomId = Polyfills.Latin1.GetString (value.ToArray ());
			if (!Mp4AtomMapping.IsTagAtom (atomId))
				return false;

			field = atomId == Mp4AtomMapping.CoverArt ? Mp4KeyedField.Artwork () : Mp4KeyedField.Atom (atomId);
			return true;
		}

		if (ns != MdtaNamespace)
			return false;

		var name = Encoding.UTF8.GetString (value.ToArray ());
		if (name.StartsWith (ApplePrefix, StringComparison.Ordinal)) {
			var suffix = name.Substring (ApplePrefix.Length);
			if (suffix == "artwork") {
				field = Mp4KeyedField.Artwork ();
				return true;
			}

			if (AppleKeys.TryGetValue (suffix, out var appleAtom)) {
				field = Mp4KeyedField.Atom (appleAtom);
				return true;
			}

			return false;
		}

		if (PlainKeys.TryGetValue (name, out var atom)) {
			field = Mp4KeyedField.Atom (atom);
			return true;
		}

		if (FreeformMeans.TryGetValue (name, out var mean)) {
			field = Mp4KeyedField.Freeform (mean, name);
			return true;
		}

		return false;
	}

	static Dictionary<string, string> Invert (Dictionary<string, string> map)
	{
		var inverted = new Dictionary<string, string> (StringComparer.Ordinal);
		foreach (var pair in map) {
			if (!inverted.TryGetValue (pair.Value, out _))
				inverted[pair.Value] = pair.Key;
		}

		return inverted;
	}

	static Dictionary<string, string> BuildFreeformMeans ()
	{
		var means = new Dictionary<string, string> (StringComparer.Ordinal);
		foreach (var name in new[] {
			Mp4AtomMapping.MusicBrainzTrackId, Mp4AtomMapping.MusicBrainzAlbumId, Mp4AtomMapping.MusicBrainzArtistId,
			Mp4AtomMapping.MusicBrainzAlbumArtistId, Mp4AtomMapping.MusicBrainzReleaseGroupId, Mp4AtomMapping.MusicBrainzWorkId,
			Mp4AtomMapping.MusicBrainzRecordingId, Mp4AtomMapping.MusicBrainzDiscId, Mp4AtomMapping.MusicBrainzReleaseStatus,
			Mp4AtomMapping.MusicBrainzReleaseType, Mp4AtomMapping.MusicBrainzReleaseCountry })
			means.Add (name, Mp4AtomMapping.MusicBrainzNamespace);

		foreach (var name in new[] {
			Mp4AtomMapping.AcoustIdId, Mp4AtomMapping.AcoustIdFingerprint, Mp4AtomMapping.Isrc, Mp4AtomMapping.Conductor,
			Mp4AtomMapping.OriginalYear, Mp4AtomMapping.GaplessInfo, Mp4AtomMapping.InitialKey, Mp4AtomMapping.Remixer,
			Mp4AtomMapping.Mood, Mp4AtomMapping.Subtitle, Mp4AtomMapping.Barcode, Mp4AtomMapping.CatalogNumber,
			Mp4AtomMapping.AmazonId, Mp4AtomMapping.DateTagged, Mp4AtomMapping.Language, Mp4AtomMapping.MediaType,
			Mp4AtomMapping.ReplayGainTrackGain, Mp4AtomMapping.ReplayGainTrackPeak, Mp4AtomMapping.ReplayGainAlbumGain,
			Mp4AtomMapping.ReplayGainAlbumPeak, Mp4AtomMapping.R128TrackGain, Mp4AtomMapping.R128AlbumGain })
			means.Add (name, Mp4AtomMapping.AppleNamespace);

		return means;
	}
}
