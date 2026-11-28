// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;

namespace TagLibSharp2.Image.Xmp;

/// <summary>
/// Replaces each changed field's property in an XMP packet, or removes it when the field is null. A property the
/// packet does not hold is not added. The property's own start and end tags (and so any namespace they declare) are
/// kept; only its content changes. Every other character of the packet is kept, in one forward pass.
/// </summary>
static class XmpMirror
{
	/// <summary>Characters the splice has written on this thread, for tests that pin it to one pass.</summary>
	[ThreadStatic]
	internal static long SpliceWrites;

	/// <param name="packet">The packet text.</param>
	/// <param name="tag">The tag whose changed fields are written.</param>
	/// <param name="carrier">Names the block in an error, such as "PNG XMP packet".</param>
	/// <param name="updated">The new packet, or <paramref name="packet"/> itself when nothing changed.</param>
	/// <param name="error">Why the packet cannot be updated.</param>
	/// <returns>False, with an error, when the packet is not well-formed XML or a value cannot be written as XML.</returns>
	internal static bool TryUpdate (string packet, ImageTag tag, string carrier, out string updated, out string? error)
	{
		// A UTF-8 byte order mark before the packet is not part of the XML.
		if (packet.Length > 0 && packet[0] == '﻿')
			return TryUpdateAfterBom (packet, tag, carrier, out updated, out error);

		// XMP Part 1 §7.3: the packet ends with the trailer processing instruction. Some writers follow it with
		// NULs (ImageIO ends JPEG XMP with one) or padding; those bytes are not XML and are copied back as they are.
		var last = packet.LastIndexOf ('>');
		if (last >= 0 && last < packet.Length - 1 && IsPacketTail (packet, last + 1))
			return TryUpdateBeforeTail (packet, last + 1, tag, carrier, out updated, out error);
		return TrySplice (packet, tag, carrier, out updated, out error);
	}

	static bool TryUpdateAfterBom (string packet, ImageTag tag, string carrier, out string updated, out string? error)
	{
		updated = packet;
		if (!TryUpdate (packet.Substring (1), tag, carrier, out var body, out error))
			return false;
		updated = body.Length == packet.Length - 1 && string.CompareOrdinal (body, 0, packet, 1, body.Length) == 0 ? packet : "﻿" + body;
		return true;
	}

	static bool TryUpdateBeforeTail (string packet, int tail, ImageTag tag, string carrier, out string updated, out string? error)
	{
		updated = packet;
		if (!TryUpdate (packet.Substring (0, tail), tag, carrier, out var head, out error))
			return false;
		updated = head.Length == tail && string.CompareOrdinal (head, 0, packet, 0, head.Length) == 0 ? packet : Join (head, packet, tail);
		return true;
	}

	static bool TrySplice (string packet, ImageTag tag, string carrier, out string updated, out string? error)
	{
		updated = packet;
		var edits = new List<XmpEdit> ();
		if (!XmpProperties.TryFind (packet, tag, carrier, edits, out error))
			return false;
		if (edits.Count == 0)
			return true;

		// One forward pass: copy the text between edits, then each edit's replacement. Edits never overlap.
		edits.Sort ((a, b) => a.Start.CompareTo (b.Start));
		var builder = new StringBuilder (packet.Length + 64);
		var written = new HashSet<ImageStores> ();
		var at = 0;
		foreach (var edit in edits) {
			var value = tag.Field (edit.Field.Store);
			var keep = value is not null && written.Add (edit.Field.Store);
			if (keep && !XmlText.IsXmlText (value!)) {
				error = $"The {carrier} holds {edit.Field.Name}, and the new value has a character XML cannot store, so the copy there cannot be updated.";
				return false;
			}

			at = keep ? Replace (builder, packet, at, edit, value!) : Remove (builder, packet, at, edit);
		}

		Append (builder, packet, at, packet.Length);
		updated = builder.ToString ();
		if (!XmlText.IsWellFormed (updated)) {
			error = $"The {carrier} could not be updated: the edited packet is not well-formed XML.";
			return false;
		}

		return true;
	}

	// Removed: an attribute takes the white space before it with it.
	static int Remove (StringBuilder builder, string packet, int at, XmpEdit edit)
	{
		Append (builder, packet, at, edit.Attribute ? edit.LeadStart : edit.Start);
		return edit.End;
	}

	static int Replace (StringBuilder builder, string packet, int at, XmpEdit edit, string value)
	{
		Append (builder, packet, at, edit.Start);
		if (edit.Attribute) {
			Write (builder, $"{edit.Name}=\"{XmlText.EscapeXml (value, attribute: true)}\"");
			return edit.End;
		}

		// The start tag, without rdf attributes that would contradict the new content.
		var tagAt = edit.Start;
		foreach (var (cutStart, cutEnd) in edit.Cuts) {
			Append (builder, packet, tagAt, cutStart);
			tagAt = cutEnd;
		}

		Append (builder, packet, tagAt, edit.StartTagEnd);
		Write (builder, edit.Empty ? ">" : "");
		Write (builder, PropertyContent (edit, value));
		if (edit.Empty)
			Write (builder, $"</{edit.Name}>");
		else
			Append (builder, packet, edit.ContentEnd, edit.End);
		return edit.End;
	}

	static string Join (string head, string packet, int tail) =>
#if NETSTANDARD2_0 || NETSTANDARD2_1
		head + packet.Substring (tail);
#else
		string.Concat (head.AsSpan (), packet.AsSpan (tail));
#endif

	static bool IsPacketTail (string packet, int from)
	{
		for (var i = from; i < packet.Length; i++) {
			if (packet[i] != '\0' && !XmlText.IsXmlSpace (packet[i]))
				return false;
		}

		return true;
	}

	static void Append (StringBuilder builder, string text, int from, int to)
	{
		if (to <= from)
			return;
		builder.Append (text, from, to - from);
		SpliceWrites += to - from;
	}

	static void Write (StringBuilder builder, string text)
	{
		builder.Append (text);
		SpliceWrites += text.Length;
	}

	static string PropertyContent (XmpEdit edit, string value)
	{
		var item = XmlText.EscapeXml (value, attribute: false);
		// The RDF namespace may have no prefix in scope at the property (a declaration on it can hide one);
		// then the container declares it as its default namespace.
		var rdf = edit.RdfPrefix is null ? "" : edit.RdfPrefix.Length == 0 ? "" : edit.RdfPrefix + ":";
		var declare = edit.RdfPrefix is null ? $" xmlns=\"{XmpProperties.RdfNamespace}\"" : "";
		return edit.Field.Ordered
			? $"<{rdf}Seq{declare}><{rdf}li>{item}</{rdf}li></{rdf}Seq>"
			: $"<{rdf}Alt{declare}><{rdf}li xml:lang=\"x-default\">{item}</{rdf}li></{rdf}Alt>";
	}
}
