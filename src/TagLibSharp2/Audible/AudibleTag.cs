// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;

namespace TagLibSharp2.Audible;

/// <summary>
/// The text metadata dictionary of an Audible <c>.aa</c> file (table-of-contents chunk type 2).
/// </summary>
/// <remarks>
/// <para>
/// Layout, all integers big-endian:
/// </para>
/// <code>
/// u32 count
/// count x { u8 0x00, u32 keyLength, u32 valueLength, key bytes, value bytes }
/// </code>
/// <para>
/// Keys are ASCII and values are UTF-8, with no terminator and no padding. Entries keep their
/// file order, their leading byte, and their exact value bytes, so a save that changes nothing
/// writes the same bytes back.
/// </para>
/// <para>
/// There is no published specification for <c>.aa</c>. This layout is the one described in prose at
/// https://wiki.multimedia.cx/index.php?title=Audible_Audio (revision of 14 May 2025), and it was
/// confirmed byte by byte on three real files, whose key/value pairs match what ffprobe reports
/// for the same files.
/// </para>
/// <para>
/// Some keys carry the DRM state the player needs to decrypt the audio (<c>codec</c>,
/// <c>HeaderSeed</c>, <c>HeaderKey</c>, <c>EncryptedBlocks</c>). <see cref="SetValue"/> refuses to
/// change them and <see cref="Clear"/> keeps them, along with every other key it does not know.
/// </para>
/// </remarks>
public sealed class AudibleTag : Tag
{
	/// <summary>Dictionary key for the title.</summary>
	public const string TitleKey = "title";

	/// <summary>Dictionary key for the author.</summary>
	public const string AuthorKey = "author";

	/// <summary>Dictionary key for the narrator.</summary>
	public const string NarratorKey = "narrator";

	/// <summary>Dictionary key for the content provider.</summary>
	public const string ProviderKey = "provider";

	/// <summary>Dictionary key for the publication date (for example <c>20-JAN-2009</c>).</summary>
	public const string PublishDateKey = "pubdate";

	/// <summary>Dictionary key for the short description.</summary>
	public const string DescriptionKey = "description";

	/// <summary>Dictionary key for the long description.</summary>
	public const string LongDescriptionKey = "long_description";

	/// <summary>Dictionary key for the keywords.</summary>
	public const string KeywordsKey = "keywords";

	/// <summary>Dictionary key for the copyright notice.</summary>
	public const string CopyrightKey = "copyright";

	/// <summary>Dictionary key for the short title.</summary>
	public const string ShortTitleKey = "short_title";

	/// <summary>Dictionary key for the parent (series or collection) title.</summary>
	public const string ParentTitleKey = "parent_title";

	/// <summary>Dictionary key for the parent short title.</summary>
	public const string ParentShortTitleKey = "parent_short_title";

	/// <summary>Dictionary key for the product identifier.</summary>
	public const string ProductIdKey = "product_id";

	/// <summary>Dictionary key for the title identifier.</summary>
	public const string TitleIdKey = "title_id";

	/// <summary>Dictionary key for the codec name (<c>acelp85</c>, <c>acelp16</c>, <c>mp332</c>).</summary>
	public const string CodecKey = "codec";

	/// <summary>Dictionary key for the price.</summary>
	public const string PriceKey = "price";

	/// <summary>Dictionary key for the publication start date.</summary>
	public const string PublishDateStartKey = "pub_date_start";

	/// <summary>Dictionary key for the short description variant.</summary>
	public const string ShortDescriptionKey = "short_description";

	// The audio cannot be decrypted without these.
	static readonly HashSet<string> ProtectedKeys = new (StringComparer.Ordinal) {
		CodecKey, "HeaderSeed", "HeaderKey", "EncryptedBlocks"
	};

	// Clear() removes only these descriptive keys. Identifiers, licensing, and unknown keys stay.
	static readonly string[] DescriptiveKeys = [
		TitleKey, AuthorKey, NarratorKey, ProviderKey, PriceKey, PublishDateKey, PublishDateStartKey,
		DescriptionKey, ShortDescriptionKey, LongDescriptionKey, KeywordsKey, CopyrightKey,
		ShortTitleKey, ParentTitleKey, ParentShortTitleKey
	];

	// Each entry is a dictionary record in file order: the leading byte, the key, and the raw value bytes.
	readonly List<Entry> _entries = [];
	IPicture[] _pictures = [];

