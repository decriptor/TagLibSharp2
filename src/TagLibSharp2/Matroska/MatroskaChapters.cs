// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// The <c>Chapters</c> element of a Matroska or WebM segment: editions of chapter atoms, which can nest.
/// </summary>
/// <remarks>
/// <para>
/// Elements and defaults follow RFC 9559 section 5.1.7 and section 20
/// (https://www.rfc-editor.org/rfc/rfc9559#section-5.1.7). <c>EditionFlagHidden</c> (0x45BD) and
/// <c>ChapterFlagEnabled</c> (0x4598) come from the Matroska element schema
/// (https://github.com/ietf-wg-cellar/matroska-specification/blob/master/ebml_matroska.xml).
/// </para>
/// <para>
/// WebM carries a subset of these elements (https://www.webmproject.org/docs/container/#chapters).
/// When a WebM file's chapters are written, every chapter element the guidelines list as unsupported
/// is left out, on every chapter, edited or not: <c>EditionUID</c>, the edition flags,
/// <c>ChapterFlagHidden</c>, <c>ChapterFlagEnabled</c>, <c>ChapLanguageBCP47</c>, and kept children
/// such as <c>EditionDisplay</c>, <c>ChapterSegmentUUID</c>, <c>ChapterTrack</c>, and <c>ChapProcess</c>.
/// A hidden or disabled chapter in a WebM file is therefore written as a plain one. Bytes kept past the
/// depth or entry limit are filtered only at their top level.
/// </para>
/// <para>
/// A chapter title names a chapter, not the file, so it never becomes <see cref="MatroskaTag.Title"/>.
/// A save writes <c>Chapters</c> again only when an edition or chapter was added, removed, or given a
/// value other than the one read. In a Matroska file, every edition and chapter that was not changed is
/// then written back byte for byte; a WebM file is rewritten to the WebM subset.
/// Otherwise the original element bytes stay where they are.
/// Child elements this type does not model are kept and written back; a rewritten parent drops its Void and CRC-32.
/// A save fails, writing nothing, when a chapter it adds or changes breaks an RFC 9559 timing rule, or
/// any chapter of an edition whose own values (UID, flags) changed does. The error names the chapter by
/// position and UID. A chapter that was not changed is not checked, so a bad one from another tool does
/// not block other edits.
/// </para>
/// <para>
/// Untrusted input is bounded: atoms nest at most <c>32</c> levels, and at most 65,536 child elements are
/// parsed. Past either limit the bytes are kept, the rest of a parent as one copy, so memory stays linear.
/// </para>
/// </remarks>
public sealed class MatroskaChapters
{
	/// <summary>Chapter atoms nested deeper than this are kept as bytes and not parsed.</summary>
	internal const int MaxDepth = 32;

	/// <summary>
	/// Child elements parsed across the whole <c>Chapters</c> element. Once they are spent, the rest of
	/// each parent is kept as one copy of its bytes, so memory stays linear in the input.
	/// </summary>
	internal const int MaxEntries = 65_536;

	/// <summary>
	/// The chapter elements WebM supports (https://www.webmproject.org/docs/container/#chapters).
	/// A WebM save writes only these, whether the model holds the value or kept the element as bytes.
	/// </summary>
	static readonly HashSet<ulong> WebMElements = [
		MatroskaIds.EditionEntry,
		MatroskaIds.ChapterAtom,
		MatroskaIds.ChapterUid,
		MatroskaIds.ChapterStringUid,
		MatroskaIds.ChapterTimeStart,
		MatroskaIds.ChapterTimeEnd,
		MatroskaIds.ChapterDisplay,
		MatroskaIds.ChapString,
		MatroskaIds.ChapLanguage,
		MatroskaIds.ChapCountry
	];

	readonly List<MatroskaEdition> _editions = new ();
	readonly List<KeptElement> _kept = new ();
	// The editions as read, to tell an edit (including a removal through the list) from a no-op.
	MatroskaEdition?[] _readEditions = [];
	bool _hadElement;
	bool _cleared;

	internal MatroskaChapters (bool isWebM) => IsWebM = isWebM;

	/// <summary>Gets a value indicating whether the file is WebM, which keeps only part of the chapter elements.</summary>
	public bool IsWebM { get; }

	/// <summary>Gets the editions in file order.</summary>
	public IList<MatroskaEdition> Editions => _editions;

	/// <summary>Gets a value indicating whether no edition has a chapter. Such a file is saved without <c>Chapters</c>.</summary>
	public bool IsEmpty {
		get {
			foreach (var edition in _editions) {
				if (edition is not null && edition.HasAtoms)
					return false;
			}

			return !KeptHas (_kept, MatroskaIds.EditionEntry);
		}
	}

	/// <summary>
	/// Gets the edition a player uses by default: the first with <see cref="MatroskaEdition.IsDefault"/> set,
	/// otherwise the first edition (RFC 9559 section 20.1.2). Null when there are no editions.
	/// </summary>
	public MatroskaEdition? DefaultEdition {
		get {
			MatroskaEdition? first = null;
			foreach (var edition in _editions) {
				if (edition is null)
					continue;
				if (edition.IsDefault)
					return edition;
				first ??= edition;
			}

			return first;
		}
	}

	/// <summary>Adds an empty edition with a new random <see cref="MatroskaEdition.Uid"/>.</summary>
	/// <returns>The new edition. It is not written until it has a chapter.</returns>
	public MatroskaEdition AddEdition ()
	{
		var edition = new MatroskaEdition ();
		_editions.Add (edition);
		return edition;
	}

