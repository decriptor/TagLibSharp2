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
/// A WebM save leaves out <c>EditionUID</c>, the edition flags, <c>ChapterFlagHidden</c>,
/// <c>ChapterFlagEnabled</c>, and <c>ChapLanguageBCP47</c>.
/// </para>
/// <para>
/// A chapter title names a chapter, not the file, so it never becomes <see cref="MatroskaTag.Title"/>.
/// A save writes <c>Chapters</c> again only when the editions differ from what was read.
/// Otherwise the original element bytes stay where they are.
/// Child elements this type does not model are kept and written back.
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
	byte[] _baseline = [];
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
	/// Gets a value indicating whether a save has to write <c>Chapters</c>: the editions render differently
	/// from the ones read, or <see cref="Clear"/> emptied a file that had the element.
	/// </summary>
	internal bool IsModified {
		get {
			var rendered = Render ();
			return !rendered.Span.SequenceEqual (_baseline) || (_cleared && _hadElement && rendered.Length == 0);
		}
	}

	/// <summary>
	/// Checks the RFC 9559 rules on chapter times: an end is not before its start (section 20.2.2),
	/// a nested chapter starts inside its parent (section 20.2.3), and every lowest-level chapter of an
	/// ordered edition has an end (section 20.2.4 and Table 29).
	/// </summary>
	internal bool TryValidate (out string error)
	{
		foreach (var edition in _editions) {
			if (edition is null)
				continue;
			foreach (var chapter in edition.Chapters) {
				if (chapter is not null && !chapter.TryValidate (edition.IsOrdered, null, 1, out error))
					return false;
			}
		}

		error = "";
		return true;
	}

	/// <summary>Writes one <c>Chapters</c> element.</summary>
	/// <returns>The element, or empty when no edition has a chapter.</returns>
	internal BinaryData Render ()
	{
		using var body = new BinaryDataBuilder ();
		foreach (var edition in _editions) {
			if (edition is not null && edition.HasAtoms)
				edition.RenderTo (body, IsWebM);
		}

		// RFC 9559 section 20.1: Chapters needs an EditionEntry. Editions past the entry limit, and
		// children this type does not model, go back as they were.
		if (body.Length == 0 && !KeptHas (_kept, MatroskaIds.EditionEntry))
			return BinaryData.Empty;
		AddKept (body, _kept, IsWebM);
		AddKept (body, _kept, IsWebM, rest: true);
		using var element = new BinaryDataBuilder (body.Length + 12);
		ChapterWriter.Master (element, MatroskaIds.Chapters, body.Span);
		return element.ToBinaryData ();
	}

	/// <summary>Starts an empty set whose baseline is no <c>Chapters</c> element.</summary>
	internal static MatroskaChapters Empty (bool isWebM) => new (isWebM);

	/// <summary>Parses a <c>Chapters</c> payload. Malformed children end their parent and do not throw.</summary>
	internal static MatroskaChapters Read (ReadOnlySpan<byte> body, bool isWebM)
	{
		var chapters = new MatroskaChapters (isWebM) { _hadElement = true };
		var budget = MaxEntries;
		var offset = 0;
		while (TryNext (body, ref offset, ref budget, chapters._kept, out var element)) {
			if (element.Id == MatroskaIds.EditionEntry)
				chapters._editions.Add (MatroskaEdition.Read (Payload (body, element), ref budget));
			else
				KeepUnknown (chapters._kept, body, element);
		}

		chapters._baseline = chapters.Render ().ToArray ();
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
		using var rest = new BinaryDataBuilder ();
		while (offset < body.Length && EbmlReader.TryRead (body, offset, body.Length, out var element) && !element.UnknownSize) {
			if (element.Id is not MatroskaIds.Void and not MatroskaIds.Crc32)
				rest.Add (body.Slice (element.Position, element.End - element.Position));
			offset = element.End;
		}

		if (rest.Length > 0)
			kept.Add (new KeptElement (rest.ToArray (), isRest: true));
	}

	internal static void KeepUnknown (List<KeptElement> kept, ReadOnlySpan<byte> body, EbmlElement element) =>
		kept.Add (new KeptElement (body.Slice (element.Position, element.End - element.Position).ToArray ()));

	/// <summary>Returns true when a kept copy holds an element with <paramref name="id"/> at its top level.</summary>
	internal static bool KeptHas (List<KeptElement> kept, ulong id)
	{
		foreach (var element in kept) {
			var bytes = element.Bytes;
			var offset = 0;
			while (offset < bytes.Length && EbmlReader.TryRead (bytes, offset, bytes.Length, out var child) && !child.UnknownSize) {
				if (child.Id == id)
					return true;
				offset = child.End;
			}
		}

		return false;
	}

	/// <summary>
	/// Writes the kept copies: the unmodelled children, or with <paramref name="rest"/> the bytes past the
	/// entry budget, which follow the modelled children as they did in the file. A WebM save leaves out
	/// every top-level element WebM does not support.
	/// </summary>
	internal static void AddKept (BinaryDataBuilder builder, List<KeptElement> kept, bool isWebM, bool rest = false)
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
public sealed class MatroskaEdition
{
	readonly List<MatroskaChapter> _chapters = new ();
	readonly List<KeptElement> _kept = new ();