	// Bytes after the last entry but inside the chunk. None of the real files has any; they are kept
	// so that an unchanged save stays byte-identical.
	byte[] _trailer = [];

	// The key's own bytes are kept so a non-ASCII key in a damaged file is written back unchanged.
	sealed class Entry (byte lead, byte[] keyBytes, byte[] value)
	{
		public byte Lead { get; } = lead;
		public byte[] KeyBytes { get; } = keyBytes;
		public string Key { get; } = Encoding.ASCII.GetString (keyBytes);
		public byte[] Value { get; set; } = value;
	}

	/// <summary>
	/// Creates an empty dictionary.
	/// </summary>
	public AudibleTag ()
	{
	}

	/// <inheritdoc />
	public override TagTypes TagType => TagTypes.Audible;

	/// <inheritdoc />
	/// <remarks>Stored under <c>title</c>.</remarks>
	public override string? Title {
		get => GetValue (TitleKey);
		set => SetValue (TitleKey, value);
	}

	/// <inheritdoc />
	/// <remarks>Stored under <c>author</c>.</remarks>
	public override string? Artist {
		get => GetValue (AuthorKey);
		set => SetValue (AuthorKey, value);
	}

	/// <summary>
	/// Gets or sets the author. Same as <see cref="Artist"/>.
	/// </summary>
	public string? Author {
		get => GetValue (AuthorKey);
		set => SetValue (AuthorKey, value);
	}

	/// <summary>
	/// Gets or sets the narrator (<c>narrator</c>).
	/// </summary>
	public string? Narrator {
		get => GetValue (NarratorKey);
		set => SetValue (NarratorKey, value);
	}

	/// <summary>
	/// Gets or sets the content provider (<c>provider</c>).
	/// </summary>
	public string? Provider {
		get => GetValue (ProviderKey);
		set => SetValue (ProviderKey, value);
	}

	/// <summary>
	/// Gets or sets the publication date exactly as stored (<c>pubdate</c>, for example <c>20-JAN-2009</c>).
	/// </summary>
	public string? PublishDate {
		get => GetValue (PublishDateKey);
		set => SetValue (PublishDateKey, value);
	}

	/// <summary>
	/// Gets or sets the long description (<c>long_description</c>).
	/// </summary>
	public string? LongDescription {
		get => GetValue (LongDescriptionKey);
		set => SetValue (LongDescriptionKey, value);
	}

	/// <summary>
	/// Gets or sets the keywords (<c>keywords</c>).
	/// </summary>
	public string? Keywords {
		get => GetValue (KeywordsKey);
		set => SetValue (KeywordsKey, value);
	}

	/// <summary>
	/// Gets or sets the short title (<c>short_title</c>).
	/// </summary>
	public string? ShortTitle {
		get => GetValue (ShortTitleKey);
		set => SetValue (ShortTitleKey, value);
	}

	/// <summary>
	/// Gets or sets the parent title (<c>parent_title</c>).
	/// </summary>
	public string? ParentTitle {
		get => GetValue (ParentTitleKey);
		set => SetValue (ParentTitleKey, value);
	}

	/// <summary>
	/// Gets or sets the parent short title (<c>parent_short_title</c>).
	/// </summary>
	public string? ParentShortTitle {
		get => GetValue (ParentShortTitleKey);
		set => SetValue (ParentShortTitleKey, value);
	}

	/// <summary>
	/// Gets or sets the product identifier (<c>product_id</c>).
	/// </summary>
	public string? ProductId {
		get => GetValue (ProductIdKey);
		set => SetValue (ProductIdKey, value);
	}

	/// <summary>
	/// Gets or sets the title identifier (<c>title_id</c>).
	/// </summary>
	public string? TitleId {
		get => GetValue (TitleIdKey);
		set => SetValue (TitleIdKey, value);
	}

	/// <summary>
	/// Gets the codec name (<c>codec</c>). It describes the encrypted payload and cannot be changed.
	/// </summary>
	public string? Codec => GetValue (CodecKey);

	/// <inheritdoc />
	/// <remarks>The dictionary has no album key. This returns null and ignores a value.</remarks>
	public override string? Album {
		get => null;
		set { }
	}