	/// <summary>Removes an edition and its chapters.</summary>
	/// <returns>True when the edition was found.</returns>
	/// <remarks>The next save removes the <c>Chapters</c> element when no edition with a chapter is left.</remarks>
	public bool RemoveEdition (MatroskaEdition edition) => _editions.Remove (edition);

	/// <summary>
	/// Adds a chapter to <see cref="DefaultEdition"/>, creating an edition when there is none.
	/// </summary>
	/// <param name="start">The chapter start.</param>
	/// <param name="end">The chapter end, which is excluded, or null to leave it out.</param>
	/// <param name="title">The display string, or null for no display.</param>
	/// <param name="language">The Matroska language of <paramref name="title"/>.</param>
	/// <returns>The new chapter with a new random <see cref="MatroskaChapter.Uid"/>.</returns>
	public MatroskaChapter AddChapter (TimeSpan start, TimeSpan? end = null, string? title = null, string language = "eng") =>
		(DefaultEdition ?? AddEdition ()).AddChapter (start, end, title, language);

	/// <summary>
	/// Removes every edition. The next save removes the <c>Chapters</c> element, even one that held
	/// only editions without chapters.
	/// </summary>
	public void Clear ()
	{
		_editions.Clear ();
		_kept.Clear ();
		_cleared = true;
	}

	/// <summary>
	/// Gets a value indicating whether a save has to write <c>Chapters</c>: an edition was added, removed,
	/// or reordered, or an edition or chapter holds a value other than the one read. Setting a value back
	/// to what was read is not an edit. A removed edition without chapters is an edit too, so
	/// <see cref="Clear"/> and <see cref="RemoveEdition"/> always drop it from the file.
	/// </summary>
	internal bool IsModified {
		get {
			// A Chapters element with only a Void or other unmodelled children has nothing to compare, so Clear says it.
			var modified = !SameItems (_editions, _readEditions) || _kept.Count != _readKept || (_cleared && _hadElement);
			foreach (var edition in _editions) {
				if (edition is not null && !edition.Prepare ())
					modified = true;
			}

			return modified;
		}
	}

	int _readKept;

	/// <summary>
	/// Checks the RFC 9559 rules on chapter times for every chapter this save adds or changes: an end is
	/// not before its start (section 20.2.2), a nested chapter starts inside its parent (section 20.2.3),
	/// and every lowest-level chapter of an ordered edition has an end (section 20.2.4 and Table 29).
	/// A chapter that was not changed is not checked, so a bad chapter written by another tool does not
	/// block other edits; in a Matroska file it is written back as it was. An edition that is new or whose
	/// own values changed has all its chapters checked. The error names the chapter by position and UID.
	/// </summary>
	internal bool TryValidate (out string error)
	{
		for (var e = 0; e < _editions.Count; e++) {
			var edition = _editions[e];
			if (edition is null)
				continue;
			var all = edition.ValuesChanged;
			var chapters = edition.Chapters;
			for (var c = 0; c < chapters.Count; c++) {
				var path = "edition " + (e + 1).ToString (System.Globalization.CultureInfo.InvariantCulture)
					+ ", chapter " + (c + 1).ToString (System.Globalization.CultureInfo.InvariantCulture);
				if (chapters[c] is { } chapter && !chapter.TryValidate (edition.IsOrdered, all, null, false, path, 1, out error))
					return false;
			}
		}

		error = "";
		return true;
	}

	internal static bool SameItems<T> (IList<T> current, T?[] read) where T : class
	{
		if (current.Count != read.Length)
			return false;
		for (var i = 0; i < read.Length; i++) {
			if (!ReferenceEquals (current[i], read[i]))
				return false;
		}

		return true;
	}

	/// <summary>Writes one <c>Chapters</c> element.</summary>
	/// <returns>The element, or empty when no edition has a chapter.</returns>
	internal BinaryData Render ()
	{
		if (!HasContent)
			return BinaryData.Empty;
		var measure = ChapterWriter.Measure ();
		Write (measure);
		var write = ChapterWriter.ToBuffer (measure);
		Write (write);
		return BinaryData.WrapUnsafe (write.Buffer!);
	}

	/// <summary>RFC 9559 section 20.1: Chapters needs an EditionEntry, parsed or kept.</summary>
	bool HasContent {
		get {
			foreach (var edition in _editions) {
				if (edition is not null && edition.HasAtoms)
					return true;
			}

			return KeptHas (_kept, MatroskaIds.EditionEntry);
		}
	}

	void Write (ChapterWriter writer)
	{
		foreach (var edition in _editions)
			edition?.Prepare ();

		writer.Begin (MatroskaIds.Chapters);
		var anchor = LastRead (_editions);
		if (anchor < 0)
			AddKept (writer, _kept, IsWebM, rest: true);
		for (var i = 0; i < _editions.Count; i++) {
			var edition = _editions[i];
			if (edition is not null && edition.HasAtoms)
				edition.Write (writer, IsWebM);
			if (i == anchor)
				AddKept (writer, _kept, IsWebM, rest: true);
		}

		// Editions past the entry limit, and children this type does not model, go back as they were.
		AddKept (writer, _kept, IsWebM);
		writer.End ();
	}

	/// <summary>Starts an empty set whose baseline is no <c>Chapters</c> element.</summary>
	internal static MatroskaChapters Empty (bool isWebM) => new (isWebM);

	/// <summary>Parses a <c>Chapters</c> payload. Malformed children end their parent and do not throw.</summary>
	internal static MatroskaChapters Read (ReadOnlySpan<byte> body, bool isWebM)
	{
		// One copy of the element, so an untouched edition or chapter is written back byte for byte.
		var source = body.ToArray ();
		var chapters = new MatroskaChapters (isWebM) { _hadElement = true };
		var budget = MaxEntries;
		var offset = 0;
		while (TryNext (source, ref offset, ref budget, chapters._kept, out var element)) {
			if (element.Id == MatroskaIds.EditionEntry)
				chapters._editions.Add (MatroskaEdition.Read (source, element, ref budget));
			else
				KeepUnknown (chapters._kept, source, element);
		}

		chapters._readEditions = chapters._editions.ToArray ();
		chapters._readKept = chapters._kept.Count;
		return chapters;
	}