	MatroskaEdition (ulong uid) => Uid = uid;

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

	internal void RenderTo (BinaryDataBuilder parent, bool isWebM)
	{
		using var body = new BinaryDataBuilder (64);
		if (!isWebM) {
			if (Uid != 0)
				ChapterWriter.Unsigned (body, MatroskaIds.EditionUid, Uid);
			if (IsHidden)
				ChapterWriter.Unsigned (body, MatroskaIds.EditionFlagHidden, 1);
			if (IsDefault)
				ChapterWriter.Unsigned (body, MatroskaIds.EditionFlagDefault, 1);
			if (IsOrdered)
				ChapterWriter.Unsigned (body, MatroskaIds.EditionFlagOrdered, 1);
		}

		MatroskaChapters.AddKept (body, _kept, isWebM);
		foreach (var chapter in _chapters)
			chapter?.RenderTo (body, isWebM, 1);
		MatroskaChapters.AddKept (body, _kept, isWebM, rest: true);
		ChapterWriter.Master (parent, MatroskaIds.EditionEntry, body.Span);
	}

	internal static MatroskaEdition Read (ReadOnlySpan<byte> body, ref int budget)
	{
		// EditionUID is optional, so an edition read without one keeps 0.
		var edition = new MatroskaEdition (0);
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
					edition._chapters.Add (MatroskaChapter.Read (payload, 1, ref budget));
					break;
				default:
					MatroskaChapters.KeepUnknown (edition._kept, body, element);
					break;
			}
		}

		return edition;
	}
}

/// <summary>One <c>ChapterAtom</c>. Times are Matroska ticks (nanoseconds) and are not scaled by <c>TimestampScale</c>.</summary>
public sealed class MatroskaChapter
{
	readonly List<MatroskaChapterDisplay> _displays = new ();
	readonly List<MatroskaChapter> _chapters = new ();
	readonly List<KeptElement> _kept = new ();
	ulong _uid;

	MatroskaChapter (ulong uid) => _uid = uid;

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

	internal void RenderTo (BinaryDataBuilder parent, bool isWebM, int depth)
	{
		using var body = new BinaryDataBuilder (64);
		ChapterWriter.Unsigned (body, MatroskaIds.ChapterUid, _uid);
		if (StringUid is not null)
			ChapterWriter.String (body, MatroskaIds.ChapterStringUid, StringUid);
		ChapterWriter.Unsigned (body, MatroskaIds.ChapterTimeStart, StartNanoseconds);
		if (EndNanoseconds is ulong end)
			ChapterWriter.Unsigned (body, MatroskaIds.ChapterTimeEnd, end);
		if (!isWebM) {
			// Both flags have defaults, so RFC 8794 section 11.1.6.4 lets a writer leave them out.
			if (IsHidden)
				ChapterWriter.Unsigned (body, MatroskaIds.ChapterFlagHidden, 1);
			if (!IsEnabled)
				ChapterWriter.Unsigned (body, MatroskaIds.ChapterFlagEnabled, 0);
		}

		MatroskaChapters.AddKept (body, _kept, isWebM);
		foreach (var display in _displays)
			display?.RenderTo (body, isWebM);

		// Rendering is recursive. A model built in code is bounded the same way a parsed one is,
		// which also stops a chapter that was added inside itself.
		if (depth < MatroskaChapters.MaxDepth) {
			foreach (var chapter in _chapters)
				chapter?.RenderTo (body, isWebM, depth + 1);
		}

		MatroskaChapters.AddKept (body, _kept, isWebM, rest: true);
		ChapterWriter.Master (parent, MatroskaIds.ChapterAtom, body.Span);
	}

