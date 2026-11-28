// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Xml;
using static TagLibSharp2.Image.MirrorFields;

namespace TagLibSharp2.Image.Xmp;

/// <summary>
/// Finds, in the text of an XMP packet, each changed field's property of a top-level <c>rdf:Description</c>: as an
/// attribute or as a child element (XMP Part 1 §7.4, §7.5), with the exact character ranges to splice.
/// </summary>
static class XmpProperties
{
	internal const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

	// rdf attributes on a property element that say its content is something else; they go with a new value.
	static readonly string[] ContradictingRdfAttributes = ["resource", "parseType", "datatype", "nodeID"];

	public static bool TryFind (string packet, ImageTag tag, string carrier, List<XmpEdit> edits, out string? error)
	{
		error = null;
		var lines = XmlText.LineStarts (packet);
		var settings = new XmlReaderSettings {
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
			IgnoreComments = true,
			IgnoreProcessingInstructions = false,
			CloseInput = true
		};
		try {
			using var reader = XmlReader.Create (new StringReader (packet), settings);
			return TryScan (packet, lines, reader, tag, carrier, edits, out error);
		} catch (XmlException ex) {
			// A packet that is not well-formed is read by no XMP reader (exiftool: "Empty XMP"); it can hold a copy only
			// where its text names a changed field's property, and is refused only then.
			edits.Clear ();
			if (!NamesAnyField (packet, tag))
				return true;
			error = $"The {carrier} is not well-formed XML ({ex.Message}), so its copy of a changed field cannot be updated.";
			return false;
		}
	}

