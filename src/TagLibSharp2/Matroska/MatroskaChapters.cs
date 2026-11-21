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

	/// <summary>Editions, atoms, and displays past this many are kept as bytes and not parsed.</summary>
	internal const int MaxEntries = 65_536;

	readonly List<MatroskaEdition> _editions = new ();
	readonly List<KeptElement> _kept = new ();
	byte[] _baseline = [];

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

			return _kept.Count == 0;
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

	/// <summary>Removes every edition. The next save removes the <c>Chapters</c> element.</summary>
	public void Clear ()
	{
		_editions.Clear ();
		_kept.Clear ();
	}

	/// <summary>Gets a value indicating whether the editions differ from the ones read from the file.</summary>
	internal bool IsModified => !Render ().Span.SequenceEqual (_baseline);

	/// <summary>Writes one <c>Chapters</c> element.</summary>
	/// <returns>The element, or empty when no edition has a chapter.</returns>
	internal BinaryData Render ()
	{
		using var body = new BinaryDataBuilder ();
		foreach (var edition in _editions) {
			if (edition is not null && edition.HasAtoms)
				body.Add (edition.Render (IsWebM));
		}

		// Editions past the entry limit, and any child this type does not model, go back as they were.
		AddKept (body, _kept);
		return body.Length == 0
			? BinaryData.Empty
			: EbmlWriter.Master (MatroskaIds.Chapters, body.ToBinaryData ().Span);
	}

	/// <summary>Starts an empty set whose baseline is no <c>Chapters</c> element.</summary>
	internal static MatroskaChapters Empty (bool isWebM) => new (isWebM);

	/// <summary>Parses a <c>Chapters</c> payload. Malformed children end their parent and do not throw.</summary>
	internal static MatroskaChapters Read (ReadOnlySpan<byte> body, bool isWebM)
	{
		var chapters = new MatroskaChapters (isWebM);
		var budget = MaxEntries;
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			if (element.Id == MatroskaIds.EditionEntry && budget > 0) {
				budget--;
				chapters._editions.Add (MatroskaEdition.Read (Payload (body, element), ref budget));
			} else {
				// CRC-32 and Void are dropped. A changed element gets neither.
				KeepUnknown (chapters._kept, body, element);
			}

			offset = element.End;
		}

		chapters._baseline = chapters.Render ().ToArray ();
		return chapters;
	}

	/// <summary>A random UID. RFC 9559 gives the UID elements the range "not 0".</summary>
	internal static ulong NewUid ()
	{
		var bytes = new byte[8];
		using var random = RandomNumberGenerator.Create ();
		ulong value;
		do {
			random.GetBytes (bytes);
			value = EbmlReader.ReadUInt (bytes);
		} while (value == 0);

		return value;
	}

	internal static ReadOnlySpan<byte> Payload (ReadOnlySpan<byte> body, EbmlElement element) =>
		body.Slice (element.DataOffset, (int)element.DataLength);

	internal static bool ReadFlag (ReadOnlySpan<byte> payload, bool defaultValue) =>
		payload.Length == 0 ? defaultValue : EbmlReader.ReadUInt (payload) != 0;

	internal static void KeepUnknown (List<KeptElement> kept, ReadOnlySpan<byte> body, EbmlElement element)
	{
		if (element.Id is MatroskaIds.Void or MatroskaIds.Crc32)
			return;
		kept.Add (new KeptElement (element.Id, body.Slice (element.Position, element.End - element.Position).ToArray ()));
	}

	internal static void AddKept (BinaryDataBuilder builder, List<KeptElement> kept)
	{
		foreach (var element in kept)
			builder.Add (element.Bytes);
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

			foreach (var kept in _kept) {
				if (kept.Id == MatroskaIds.ChapterAtom)
					return true;
			}

			return false;
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

	internal BinaryData Render (bool isWebM)
	{
		using var body = new BinaryDataBuilder ();
		if (!isWebM) {
			if (Uid != 0)
				body.Add (EbmlWriter.Unsigned (MatroskaIds.EditionUid, Uid));
			if (IsHidden)
				body.Add (EbmlWriter.Unsigned (MatroskaIds.EditionFlagHidden, 1));
			if (IsDefault)
				body.Add (EbmlWriter.Unsigned (MatroskaIds.EditionFlagDefault, 1));
			if (IsOrdered)
				body.Add (EbmlWriter.Unsigned (MatroskaIds.EditionFlagOrdered, 1));
		}

		MatroskaChapters.AddKept (body, _kept);
		foreach (var chapter in _chapters) {
			if (chapter is not null)
				body.Add (chapter.Render (isWebM, 1));
		}

		return EbmlWriter.Master (MatroskaIds.EditionEntry, body.ToBinaryData ().Span);
	}

	internal static MatroskaEdition Read (ReadOnlySpan<byte> body, ref int budget)
	{
		// EditionUID is optional, so an edition read without one keeps 0.
		var edition = new MatroskaEdition (0);
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
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
				case MatroskaIds.ChapterAtom when budget > 0:
					budget--;
					edition._chapters.Add (MatroskaChapter.Read (payload, 1, ref budget));
					break;
				default:
					MatroskaChapters.KeepUnknown (edition._kept, body, element);
					break;
			}

			offset = element.End;
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
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public TimeSpan Start {
		get => FromNanoseconds (StartNanoseconds);
		set => StartNanoseconds = ToNanoseconds (value);
	}

	/// <summary>Gets or sets the end as a <see cref="TimeSpan"/>, or null to leave it out.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
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

	internal BinaryData Render (bool isWebM, int depth)
	{
		using var body = new BinaryDataBuilder ();
		body.Add (EbmlWriter.Unsigned (MatroskaIds.ChapterUid, _uid));
		if (StringUid is not null)
			body.Add (EbmlWriter.String (MatroskaIds.ChapterStringUid, StringUid));
		body.Add (EbmlWriter.Unsigned (MatroskaIds.ChapterTimeStart, StartNanoseconds));
		if (EndNanoseconds is ulong end)
			body.Add (EbmlWriter.Unsigned (MatroskaIds.ChapterTimeEnd, end));
		if (!isWebM) {
			// Both flags have defaults, so RFC 8794 section 11.1.6.4 lets a writer leave them out.
			if (IsHidden)
				body.Add (EbmlWriter.Unsigned (MatroskaIds.ChapterFlagHidden, 1));
			if (!IsEnabled)
				body.Add (EbmlWriter.Unsigned (MatroskaIds.ChapterFlagEnabled, 0));
		}

		MatroskaChapters.AddKept (body, _kept);
		foreach (var display in _displays) {
			if (display is not null)
				body.Add (display.Render (isWebM));
		}

		// Rendering is recursive. A model built in code is bounded the same way a parsed one is.
		if (depth < MatroskaChapters.MaxDepth) {
			foreach (var chapter in _chapters) {
				if (chapter is not null)
					body.Add (chapter.Render (isWebM, depth + 1));
			}
		}

		return EbmlWriter.Master (MatroskaIds.ChapterAtom, body.ToBinaryData ().Span);
	}

	internal static MatroskaChapter Read (ReadOnlySpan<byte> body, int depth, ref int budget)
	{
		ulong uid = 0;
		var chapter = new MatroskaChapter (0);
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
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
				case MatroskaIds.ChapterDisplay when budget > 0:
					budget--;
					chapter._displays.Add (MatroskaChapterDisplay.Read (payload));
					break;
				case MatroskaIds.ChapterAtom when budget > 0 && depth < MatroskaChapters.MaxDepth:
					budget--;
					chapter._chapters.Add (Read (payload, depth + 1, ref budget));
					break;
				default:
					// Too deep, past the budget, or not modelled: the bytes go back as they were.
					MatroskaChapters.KeepUnknown (chapter._kept, body, element);
					break;
			}

			offset = element.End;
		}

		// ChapterUID is mandatory and nonzero. A missing or 0 UID gets a new one, used only when chapters are edited.
		chapter._uid = uid != 0 ? uid : MatroskaChapters.NewUid ();
		return chapter;
	}

	static TimeSpan FromNanoseconds (ulong nanoseconds) => TimeSpan.FromTicks ((long)(nanoseconds / 100));

	static ulong ToNanoseconds (TimeSpan value)
	{
		if (value < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (value), "A chapter time cannot be negative.");
		return (ulong)value.Ticks * 100;
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

	internal BinaryData Render (bool isWebM)
	{
		using var body = new BinaryDataBuilder ();
		body.Add (EbmlWriter.String (MatroskaIds.ChapString, Text ?? string.Empty));
		foreach (var language in _languages) {
			if (language is not null)
				body.Add (EbmlWriter.String (MatroskaIds.ChapLanguage, language));
		}

		if (!isWebM) {
			foreach (var language in _languagesBcp47) {
				if (language is not null)
					body.Add (EbmlWriter.String (MatroskaIds.ChapLanguageBcp47, language));
			}
		}

		foreach (var country in _countries) {
			if (country is not null)
				body.Add (EbmlWriter.String (MatroskaIds.ChapCountry, country));
		}

		MatroskaChapters.AddKept (body, _kept);
		return EbmlWriter.Master (MatroskaIds.ChapterDisplay, body.ToBinaryData ().Span);
	}

	internal static MatroskaChapterDisplay Read (ReadOnlySpan<byte> body)
	{
		var display = new MatroskaChapterDisplay (string.Empty, null);
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
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

			offset = element.End;
		}

		// ChapLanguage is mandatory with the default "eng".
		if (display._languages.Count == 0)
			display._languages.Add ("eng");
		return display;
	}
}

/// <summary>A child element kept as its original bytes.</summary>
internal readonly struct KeptElement
{
	public KeptElement (ulong id, byte[] bytes)
	{
		Id = id;
		Bytes = bytes;
	}

	public ulong Id { get; }

	public byte[] Bytes { get; }
}