	internal static MatroskaChapter Read (ReadOnlySpan<byte> body, int depth, ref int budget)
	{
		ulong uid = 0;
		var chapter = new MatroskaChapter (0);
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
					chapter._chapters.Add (Read (payload, depth + 1, ref budget));
					break;
				default:
					// Too deep or not modelled: the bytes go back as they were.
					MatroskaChapters.KeepUnknown (chapter._kept, body, element);
					break;
			}
		}

		// ChapterUID is mandatory and nonzero. A missing or 0 UID gets a new one, used only when chapters are edited.
		chapter._uid = uid != 0 ? uid : MatroskaChapters.NewUid ();
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

	internal bool TryValidate (bool ordered, MatroskaChapter? parent, int depth, out string error)
	{
		error = "";
		if (EndNanoseconds is ulong end && end < StartNanoseconds) {
			error = "A Matroska chapter ends before it starts (RFC 9559 section 20.2.2)";
			return false;
		}

		if (parent is not null && (StartNanoseconds < parent.StartNanoseconds
			|| (parent.EndNanoseconds is ulong parentEnd && StartNanoseconds > parentEnd))) {
			error = "A nested Matroska chapter starts outside its parent (RFC 9559 section 20.2.3)";
			return false;
		}

		var nested = MatroskaChapters.KeptHas (_kept, MatroskaIds.ChapterAtom);
		if (depth < MatroskaChapters.MaxDepth) {
			foreach (var chapter in _chapters) {
				if (chapter is null)
					continue;
				nested = true;
				if (!chapter.TryValidate (ordered, this, depth + 1, out error))
					return false;
			}
		}

		if (ordered && !nested && EndNanoseconds is null) {
			error = "A chapter in an ordered Matroska edition needs an end (RFC 9559 section 20.2.4)";
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

	internal void RenderTo (BinaryDataBuilder parent, bool isWebM)
	{
		using var body = new BinaryDataBuilder (64);
		ChapterWriter.String (body, MatroskaIds.ChapString, Text ?? string.Empty);
		foreach (var language in _languages) {
			if (language is not null)
				ChapterWriter.String (body, MatroskaIds.ChapLanguage, language);
		}

		if (!isWebM) {
			foreach (var language in _languagesBcp47) {
				if (language is not null)
					ChapterWriter.String (body, MatroskaIds.ChapLanguageBcp47, language);
			}
		}

		foreach (var country in _countries) {
			if (country is not null)
				ChapterWriter.String (body, MatroskaIds.ChapCountry, country);
		}

		MatroskaChapters.AddKept (body, _kept, isWebM);
		MatroskaChapters.AddKept (body, _kept, isWebM, rest: true);
		ChapterWriter.Master (parent, MatroskaIds.ChapterDisplay, body.Span);
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
/// Writes chapter elements straight into the parent's buffer, with the same minimal widths as
/// <see cref="EbmlWriter"/>, so a large model is not copied once per level.
/// </summary>
internal static class ChapterWriter
{
	public static void Unsigned (BinaryDataBuilder builder, ulong id, ulong value)
	{
		var width = 1;
		for (var probe = value >> 8; probe != 0; probe >>= 8)
			width++;
		Header (builder, id, width);
		for (var i = width - 1; i >= 0; i--)
			builder.Add ((byte)(value >> (8 * i)));
	}

	public static void String (BinaryDataBuilder builder, ulong id, string value)
	{
		var bytes = System.Text.Encoding.UTF8.GetBytes (value);
		Header (builder, id, bytes.Length);
		builder.Add (bytes);
	}

	public static void Master (BinaryDataBuilder builder, ulong id, ReadOnlySpan<byte> payload)
	{
		Header (builder, id, payload.Length);
		builder.Add (payload);
	}

	static void Header (BinaryDataBuilder builder, ulong id, int size)
	{
		var idWidth = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
		for (var i = idWidth - 1; i >= 0; i--)
			builder.Add ((byte)(id >> (8 * i)));

		// The same widths as EbmlWriter: all ones is reserved for an unknown size (RFC 8794 section 6.2).
		var value = (ulong)size;
		var width = value < 0x7F ? 1 : value < 0x3FFF ? 2 : value < 0x1FFFFF ? 3 : value < 0x0FFFFFFF ? 4 : 8;
		var raw = (1UL << (7 * width)) | value;
		for (var i = width - 1; i >= 0; i--)
			builder.Add ((byte)(raw >> (8 * i)));
	}
}

/// <summary>One or more consecutive child elements kept as their original bytes.</summary>
internal readonly struct KeptElement
{
	public KeptElement (byte[] bytes, bool isRest = false)
	{
		Bytes = bytes;
		IsRest = isRest;
	}

	public byte[] Bytes { get; }

	/// <summary>Gets a value indicating whether this holds the rest of a parent past the entry budget, which comes last.</summary>
	public bool IsRest { get; }
}