	/// <inheritdoc />
	/// <remarks>
	/// The year at the end of <c>pubdate</c>. Setting a four-digit year keeps the day and month
	/// of a <c>DD-MON-YYYY</c> date. Any other value is stored in <c>pubdate</c> as given.
	/// </remarks>
	public override string? Year {
		get {
			var date = PublishDate;
			if (date is null || date.Length < 4)
				return null;
			var year = date.Substring (date.Length - 4);
			return IsFourDigits (year) ? year : null;
		}
		set {
			var date = PublishDate;
			if (value is not null && IsFourDigits (value) && date is { Length: 11 } && date[2] == '-' && date[6] == '-')
				PublishDate = date.Remove (7) + value;
			else
				PublishDate = value;
		}
	}

	static bool IsFourDigits (string value) =>
		value.Length == 4 && char.IsDigit (value[0]) && char.IsDigit (value[1]) && char.IsDigit (value[2]) && char.IsDigit (value[3]);

	/// <inheritdoc />
	/// <remarks>The dictionary has no comment key. This returns null and ignores a value. See <see cref="Description"/>.</remarks>
	public override string? Comment {
		get => null;
		set { }
	}

	/// <inheritdoc />
	/// <remarks>The dictionary has no genre key. This returns null and ignores a value.</remarks>
	public override string? Genre {
		get => null;
		set { }
	}

	/// <inheritdoc />
	/// <remarks>The dictionary has no track key. This returns null and ignores a value.</remarks>
	public override uint? Track {
		get => null;
		set { }
	}

	/// <inheritdoc />
	/// <remarks>Stored under <c>description</c>.</remarks>
	public override string? Description {
		get => GetValue (DescriptionKey);
		set => SetValue (DescriptionKey, value);
	}

	/// <inheritdoc />
	/// <remarks>Stored under <c>copyright</c>, as written by Audible (it may contain HTML entities such as <c>&amp;#169;</c>).</remarks>
	public override string? Copyright {
		get => GetValue (CopyrightKey);
		set => SetValue (CopyrightKey, value);
	}

	/// <inheritdoc />
	/// <remarks>
	/// The cover image from table-of-contents chunk type 11, when the file has one. It is read-only:
	/// a value set here is ignored and the cover chunk is saved unchanged.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - TagLib# API compatibility
	public override IPicture[] Pictures {
		get => _pictures;
		set { }
	}
#pragma warning restore CA1819

	/// <summary>
	/// Gets the dictionary keys in file order.
	/// </summary>
	public IReadOnlyList<string> Keys {
		get {
			var keys = new List<string> (_entries.Count);
			foreach (var entry in _entries)
				keys.Add (entry.Key);
			return keys;
		}
	}

	/// <summary>
	/// Gets the value stored under a key, decoded as UTF-8.
	/// </summary>
	/// <param name="key">The key, compared case-sensitively.</param>
	/// <returns>The first value for the key, or null when the key is absent.</returns>
	public string? GetValue (string key)
	{
		var entry = Find (key);
		return entry is null ? null : Encoding.UTF8.GetString (entry.Value);
	}

	/// <summary>
	/// Sets, adds, or removes the value stored under a key.
	/// </summary>
	/// <param name="key">The key, ASCII, compared case-sensitively.</param>
	/// <param name="value">The value, or null or empty to remove the key.</param>
	/// <exception cref="ArgumentException">The key is empty, not ASCII, or carries DRM state (<c>codec</c>, <c>HeaderSeed</c>, <c>HeaderKey</c>, <c>EncryptedBlocks</c>).</exception>
	/// <remarks>
	/// An existing key keeps its position. A new key is appended. Later duplicates of the key are removed.
	/// </remarks>
	public void SetValue (string key, string? value)
	{
		if (string.IsNullOrEmpty (key))
			throw new ArgumentException ("Key must not be empty.", nameof (key));
		foreach (var c in key) {
			if (c > 0x7F)
				throw new ArgumentException ("Key must be ASCII.", nameof (key));
		}
		if (ProtectedKeys.Contains (key))
			throw new ArgumentException ($"'{key}' is needed to decrypt the audio and cannot be changed.", nameof (key));

		var index = _entries.FindIndex (e => e.Key == key);
		if (string.IsNullOrEmpty (value)) {
			_entries.RemoveAll (e => e.Key == key);
			return;
		}

		var bytes = Encoding.UTF8.GetBytes (value);
		if (index < 0) {
			_entries.Add (new Entry (0, Encoding.ASCII.GetBytes (key), bytes));
			return;
		}

		_entries[index].Value = bytes;
		for (var i = _entries.Count - 1; i > index; i--) {
			if (_entries[i].Key == key)
				_entries.RemoveAt (i);
		}
	}