	static readonly RandomNumberGenerator Random = RandomNumberGenerator.Create ();

	[ThreadStatic]
	static byte[]? _uidBytes;

	/// <summary>A random UID. RFC 9559 gives the UID elements the range "not 0".</summary>
	internal static ulong NewUid ()
	{
		var bytes = _uidBytes ??= new byte[8];
		ulong value;
		do {
			// RandomNumberGenerator instance methods are thread-safe.
			Random.GetBytes (bytes);
			value = EbmlReader.ReadUInt (bytes);
		} while (value == 0);

		return value;
	}

	internal static ReadOnlySpan<byte> Payload (ReadOnlySpan<byte> body, EbmlElement element) =>
		body.Slice (element.DataOffset, (int)element.DataLength);

	internal static bool ReadFlag (ReadOnlySpan<byte> payload, bool defaultValue) =>
		payload.Length == 0 ? defaultValue : EbmlReader.ReadUInt (payload) != 0;

	/// <summary>
	/// Reads the next child of a parent and spends one entry on it. Void and CRC-32 are skipped: a changed
	/// parent gets neither, since a CRC-32 MUST be the first child and cover the new bytes (RFC 8794 section 11.3.1).
	/// When the budget is spent, the rest of the parent goes into <paramref name="kept"/> as one copy.
	/// </summary>
	/// <returns>False at the end of the parent, at bytes that do not parse, or once the rest was kept.</returns>
	internal static bool TryNext (ReadOnlySpan<byte> body, ref int offset, ref int budget, List<KeptElement> kept, out EbmlElement element)
	{
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out element) || element.UnknownSize)
				break;
			if (element.Id is MatroskaIds.Void or MatroskaIds.Crc32) {
				offset = element.End;
				continue;
			}

			if (budget <= 0) {
				KeepRest (body, offset, kept);
				offset = body.Length;
				break;
			}

			budget--;
			offset = element.End;
			return true;
		}

		element = default;
		return false;
	}

	/// <summary>Keeps every parseable element from <paramref name="offset"/> on, without Void and CRC-32, as one copy.</summary>
	static void KeepRest (ReadOnlySpan<byte> body, int offset, List<KeptElement> kept)
	{
		// Measured first, so the copy is one exact allocation.
		var length = 0;
		for (var at = offset; at < body.Length && EbmlReader.TryRead (body, at, body.Length, out var element) && !element.UnknownSize; at = element.End) {
			if (element.Id is not MatroskaIds.Void and not MatroskaIds.Crc32)
				length += element.End - element.Position;
		}

		if (length == 0)
			return;
		var rest = new byte[length];
		var written = 0;
		for (var at = offset; at < body.Length && EbmlReader.TryRead (body, at, body.Length, out var element) && !element.UnknownSize; at = element.End) {
			if (element.Id is MatroskaIds.Void or MatroskaIds.Crc32)
				continue;
			body.Slice (element.Position, element.End - element.Position).CopyTo (rest.AsSpan (written));
			written += element.End - element.Position;
		}

		kept.Add (new KeptElement (rest, isRest: true));
	}

	internal static void KeepUnknown (List<KeptElement> kept, ReadOnlySpan<byte> body, EbmlElement element) =>
		kept.Add (new KeptElement (body.Slice (element.Position, element.End - element.Position).ToArray ()));

	/// <summary>
	/// Writes chapter atoms in list order. The bytes kept past the entry budget follow the last atom
	/// that was read from the file, so added atoms still come after them.
	/// </summary>
	internal static void WriteChapters (ChapterWriter writer, IList<MatroskaChapter> chapters, List<KeptElement> kept, bool isWebM, int depth)
	{
		var anchor = LastRead (chapters);
		if (anchor < 0)
			AddKept (writer, kept, isWebM, rest: true);
		for (var i = 0; i < chapters.Count; i++) {
			chapters[i]?.Write (writer, isWebM, depth);
			if (i == anchor)
				AddKept (writer, kept, isWebM, rest: true);
		}
	}

	static int LastRead<T> (IList<T> items) where T : class, IReadFromFile
	{
		for (var i = items.Count - 1; i >= 0; i--) {
			if (items[i] is { IsFromFile: true })
				return i;
		}

		return -1;
	}

	/// <summary>Returns true when a kept copy holds an element with <paramref name="id"/> at its top level.</summary>
	internal static bool KeptHas (List<KeptElement> kept, ulong id)
	{
		foreach (var element in kept) {
			if (id == MatroskaIds.ChapterAtom ? element.HasAtom : id == MatroskaIds.EditionEntry && element.HasEdition)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Writes the kept copies: the unmodelled children, or with <paramref name="rest"/> the bytes past the
	/// entry budget, which follow the modelled children as they did in the file. A WebM save leaves out
	/// every top-level element WebM does not support.
	/// </summary>
	internal static void AddKept (ChapterWriter builder, List<KeptElement> kept, bool isWebM, bool rest = false)
	{
		foreach (var element in kept) {
			if (element.IsRest != rest)
				continue;
			if (!isWebM) {
				builder.Add (element.Bytes);
				continue;
			}

			var bytes = element.Bytes;
			var offset = 0;
			while (offset < bytes.Length && EbmlReader.TryRead (bytes, offset, bytes.Length, out var child) && !child.UnknownSize) {
				if (WebMElements.Contains (child.Id))
					builder.Add (bytes.AsSpan (child.Position, child.End - child.Position));
				offset = child.End;
			}
		}
	}
}

/// <summary>One <c>EditionEntry</c>: a set of chapters a player can choose.</summary>
public sealed class MatroskaEdition : IReadFromFile
{
	readonly List<MatroskaChapter> _chapters = new ();
	readonly List<KeptElement> _kept = new ();

	MatroskaEdition (ulong uid) => Uid = uid;

	bool IReadFromFile.IsFromFile => _fromFile;

	bool _fromFile;
	byte[]? _source;
	int _sourceAt;
	int _sourceLength;
	string? _readState;
	MatroskaChapter?[] _readChapters = [];
	bool _unchanged;

	string OwnState => string.Join ("|", Uid, IsDefault, IsOrdered, IsHidden);

	/// <summary>Gets a value indicating whether the edition is new or its own values (UID, flags) changed.</summary>
	internal bool ValuesChanged => !_fromFile || OwnState != _readState;

	/// <summary>Gets a value indicating whether the edition is new, or its own values or chapter list changed.</summary>
	internal bool OwnChanged => ValuesChanged || !MatroskaChapters.SameItems (_chapters, _readChapters);

	/// <summary>Works out, for this save, which parts are unchanged. Returns true when the whole edition is.</summary>
	internal bool Prepare ()
	{
		var unchanged = !OwnChanged;
		foreach (var chapter in _chapters) {
			if (chapter is not null && !chapter.Prepare (1))
				unchanged = false;
		}

		_unchanged = unchanged && _source is not null;
		return _unchanged;
	}

	/// <summary>Creates an edition with a new random <see cref="Uid"/> and no chapters.</summary>
	public MatroskaEdition () : this (MatroskaChapters.NewUid ())
	{
	}

	/// <summary>Gets or sets the <c>EditionUID</c>, or 0 to leave it out. WebM does not store it.</summary>
	public ulong Uid { get; set; }

	/// <summary>Gets or sets <c>EditionFlagDefault</c>. The default is false.</summary>
	public bool IsDefault { get; set; }

	/// <summary>Gets or sets <c>EditionFlagOrdered</c>. The default is false.</summary>
	public bool IsOrdered { get; set; }

	/// <summary>Gets or sets <c>EditionFlagHidden</c>. The default is false.</summary>
	public bool IsHidden { get; set; }

	/// <summary>Gets the top-level chapter atoms in file order.</summary>
	public IList<MatroskaChapter> Chapters => _chapters;

	internal bool HasAtoms {
		get {
			foreach (var chapter in _chapters) {
				if (chapter is not null)
					return true;
			}

			return MatroskaChapters.KeptHas (_kept, MatroskaIds.ChapterAtom);
		}
	}

	/// <summary>Adds a chapter with a new random <see cref="MatroskaChapter.Uid"/>.</summary>
	/// <param name="start">The chapter start.</param>
	/// <param name="end">The chapter end, which is excluded, or null to leave it out.</param>
	/// <param name="title">The display string, or null for no display.</param>
	/// <param name="language">The Matroska language of <paramref name="title"/>.</param>
	/// <returns>The new chapter.</returns>
	public MatroskaChapter AddChapter (TimeSpan start, TimeSpan? end = null, string? title = null, string language = "eng")
	{
		var chapter = MatroskaChapter.Create (start, end, title, language);
		_chapters.Add (chapter);
		return chapter;
	}

	/// <summary>Removes a top-level chapter and the chapters nested in it.</summary>
	/// <returns>True when the chapter was found.</returns>
	public bool RemoveChapter (MatroskaChapter chapter) => _chapters.Remove (chapter);

	internal void Write (ChapterWriter writer, bool isWebM)
	{
		// An unchanged edition in a Matroska file goes back byte for byte. WebM keeps to its subset instead.
		if (!isWebM && _unchanged) {
			writer.Add (_source.AsSpan (_sourceAt, _sourceLength));
			return;
		}

		writer.Begin (MatroskaIds.EditionEntry);
		if (!isWebM) {
			if (Uid != 0)
				writer.Unsigned (MatroskaIds.EditionUid, Uid);
			if (IsHidden)
				writer.Unsigned (MatroskaIds.EditionFlagHidden, 1);
			if (IsDefault)
				writer.Unsigned (MatroskaIds.EditionFlagDefault, 1);
			if (IsOrdered)
				writer.Unsigned (MatroskaIds.EditionFlagOrdered, 1);
		}

		MatroskaChapters.AddKept (writer, _kept, isWebM);
		MatroskaChapters.WriteChapters (writer, _chapters, _kept, isWebM, 1);
		writer.End ();
	}

	internal static MatroskaEdition Read (byte[] source, EbmlElement entry, ref int budget)
	{
		// EditionUID is optional, so an edition read without one keeps 0.
		var edition = new MatroskaEdition (0) {
			_fromFile = true,
			_source = source,
			_sourceAt = entry.Position,
			_sourceLength = entry.End - entry.Position
		};
		var body = MatroskaChapters.Payload (source, entry);
		var bodyAt = entry.DataOffset;
		var offset = 0;
		while (MatroskaChapters.TryNext (body, ref offset, ref budget, edition._kept, out var element)) {
			var payload = MatroskaChapters.Payload (body, element);
			switch (element.Id) {
				case MatroskaIds.EditionUid:
					edition.Uid = EbmlReader.ReadUInt (payload);
					break;
				case MatroskaIds.EditionFlagHidden:
					edition.IsHidden = MatroskaChapters.ReadFlag (payload, false);
					break;
				case MatroskaIds.EditionFlagDefault:
					edition.IsDefault = MatroskaChapters.ReadFlag (payload, false);
					break;
				case MatroskaIds.EditionFlagOrdered:
					edition.IsOrdered = MatroskaChapters.ReadFlag (payload, false);
					break;
				case MatroskaIds.ChapterAtom:
					edition._chapters.Add (MatroskaChapter.Read (source, bodyAt, element, 1, ref budget));
					break;
				default:
					MatroskaChapters.KeepUnknown (edition._kept, body, element);
					break;
			}
		}

		edition._readState = edition.OwnState;
		edition._readChapters = edition._chapters.ToArray ();
		return edition;
	}
}

/// <summary>One <c>ChapterAtom</c>. Times are Matroska ticks (nanoseconds) and are not scaled by <c>TimestampScale</c>.</summary>
public sealed class MatroskaChapter : IReadFromFile
{
	readonly List<MatroskaChapterDisplay> _displays = new ();
	readonly List<MatroskaChapter> _chapters = new ();
	readonly List<KeptElement> _kept = new ();
	ulong _uid;

	MatroskaChapter (ulong uid) => _uid = uid;

	bool IReadFromFile.IsFromFile => _fromFile;

	bool _fromFile;
	byte[]? _source;
	int _sourceAt;
	int _sourceLength;
	ulong _readUid;
	string? _readStringUid;
	ulong _readStart;
	ulong? _readEnd;
	bool _readHidden;
	bool _readEnabled;
	string?[] _readDisplays = [];
	MatroskaChapter?[] _readChapters = [];
	bool _unchanged;

	void MarkRead ()
	{
		_readUid = _uid;
		_readStringUid = StringUid;
		_readStart = StartNanoseconds;
		_readEnd = EndNanoseconds;
		_readHidden = IsHidden;
		_readEnabled = IsEnabled;
		_readDisplays = _displays.Count == 0 ? [] : _displays.ConvertAll (display => display?.State).ToArray ();
		_readChapters = _chapters.Count == 0 ? [] : _chapters.ToArray ();
	}

	bool DisplaysAsRead {
		get {
			if (_displays.Count != _readDisplays.Length)
				return false;
			for (var i = 0; i < _readDisplays.Length; i++) {
				if (_displays[i]?.State != _readDisplays[i])
					return false;
			}

			return true;
		}
	}

	/// <summary>Gets a value indicating whether the chapter is new, or its own values or nested list changed.</summary>
	internal bool OwnChanged =>
		!_fromFile
		|| _uid != _readUid
		|| StringUid != _readStringUid
		|| StartNanoseconds != _readStart
		|| EndNanoseconds != _readEnd
		|| IsHidden != _readHidden
		|| IsEnabled != _readEnabled
		|| !DisplaysAsRead
		|| !MatroskaChapters.SameItems (_chapters, _readChapters);

	/// <summary>Works out, for this save, which parts are unchanged. Returns true when the whole chapter is.</summary>
	internal bool Prepare (int depth)
	{
		// Bounded like Write, which also stops a chapter that was added inside itself.
		if (depth > MatroskaChapters.MaxDepth) {
			_unchanged = false;
			return false;
		}

		var unchanged = !OwnChanged;
		foreach (var chapter in _chapters) {
			if (chapter is not null && !chapter.Prepare (depth + 1))
				unchanged = false;
		}

		_unchanged = unchanged && _source is not null;
		return _unchanged;
	}

	/// <summary>Creates a chapter with a new random <see cref="Uid"/> that starts at 0 and has no display.</summary>
	public MatroskaChapter () : this (MatroskaChapters.NewUid ())
	{
	}

	/// <summary>Gets or sets the <c>ChapterUID</c>. RFC 9559 does not allow 0.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is 0.</exception>
	public ulong Uid {
		get => _uid;
		set {
			if (value == 0)
				throw new ArgumentOutOfRangeException (nameof (value), "A ChapterUID cannot be 0.");
			_uid = value;
		}
	}

	/// <summary>Gets or sets the <c>ChapterStringUID</c>, such as a WebVTT cue identifier, or null to leave it out.</summary>
	public string? StringUid { get; set; }

	/// <summary>Gets or sets <c>ChapterTimeStart</c> in nanoseconds.</summary>
	public ulong StartNanoseconds { get; set; }

	/// <summary>Gets or sets <c>ChapterTimeEnd</c> in nanoseconds, which is excluded, or null to leave it out.</summary>
	public ulong? EndNanoseconds { get; set; }

	/// <summary>Gets or sets the start as a <see cref="TimeSpan"/>. Precision below 100 ns is dropped on read.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or past 2^64 - 1 nanoseconds.</exception>
	public TimeSpan Start {
		get => FromNanoseconds (StartNanoseconds);
		set => StartNanoseconds = ToNanoseconds (value);
	}

	/// <summary>Gets or sets the end as a <see cref="TimeSpan"/>, or null to leave it out. A save fails when it is before the start.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or past 2^64 - 1 nanoseconds.</exception>
	public TimeSpan? End {
		get => EndNanoseconds is ulong end ? FromNanoseconds (end) : null;
		set => EndNanoseconds = value is TimeSpan end ? ToNanoseconds (end) : null;
	}

	/// <summary>Gets or sets <c>ChapterFlagHidden</c>. The default is false.</summary>
	public bool IsHidden { get; set; }

	/// <summary>Gets or sets <c>ChapterFlagEnabled</c>. The default is true.</summary>
	public bool IsEnabled { get; set; } = true;

	/// <summary>Gets the <c>ChapterDisplay</c> strings in file order.</summary>
	public IList<MatroskaChapterDisplay> Displays => _displays;

	/// <summary>
	/// Gets the chapters nested in this one. A save writes 32 levels below an edition. Deeper levels
	/// read from a file are kept as bytes. Deeper levels added in code are not written.
	/// </summary>
	public IList<MatroskaChapter> Chapters => _chapters;

	/// <summary>
	/// Gets or sets the first display string. Setting it replaces that string, or adds an English display.
	/// Null removes every display.
	/// </summary>
	public string? Title {
		get {
			foreach (var display in _displays) {
				if (display is not null)
					return display.Text;
			}

			return null;
		}
		set {
			if (value is null) {
				_displays.Clear ();
				return;
			}

			foreach (var display in _displays) {
				if (display is not null) {
					display.Text = value;
					return;
				}
			}

			_displays.Add (new MatroskaChapterDisplay (value));
		}
	}

	/// <summary>Adds a chapter nested in this one with a new random <see cref="Uid"/>.</summary>
	/// <param name="start">The chapter start.</param>
	/// <param name="end">The chapter end, which is excluded, or null to leave it out.</param>
	/// <param name="title">The display string, or null for no display.</param>
	/// <param name="language">The Matroska language of <paramref name="title"/>.</param>
	/// <returns>The new chapter.</returns>
	public MatroskaChapter AddChapter (TimeSpan start, TimeSpan? end = null, string? title = null, string language = "eng")
	{
		var chapter = Create (start, end, title, language);
		_chapters.Add (chapter);
		return chapter;
	}

	/// <summary>Removes a nested chapter.</summary>
	/// <returns>True when the chapter was found.</returns>
	public bool RemoveChapter (MatroskaChapter chapter) => _chapters.Remove (chapter);

	internal static MatroskaChapter Create (TimeSpan start, TimeSpan? end, string? title, string language)
	{
		var chapter = new MatroskaChapter { Start = start, End = end };
		if (title is not null)
			chapter._displays.Add (new MatroskaChapterDisplay (title, language));
		return chapter;
	}

	internal void Write (ChapterWriter writer, bool isWebM, int depth)
	{
		// An unchanged chapter in a Matroska file goes back byte for byte. WebM keeps to its subset instead.
		if (!isWebM && _unchanged) {
			writer.Add (_source.AsSpan (_sourceAt, _sourceLength));
			return;
		}

		writer.Begin (MatroskaIds.ChapterAtom);
		writer.Unsigned (MatroskaIds.ChapterUid, _uid);
		if (StringUid is not null)
			writer.String (MatroskaIds.ChapterStringUid, StringUid);
		writer.Unsigned (MatroskaIds.ChapterTimeStart, StartNanoseconds);
		if (EndNanoseconds is ulong end)
			writer.Unsigned (MatroskaIds.ChapterTimeEnd, end);
		if (!isWebM) {
			// Both flags have defaults, so RFC 8794 section 11.1.6.4 lets a writer leave them out.
			if (IsHidden)
				writer.Unsigned (MatroskaIds.ChapterFlagHidden, 1);
			if (!IsEnabled)
				writer.Unsigned (MatroskaIds.ChapterFlagEnabled, 0);
		}

		MatroskaChapters.AddKept (writer, _kept, isWebM);
		foreach (var display in _displays)
			display?.Write (writer, isWebM);

		// Writing is recursive. A model built in code is bounded the same way a parsed one is,
		// which also stops a chapter that was added inside itself.
		MatroskaChapters.WriteChapters (writer, depth < MatroskaChapters.MaxDepth ? _chapters : [], _kept, isWebM, depth + 1);
		writer.End ();
	}

	internal static MatroskaChapter Read (byte[] source, int parentAt, EbmlElement atom, int depth, ref int budget)
	{
		ulong uid = 0;
		var chapter = new MatroskaChapter (0) {
			_fromFile = true,
			_source = source,
			_sourceAt = parentAt + atom.Position,
			_sourceLength = atom.End - atom.Position
		};
		var bodyAt = parentAt + atom.DataOffset;
		var body = source.AsSpan (bodyAt, (int)atom.DataLength);
		var offset = 0;
		while (MatroskaChapters.TryNext (body, ref offset, ref budget, chapter._kept, out var element)) {
			var payload = MatroskaChapters.Payload (body, element);
			switch (element.Id) {
				case MatroskaIds.ChapterUid:
					uid = EbmlReader.ReadUInt (payload);
					break;
				case MatroskaIds.ChapterStringUid:
					chapter.StringUid = EbmlReader.ReadString (payload);
					break;
				case MatroskaIds.ChapterTimeStart:
					chapter.StartNanoseconds = EbmlReader.ReadUInt (payload);
					break;
				case MatroskaIds.ChapterTimeEnd:
					chapter.EndNanoseconds = EbmlReader.ReadUInt (payload);
					break;
				case MatroskaIds.ChapterFlagHidden:
					chapter.IsHidden = MatroskaChapters.ReadFlag (payload, false);
					break;
				case MatroskaIds.ChapterFlagEnabled:
					chapter.IsEnabled = MatroskaChapters.ReadFlag (payload, true);
					break;
				case MatroskaIds.ChapterDisplay:
					chapter._displays.Add (MatroskaChapterDisplay.Read (payload, ref budget));
					break;
				case MatroskaIds.ChapterAtom when depth < MatroskaChapters.MaxDepth:
					chapter._chapters.Add (Read (source, bodyAt, element, depth + 1, ref budget));
					break;
				default:
					// Too deep or not modelled: the bytes go back as they were.
					MatroskaChapters.KeepUnknown (chapter._kept, body, element);
					break;
			}
		}

		// ChapterUID is mandatory and nonzero. A missing or 0 UID gets a new one, written only when the
		// chapter itself is rewritten; until then the chapter keeps its bytes.
		chapter._uid = uid != 0 ? uid : MatroskaChapters.NewUid ();
		chapter.MarkRead ();
		return chapter;
	}

	static TimeSpan FromNanoseconds (ulong nanoseconds) => TimeSpan.FromTicks ((long)(nanoseconds / 100));

	static ulong ToNanoseconds (TimeSpan value)
	{
		// ChapterTimeStart and ChapterTimeEnd are unsigned 64-bit nanoseconds (RFC 9559 section 5.1.7.1.4.3).
		if (value < TimeSpan.Zero || (ulong)value.Ticks > ulong.MaxValue / 100)
			throw new ArgumentOutOfRangeException (nameof (value), "A chapter time must be from 0 to 2^64 - 1 nanoseconds.");
		return (ulong)value.Ticks * 100;
	}

	/// <param name="ordered">Whether the edition is ordered.</param>
	/// <param name="all">Whether to check this chapter even if it did not change (its edition is new or changed).</param>
	/// <param name="parent">The parent chapter, or null at the top.</param>
	/// <param name="parentChanged">Whether the parent's own values changed, which puts the nesting rule back in play.</param>
	/// <param name="path">The chapter's position, such as "edition 1, chapter 2.1".</param>
	/// <param name="depth">The nesting depth.</param>
	/// <param name="error">The rule that failed, naming the chapter.</param>
	internal bool TryValidate (bool ordered, bool all, MatroskaChapter? parent, bool parentChanged, string path, int depth, out string error)
	{
		error = "";
		var changed = all || OwnChanged;
		var name = "Matroska chapter (" + path + ", UID " + _uid.ToString (System.Globalization.CultureInfo.InvariantCulture) + ")";
		if (changed && EndNanoseconds is ulong end && end < StartNanoseconds) {
			error = name + " ends before it starts (RFC 9559 section 20.2.2)";
			return false;
		}

		if ((changed || parentChanged) && parent is not null && (StartNanoseconds < parent.StartNanoseconds
			|| (parent.EndNanoseconds is ulong parentEnd && StartNanoseconds > parentEnd))) {
			error = name + " starts outside its parent (RFC 9559 section 20.2.3)";
			return false;
		}

		// Nested atoms kept as bytes past the depth limit still make this a parent.
		var nested = MatroskaChapters.KeptHas (_kept, MatroskaIds.ChapterAtom);
		if (depth < MatroskaChapters.MaxDepth) {
			for (var i = 0; i < _chapters.Count; i++) {
				if (_chapters[i] is not { } chapter)
					continue;
				nested = true;
				var childPath = path + "." + (i + 1).ToString (System.Globalization.CultureInfo.InvariantCulture);
				if (!chapter.TryValidate (ordered, all, this, changed, childPath, depth + 1, out error))
					return false;
			}
		}

		if (changed && ordered && !nested && EndNanoseconds is null) {
			error = name + " is in an ordered edition and needs an end (RFC 9559 section 20.2.4)";
			return false;
		}

		return true;
	}
}

/// <summary>One <c>ChapterDisplay</c>: a chapter string and its languages.</summary>
public sealed class MatroskaChapterDisplay
{
	readonly List<string> _languages = new ();
	readonly List<string> _languagesBcp47 = new ();
	readonly List<string> _countries = new ();
	readonly List<KeptElement> _kept = new ();

	/// <summary>Creates a display.</summary>
	/// <param name="text">The <c>ChapString</c>.</param>
	/// <param name="language">A Matroska language, or null to add none (which reads as <c>eng</c>).</param>
	public MatroskaChapterDisplay (string text, string? language = "eng")
	{
		Text = text ?? string.Empty;
		if (language is not null)
			_languages.Add (language);
	}

	/// <summary>Gets or sets the <c>ChapString</c>.</summary>
	public string Text { get; set; }

	/// <summary>
	/// Gets the <c>ChapLanguage</c> values. A display without one reads as <c>eng</c> (RFC 9559 section 5.1.7.1.4.11).
	/// They are ignored when <see cref="LanguagesBcp47"/> has a value.
	/// </summary>
	public IList<string> Languages => _languages;

	/// <summary>Gets the <c>ChapLanguageBCP47</c> values (RFC 5646). WebM does not store them.</summary>
	public IList<string> LanguagesBcp47 => _languagesBcp47;

	/// <summary>Gets the <c>ChapCountry</c> values.</summary>
	public IList<string> Countries => _countries;

	/// <summary>Gets the display's values as one string, to tell an edit from a no-op.</summary>
	internal string State =>
		Text + "\u0002" + string.Join ("\u0003", _languages) + "\u0002" + string.Join ("\u0003", _languagesBcp47)
		+ "\u0002" + string.Join ("\u0003", _countries);

	internal void Write (ChapterWriter writer, bool isWebM)
	{
		writer.Begin (MatroskaIds.ChapterDisplay);
		writer.String (MatroskaIds.ChapString, Text ?? string.Empty);
		foreach (var language in _languages) {
			if (language is not null)
				writer.String (MatroskaIds.ChapLanguage, language);
		}

		if (!isWebM) {
			foreach (var language in _languagesBcp47) {
				if (language is not null)
					writer.String (MatroskaIds.ChapLanguageBcp47, language);
			}
		}

		foreach (var country in _countries) {
			if (country is not null)
				writer.String (MatroskaIds.ChapCountry, country);
		}

		MatroskaChapters.AddKept (writer, _kept, isWebM);
		MatroskaChapters.AddKept (writer, _kept, isWebM, rest: true);
		writer.End ();
	}

	internal static MatroskaChapterDisplay Read (ReadOnlySpan<byte> body, ref int budget)
	{
		var display = new MatroskaChapterDisplay (string.Empty, null);
		var offset = 0;
		while (MatroskaChapters.TryNext (body, ref offset, ref budget, display._kept, out var element)) {
			var payload = MatroskaChapters.Payload (body, element);
			switch (element.Id) {
				case MatroskaIds.ChapString:
					display.Text = EbmlReader.ReadString (payload);
					break;
				case MatroskaIds.ChapLanguage:
					display._languages.Add (EbmlReader.ReadString (payload));
					break;
				case MatroskaIds.ChapLanguageBcp47:
					display._languagesBcp47.Add (EbmlReader.ReadString (payload));
					break;
				case MatroskaIds.ChapCountry:
					display._countries.Add (EbmlReader.ReadString (payload));
					break;
				default:
					MatroskaChapters.KeepUnknown (display._kept, body, element);
					break;
			}
		}

		// ChapLanguage is mandatory with the default "eng".
		if (display._languages.Count == 0)
			display._languages.Add ("eng");
		return display;
	}
}

/// <summary>
/// Writes chapter elements in two passes over the same model: one that only measures each master's
/// payload, then one that writes into an exactly sized buffer. No level is copied into its
/// parent. Sizes use the same minimal widths as <see cref="EbmlWriter"/>.
/// </summary>
internal sealed class ChapterWriter
{
	readonly List<long> _sizes;
	readonly Stack<(int Index, long Start)> _open = new ();
	readonly byte[] _scratch = new byte[16];
	readonly bool _measuring;
	int _next;

	ChapterWriter (List<long> sizes, bool measuring, byte[]? buffer)
	{
		_sizes = sizes;
		_measuring = measuring;
		Buffer = buffer;
	}

	/// <summary>Gets the written bytes after a buffer pass.</summary>
	public byte[]? Buffer { get; }

	/// <summary>Gets the bytes written or measured so far.</summary>
	public long Position { get; private set; }

	public static ChapterWriter Measure () => new (new List<long> (), true, null);

	public static ChapterWriter ToBuffer (ChapterWriter measured) =>
		new (measured._sizes, false, new byte[measured.Position]);

	public void Begin (ulong id)
	{
		WriteId (id);
		if (_measuring) {
			_sizes.Add (0);
			_open.Push ((_sizes.Count - 1, Position));
			return;
		}

		WriteSize (_sizes[_next++]);
	}

	public void End ()
	{
		if (!_measuring)
			return;
		var (index, start) = _open.Pop ();
		var payload = Position - start;
		_sizes[index] = payload;
		Position += SizeWidth (payload);
	}

	public void Unsigned (ulong id, ulong value)
	{
		var width = 1;
		for (var probe = value >> 8; probe != 0; probe >>= 8)
			width++;
		WriteId (id);
		WriteSize (width);
		for (var i = width - 1; i >= 0; i--)
			_scratch[width - 1 - i] = (byte)(value >> (8 * i));
		Add (_scratch.AsSpan (0, width));
	}

	public void String (ulong id, string value)
	{
		var bytes = System.Text.Encoding.UTF8.GetBytes (value);
		WriteId (id);
		WriteSize (bytes.Length);
		Add (bytes);
	}

	public void Add (ReadOnlySpan<byte> bytes)
	{
		if (!_measuring && Buffer is not null)
			bytes.CopyTo (Buffer.AsSpan ((int)Position));
		Position += bytes.Length;
	}

	void WriteId (ulong id)
	{
		var width = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
		for (var i = width - 1; i >= 0; i--)
			_scratch[width - 1 - i] = (byte)(id >> (8 * i));
		Add (_scratch.AsSpan (0, width));
	}

	void WriteSize (long size)
	{
		// All ones is reserved for an unknown size (RFC 8794 section 6.2), so each width stops one short.
		var width = SizeWidth (size);
		var raw = (1UL << (7 * width)) | (ulong)size;
		for (var i = width - 1; i >= 0; i--)
			_scratch[width - 1 - i] = (byte)(raw >> (8 * i));
		Add (_scratch.AsSpan (0, width));
	}

	static int SizeWidth (long size) =>
		size < 0x7F ? 1 : size < 0x3FFF ? 2 : size < 0x1FFFFF ? 3 : size < 0x0FFFFFFF ? 4 : 8;
}

/// <summary>An edition or chapter that knows whether it was read from the file or added in code.</summary>
internal interface IReadFromFile
{
	bool IsFromFile { get; }
}

/// <summary>One or more consecutive child elements kept as their original bytes.</summary>
internal readonly struct KeptElement
{
	public KeptElement (byte[] bytes, bool isRest = false)
	{
		Bytes = bytes;
		IsRest = isRest;
		var atom = false;
		var edition = false;
		var offset = 0;
		while (offset < bytes.Length && !(atom && edition) && EbmlReader.TryRead (bytes, offset, bytes.Length, out var child) && !child.UnknownSize) {
			atom |= child.Id == MatroskaIds.ChapterAtom;
			edition |= child.Id == MatroskaIds.EditionEntry;
			offset = child.End;
		}

		HasAtom = atom;
		HasEdition = edition;
	}

	public byte[] Bytes { get; }

	/// <summary>Gets a value indicating whether a <c>ChapterAtom</c> is among the kept top-level elements.</summary>
	public bool HasAtom { get; }

	/// <summary>Gets a value indicating whether an <c>EditionEntry</c> is among the kept top-level elements.</summary>
	public bool HasEdition { get; }

	/// <summary>Gets a value indicating whether this holds the rest of a parent past the entry budget, which comes last.</summary>
	public bool IsRest { get; }
}