	static bool NamesAnyField (string packet, ImageTag tag)
	{
		foreach (var field in All) {
			if (tag.Changed (field.Store) && packet.Contains (field.XmpName, StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	static bool TryScan (string packet, int[] lines, XmlReader reader, ImageTag tag, string carrier, List<XmpEdit> edits, out string? error)
	{
		error = null;
		var info = (IXmlLineInfo)reader;
		var descriptionDepth = -1;
		while (reader.Read ()) {
			if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == descriptionDepth) {
				descriptionDepth = -1;
				continue;
			}

			if (reader.NodeType != XmlNodeType.Element)
				continue;
			if (descriptionDepth < 0) {
				if (reader.LocalName == "Description" && reader.NamespaceURI == RdfNamespace) {
					if (!TryFindAttributes (packet, lines, reader, info, tag, carrier, edits, out error))
						return false;
					if (!reader.IsEmptyElement)
						descriptionDepth = reader.Depth;
				}

				continue;
			}

			if (reader.Depth != descriptionDepth + 1 || FieldFor (reader.NamespaceURI, reader.LocalName, tag) is not MirrorField field)
				continue;
			if (!TryElementEdit (packet, lines, reader, info, field, out var edit)) {
				error = NotLocated (carrier, field);
				return false;
			}

			edits.Add (edit!);
		}

		return true;
	}

	static string NotLocated (string carrier, MirrorField field) =>
		$"The {carrier} holds {field.Name}, and the property could not be located in the packet text, so the copy there cannot be updated.";

	static bool TryFindAttributes (string packet, int[] lines, XmlReader reader, IXmlLineInfo info, ImageTag tag, string carrier, List<XmpEdit> edits, out string? error)
	{
		error = null;
		if (!reader.MoveToFirstAttribute ())
			return true;
		do {
			var field = FieldFor (reader.NamespaceURI, reader.LocalName, tag);
			if (field is null)
				continue;
			if (!TryAttributeRange (packet, lines, reader, info, out var lead, out var start, out var end)) {
				error = NotLocated (carrier, field);
				return false;
			}

			edits.Add (new XmpEdit (field, reader.Name, Attribute: true) { LeadStart = lead, Start = start, End = end });
		} while (reader.MoveToNextAttribute ());
		reader.MoveToElement ();
		return true;
	}

	// The range of the attribute the reader is on, and where the white space before it starts.
	static bool TryAttributeRange (string packet, int[] lines, XmlReader reader, IXmlLineInfo info, out int lead, out int start, out int end)
	{
		end = -1;
		start = XmlText.Offset (lines, info.LineNumber, info.LinePosition);
		lead = start;
		var name = reader.Name;
		if (start < 0 || !XmlText.At (packet, start, name) || !XmlText.TryAttributeEnd (packet, start + name.Length, out end))
			return false;
		while (lead > 0 && XmlText.IsXmlSpace (packet[lead - 1]))
			lead--;
		return true;
	}

	static bool TryElementEdit (string packet, int[] lines, XmlReader reader, IXmlLineInfo info, MirrorField field, out XmpEdit? edit)
	{
		edit = null;
		var name = reader.Name;
		var nameAt = XmlText.Offset (lines, info.LineNumber, info.LinePosition);
		var start = nameAt - 1;
		if (start < 0 || packet[start] != '<' || !XmlText.At (packet, nameAt, name) || !XmlText.TryTagEnd (packet, nameAt + name.Length, out var startTagEnd))
			return false;
		var rdfPrefix = (reader as IXmlNamespaceResolver)?.LookupPrefix (RdfNamespace);
		var empty = reader.IsEmptyElement;
		if (!TryRdfCuts (packet, lines, reader, info, out var cuts))
			return false;
		var element = new XmpEdit (field, name, Attribute: false) { Start = start, StartTagEnd = startTagEnd, RdfPrefix = rdfPrefix, Cuts = cuts };
		if (!empty)
			return TryCloseElement (packet, lines, reader, info, element, out edit);

		// "<name .../>": the start tag is kept up to the "/".
		var slash = startTagEnd - 2;
		if (packet[slash] != '/')
			return false;
		edit = element with { StartTagEnd = slash, ContentEnd = startTagEnd, End = startTagEnd, Empty = true };
		return true;
	}

	// The ranges of the rdf attributes of the element the reader is on that contradict a new content.
	static bool TryRdfCuts (string packet, int[] lines, XmlReader reader, IXmlLineInfo info, out List<(int Start, int End)> cuts)
	{
		cuts = [];
		if (!reader.MoveToFirstAttribute ())
			return true;
		do {
			if (reader.NamespaceURI != RdfNamespace || Array.IndexOf (ContradictingRdfAttributes, reader.LocalName) < 0)
				continue;
			if (!TryAttributeRange (packet, lines, reader, info, out var lead, out _, out var attributeEnd))
				return false;
			cuts.Add ((lead, attributeEnd));
		} while (reader.MoveToNextAttribute ());
		reader.MoveToElement ();
		return true;
	}

	// Reads to the element's end tag and records where its content and end tag are.
	static bool TryCloseElement (string packet, int[] lines, XmlReader reader, IXmlLineInfo info, XmpEdit element, out XmpEdit? edit)
	{
		edit = null;
		var name = element.Name;
		var depth = reader.Depth;
		while (reader.Read ()) {
			if (reader.NodeType != XmlNodeType.EndElement || reader.Depth != depth)
				continue;
			var closeAt = XmlText.Offset (lines, info.LineNumber, info.LinePosition);
			if (closeAt < 2 || packet[closeAt - 2] != '<' || packet[closeAt - 1] != '/' || !XmlText.At (packet, closeAt, name) || !XmlText.TryTagEnd (packet, closeAt + name.Length, out var end))
				return false;
			edit = element with { ContentEnd = closeAt - 2, End = end };
			return true;
		}

		return false;
	}

	static MirrorField? FieldFor (string ns, string localName, ImageTag tag)
	{
		foreach (var field in All) {
			if (tag.Changed (field.Store) && field.XmpNamespace == ns && field.XmpName == localName)
				return field;
		}

		return null;
	}
}

/// <summary>One property to splice: its character ranges in the packet text.</summary>
sealed record XmpEdit (MirrorField Field, string Name, bool Attribute)
{
	/// <summary>An attribute's preceding white space, removed with it.</summary>
	public int LeadStart { get; init; }

	/// <summary>The '&lt;' of the element, or the attribute name.</summary>
	public int Start { get; init; }

	/// <summary>Past the start tag's '&gt;', or the '/' of an empty element.</summary>
	public int StartTagEnd { get; init; }

	/// <summary>The '&lt;' of the end tag.</summary>
	public int ContentEnd { get; init; }

	public int End { get; init; }
	public bool Empty { get; init; }
	public string? RdfPrefix { get; init; }
	public List<(int Start, int End)> Cuts { get; init; } = [];
}