	Entry? Find (string key)
	{
		foreach (var entry in _entries) {
			if (entry.Key == key)
				return entry;
		}
		return null;
	}

	/// <inheritdoc />
	/// <remarks>True when none of the descriptive keys that <see cref="Clear"/> removes is present.</remarks>
	public override bool IsEmpty {
		get {
			foreach (var key in DescriptiveKeys) {
				if (Find (key) is not null)
					return false;
			}
			return true;
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Removes the descriptive keys (title, author, narrator, provider, price, dates, descriptions,
	/// keywords, copyright, and the short and parent titles). Identifiers, licensing and DRM keys,
	/// and unknown keys stay, so the audio can still be decrypted. The cover chunk is not changed.
	/// </remarks>
	public override void Clear ()
	{
		foreach (var key in DescriptiveKeys)
			_entries.RemoveAll (e => e.Key == key);
	}

	internal void SetPictures (IPicture[] pictures) => _pictures = pictures;

	/// <summary>
	/// Parses a dictionary chunk.
	/// </summary>
	/// <param name="data">The chunk bytes, starting at the entry count.</param>
	/// <returns>The parsed dictionary, or a failure when a count or length runs past the chunk.</returns>
	public static TagReadResult<AudibleTag> Parse (ReadOnlySpan<byte> data)
	{
		if (data.Length < 4)
			return TagReadResult<AudibleTag>.Failure ("Audible dictionary is shorter than its entry count");

		var count = BinaryPrimitives.ReadUInt32BigEndian (data);
		// Every entry is at least 9 bytes, so a count the chunk cannot hold is rejected before the loop.
		if (count > (uint)(data.Length - 4) / 9)
			return TagReadResult<AudibleTag>.Failure ($"Audible dictionary claims {count} entries in {data.Length} bytes");

		var tag = new AudibleTag ();
		var offset = 4;
		for (var i = 0u; i < count; i++) {
			if (data.Length - offset < 9)
				return TagReadResult<AudibleTag>.Failure ($"Audible dictionary entry {i} is truncated");

			var lead = data[offset];
			var keyLength = BinaryPrimitives.ReadUInt32BigEndian (data.Slice (offset + 1));
			var valueLength = BinaryPrimitives.ReadUInt32BigEndian (data.Slice (offset + 5));
			offset += 9;
			var remaining = (uint)(data.Length - offset);
			if (keyLength > remaining || valueLength > remaining - keyLength)
				return TagReadResult<AudibleTag>.Failure ($"Audible dictionary entry {i} runs past the chunk");

			var key = data.Slice (offset, (int)keyLength).ToArray ();
			offset += (int)keyLength;
			var value = data.Slice (offset, (int)valueLength).ToArray ();
			offset += (int)valueLength;
			tag._entries.Add (new Entry (lead, key, value));
		}

		tag._trailer = data.Slice (offset).ToArray ();
		return TagReadResult<AudibleTag>.Success (tag, offset);
	}

	/// <inheritdoc />
	/// <remarks>Renders the dictionary chunk: the entry count, then every entry in order, then any bytes that followed the entries when it was read.</remarks>
	public override BinaryData Render ()
	{
		var size = 4L + _trailer.Length;
		foreach (var entry in _entries)
			size += 9L + entry.KeyBytes.Length + entry.Value.Length;
		if (size > int.MaxValue)
			throw new InvalidOperationException ("Audible dictionary is too large to render.");

		var output = new byte[size];
		BinaryPrimitives.WriteUInt32BigEndian (output, (uint)_entries.Count);
		var offset = 4;
		foreach (var entry in _entries) {
			output[offset] = entry.Lead;
			BinaryPrimitives.WriteUInt32BigEndian (output.AsSpan (offset + 1), (uint)entry.KeyBytes.Length);
			BinaryPrimitives.WriteUInt32BigEndian (output.AsSpan (offset + 5), (uint)entry.Value.Length);
			offset += 9;
			entry.KeyBytes.CopyTo (output, offset);
			offset += entry.KeyBytes.Length;
			entry.Value.CopyTo (output, offset);
			offset += entry.Value.Length;
		}
		_trailer.CopyTo (output, offset);

		return new BinaryData (output);
	}
}
