// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;
using System.Xml;

namespace TagLibSharp2.Image;

/// <summary>
/// The XMP an HDR decoder needs to find and apply a JPEG gain map, which Clear keeps (rule 7: what a decoder
/// needs). Clear writes a new packet from an explicit list of properties with typed values; nothing of the
/// original packet is copied, so no other property, attribute, nested structure, comment or text survives.
/// </summary>
/// <remarks>
/// <para>Ultra HDR v1.0 (https://developer.android.com/media/platform/hdr-image-format, "XMP attributes"):
/// <c>hdrgm:Version</c> ("1.0"), <c>hdrgm:BaseRenditionIsHDR</c> (Boolean), <c>hdrgm:GainMapMin</c>,
/// <c>GainMapMax</c>, <c>Gamma</c>, <c>OffsetSDR</c>, <c>OffsetHDR</c> (Real, or an ordered array of three Reals,
/// one per channel), <c>hdrgm:HDRCapacityMin</c> and <c>HDRCapacityMax</c> (Real); and in the primary image,
/// <c>Container:Directory</c>, an ordered array of <c>Container:Item</c> structures with <c>Item:Semantic</c>,
/// <c>Item:Mime</c>, <c>Item:Length</c> and <c>Item:Padding</c> (lengths and paddings are integers; an item's
/// offset is the primary's length plus the preceding items' lengths and paddings).</para>
/// <para>Apple (the properties ImageIO writes for an HDRGainMap auxiliary image, as in
/// <c>CGImageDestinationAddAuxiliaryDataInfo</c> output): <c>apdi:AuxiliaryImageType</c>
/// (<c>urn:com:apple:photo:2020:aux:hdrgainmap</c>), <c>apdi:NativeFormat</c> and <c>apdi:StoredFormat</c>
/// (integer pixel formats), <c>HDRGainMap:HDRGainMapVersion</c> (integer) and, from version 131072 (iPhone 15),
/// <c>HDRGainMap:HDRGainMapHeadroom</c> (Real), the headroom ImageIO and libultrahdr scale the gain map by. A
/// version-65536 gain map (iPhone 12-14) has no headroom in its XMP: decoders compute it from the primary image's
/// Apple maker note, tags 33 and 48 (Apple, "Applying Apple HDR effect to your photos",
/// https://developer.apple.com/documentation/appkit/applying-apple-hdr-effect-to-your-photos). For such a packet
/// Clear keeps those two values, as ImageIO applies them, and nothing else of the Exif, in a new minimal Exif in
/// the primary (<see cref="Exif.AppleHeadroom.Keep"/>, which also says why it is not a conformant Exif);
/// after Clear ImageIO decodes the HDR rendition exactly as before. Without them ImageIO renders the photo SDR and
/// libultrahdr refuses it as a gain-map image.</para>
/// <para>Motion Photo 1.0 (https://developer.android.com/media/platform/motion-photo-format): <c>Camera:MotionPhoto</c>
/// and <c>Camera:MotionPhotoVersion</c> (Integer) and <c>Camera:MotionPhotoPresentationTimestampUs</c> (Long, -1 for
/// unset), and the directory's Item:Semantic and Item:Mime values, which are enumerated.</para>
/// <para>The packet is read with one forward pass of <see cref="XmlReader"/> that keeps only the path of the
/// current element, so the cost is linear in the packet and nesting does not recurse. Only properties of a
/// top-level <c>rdf:Description</c> count; the first occurrence of a property wins.</para>
/// </remarks>
static class ImageGainMap
{
	const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
	const string Hdrgm = "http://ns.adobe.com/hdr-gain-map/1.0/";
	const string AppleGainMap = "http://ns.apple.com/HDRGainMap/1.0/";
	const string Apdi = "http://ns.apple.com/pixeldatainfo/1.0/";
	const string Container = "http://ns.google.com/photos/1.0/container/";
	const string Item = "http://ns.google.com/photos/1.0/container/item/";
	const string Camera = "http://ns.google.com/photos/1.0/camera/";
	const string AppleGainMapType = "urn:com:apple:photo:2020:aux:hdrgainmap";

	enum Shape { Version, Boolean, Real, RealOrThree, Integer, Long, AppleType }

	// The order is the order they are written in.
	static readonly (string Ns, string Name, string Prefix, Shape Shape)[] Properties = [
		(Hdrgm, "Version", "hdrgm", Shape.Version),
		(Hdrgm, "BaseRenditionIsHDR", "hdrgm", Shape.Boolean),
		(Hdrgm, "GainMapMin", "hdrgm", Shape.RealOrThree),
		(Hdrgm, "GainMapMax", "hdrgm", Shape.RealOrThree),
		(Hdrgm, "Gamma", "hdrgm", Shape.RealOrThree),
		(Hdrgm, "OffsetSDR", "hdrgm", Shape.RealOrThree),
		(Hdrgm, "OffsetHDR", "hdrgm", Shape.RealOrThree),
		(Hdrgm, "HDRCapacityMin", "hdrgm", Shape.Real),
		(Hdrgm, "HDRCapacityMax", "hdrgm", Shape.Real),
		(Apdi, "AuxiliaryImageType", "apdi", Shape.AppleType),
		(Apdi, "NativeFormat", "apdi", Shape.Integer),
		(Apdi, "StoredFormat", "apdi", Shape.Integer),
		(AppleGainMap, "HDRGainMapVersion", "HDRGainMap", Shape.Integer),
		(AppleGainMap, "HDRGainMapHeadroom", "HDRGainMap", Shape.Real),
		(Camera, "MotionPhoto", "Camera", Shape.Integer),
		(Camera, "MotionPhotoVersion", "Camera", Shape.Integer),
		(Camera, "MotionPhotoPresentationTimestampUs", "Camera", Shape.Long),
	];

	// Ultra HDR v1.0 and Motion Photo 1.0 (https://developer.android.com/media/platform/motion-photo-format)
	// enumerate these; any other value is dropped (the item keeps its slot, its length and its padding).
	static readonly string[] Semantics = ["Primary", "GainMap", "MotionPhoto"];
	static readonly string[] MimeTypes = ["image/jpeg", "image/heic", "image/avif", "video/mp4", "video/quicktime"];

	static readonly string[] ItemFields = ["Semantic", "Mime", "Length", "Padding"];

	// Marks an rdf:li whose Container:Item was read; not one of ItemFields, so it is never written.
	const string ItemSeen = "\0item";

	/// <summary>Steps of the last read on this thread, for tests that bound it by an operation count.</summary>
	[ThreadStatic]
	internal static long Steps;

	/// <summary>
	/// The packet Clear keeps: null when it holds no valid gain-map property, otherwise a new packet that holds
	/// only those properties.
	/// </summary>
	/// <param name="packet">The packet bytes (UTF-8), possibly followed by NULs or padding.</param>
	/// <param name="itemLengths">The new lengths of the images after the first, in file order, or null.</param>
	/// <param name="headroomInMakerNote">True when the kept packet has <c>HDRGainMapVersion</c> and no
	/// <c>HDRGainMapHeadroom</c>: decoders then take the headroom from the primary image's Apple maker note.</param>
	internal static byte[]? KeepForClear (ReadOnlySpan<byte> packet, IReadOnlyList<long>? itemLengths, out bool headroomInMakerNote)
	{
		headroomInMakerNote = false;
		var text = Encoding.UTF8.GetString (packet.ToArray ());
		var end = text.LastIndexOf ('>');
		if (end < 0)
			return null;
		var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
		var found = new Found ();
		try {
			Read (text.Substring (start, end + 1 - start), found);
		} catch (XmlException) {
			return null;
		}

		if (found.Values.Count == 0 && found.Items is null)
			return null;
		if (found.Items is not null && itemLengths is not null)
			SetItemLengths (found.Items, itemLengths);
		headroomInMakerNote = found.Values.ContainsKey (Key ("HDRGainMap", "HDRGainMapVersion")) && !found.Values.ContainsKey (Key ("HDRGainMap", "HDRGainMapHeadroom"));
		return Encoding.UTF8.GetBytes (Write (found));
	}

	sealed class Found
	{
		public Dictionary<string, List<string>> Values { get; } = [];
		public List<Dictionary<string, string>>? Items { get; set; }
	}

	enum Role { Other, Rdf, Description, Property, PropertySeq, PropertyLi, Directory, DirectorySeq, DirectoryLi, DirectoryLiDescription, ItemNode, ItemDescription, ItemField }

	sealed class Frame (Role role, string key)
	{
		public Role Role { get; } = role;
		public string Key { get; } = key;
		public StringBuilder? Text { get; set; }
		public List<string>? Values { get; set; }
		public bool Invalid { get; set; }
	}

	static void Read (string packet, Found found)
	{
		var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true };
		using var reader = XmlReader.Create (new StringReader (packet), settings);
		var state = new ReadState ();
		while (reader.Read ()) {
			Steps++;
			switch (reader.NodeType) {
				case XmlNodeType.Element:
					Open (reader, state, found);
					break;
				case XmlNodeType.Text:
				case XmlNodeType.CDATA:
				case XmlNodeType.SignificantWhitespace:
				case XmlNodeType.Whitespace:
					AppendText (reader, state.Stack);
					break;
				case XmlNodeType.EndElement: {
						var frame = state.Stack[^1];
						state.Stack.RemoveAt (state.Stack.Count - 1);
						Close (frame, state.Stack, found, state.Item, ref state.Items, ref state.DirectoryDone);
						break;
					}
			}
		}
	}

	// The open elements, the directory's items so far and the one being read, and whether the directory is done.
	sealed class ReadState
	{
		public readonly List<Frame> Stack = [];
		public List<Dictionary<string, string>>? Items;
		public Dictionary<string, string>? Item;
		public bool DirectoryDone;
	}

	static void Open (XmlReader reader, ReadState state, Found found)
	{
		var stack = state.Stack;
		var parent = stack.Count > 0 ? stack[^1] : null;
		var frame = Classify (reader, parent, found, state.DirectoryDone);
		if (frame.Role == Role.Description)
			ReadAttributes (reader, found.Values);
		if (frame.Role == Role.Directory)
			state.Items = [];
		if (frame.Role == Role.DirectoryLi && state.Items is not null) {
			state.Item = [];
			state.Items.Add (state.Item);
		}

		if (frame.Role is Role.ItemNode or Role.ItemDescription && state.Item is not null) {
			state.Item[ItemSeen] = "";
			ReadItemAttributes (reader, state.Item);
		}

		// Anything but the expected shape inside a kept value makes that value invalid: it is dropped.
		if (frame.Role == Role.Other && parent is not null) {
			if (parent.Role == Role.ItemField)
				parent.Invalid = true;
			else
				InvalidateProperty (stack);
		}

		if (reader.IsEmptyElement)
			Close (frame, stack, found, state.Item, ref state.Items, ref state.DirectoryDone);
		else
			stack.Add (frame);
	}

	static void AppendText (XmlReader reader, List<Frame> stack)
	{
		if (stack.Count == 0 || stack[^1].Role is not (Role.Property or Role.PropertyLi or Role.ItemField))
			return;
		var top = stack[^1];
		top.Text ??= new StringBuilder ();
		if (top.Text.Length <= 64)
			top.Text.Append (reader.Value);
	}

	static Frame Classify (XmlReader reader, Frame? parent, Found found, bool directoryDone)
	{
		var ns = reader.NamespaceURI;
		var name = reader.LocalName;
		var role = parent?.Role;
		if (ns == Rdf && name == "RDF")
			return new Frame (Role.Rdf, "");
		if (role == Role.Rdf)
			return new Frame (ns == Rdf && name == "Description" ? Role.Description : Role.Other, "");
		if (role == Role.Description) {
			if (ns == Container && name == "Directory" && !directoryDone)
				return new Frame (Role.Directory, "");
			foreach (var property in Properties) {
				if (property.Ns == ns && property.Name == name && !found.Values.ContainsKey (Key (property.Prefix, name)))
					return new Frame (Role.Property, Key (property.Prefix, name)) { Values = [] };
			}

			return new Frame (Role.Other, "");
		}

		if (role == Role.Property && ns == Rdf && name is "Seq" or "Bag" or "Alt")
			return new Frame (Role.PropertySeq, parent!.Key);
		if (role == Role.PropertySeq && ns == Rdf && name == "li")
			return new Frame (Role.PropertyLi, parent!.Key);
		if (role == Role.Directory && ns == Rdf && name == "Seq")
			return new Frame (Role.DirectorySeq, "");
		if (role == Role.DirectorySeq && ns == Rdf && name == "li")
			return new Frame (Role.DirectoryLi, "");
		// An item is rdf:li > Container:Item, or the RDF struct form rdf:li > rdf:Description > Container:Item.
		if (role == Role.DirectoryLi && ns == Rdf && name == "Description")
			return new Frame (Role.DirectoryLiDescription, "");
		if (role is Role.DirectoryLi or Role.DirectoryLiDescription && ns == Container && name == "Item")
			return new Frame (Role.ItemNode, "");
		if (role == Role.ItemNode && ns == Rdf && name == "Description")
			return new Frame (Role.ItemDescription, "");
		if (role is Role.ItemNode or Role.ItemDescription && ns == Item && Array.IndexOf (ItemFields, name) >= 0)
			return new Frame (Role.ItemField, name);
		return new Frame (Role.Other, "");
	}

	// The property a frame on top of the stack belongs to (Property, or Property > Seq > li), or null.
	static Frame? EnclosingProperty (List<Frame> stack)
	{
		for (var n = stack.Count - 1; n >= 0 && n >= stack.Count - 3; n--) {
			if (stack[n].Role == Role.Property)
				return stack[n];
			if (stack[n].Role is not (Role.PropertySeq or Role.PropertyLi))
				return null;
		}

		return null;
	}

	static void InvalidateProperty (List<Frame> stack)
	{
		if (EnclosingProperty (stack) is Frame property)
			property.Invalid = true;
	}

	static void Close (Frame frame, List<Frame> stack, Found found, Dictionary<string, string>? item, ref List<Dictionary<string, string>>? items, ref bool directoryDone)
	{
		switch (frame.Role) {
			case Role.Property: {
					if (frame.Invalid || found.Values.ContainsKey (frame.Key))
						return;
					var values = frame.Values!.Count > 0 ? frame.Values : frame.Text is null ? [] : [frame.Text.ToString ().Trim ()];
					if (IsValid (frame.Key, values))
						found.Values[frame.Key] = values;
					return;
				}

			case Role.PropertyLi: {
					var property = EnclosingProperty (stack);
					if (property is null)
						return;
					if (frame.Invalid || frame.Text is null)
						property.Invalid = true;
					else
						property.Values!.Add (frame.Text.ToString ().Trim ());
					return;
				}

			case Role.ItemField:
				if (!frame.Invalid && item is not null && frame.Text is not null && !item.ContainsKey (frame.Key) && IsItemValue (frame.Key, frame.Text.ToString ().Trim ()))
					item[frame.Key] = frame.Text.ToString ().Trim ();
				return;

			case Role.Directory:
				// Readers locate items by their order and lengths, so a slot is never removed: when one rdf:li
				// holds no Container:Item that can be read, the whole directory is dropped.
				if (items is { Count: > 0 } && found.Items is null && items.TrueForAll (i => i.ContainsKey (ItemSeen)))
					found.Items = items;
				directoryDone = true;
				items = null;
				return;
		}
	}

	static void ReadAttributes (XmlReader reader, Dictionary<string, List<string>> values)
	{
		if (!reader.MoveToFirstAttribute ())
			return;
		do {
			foreach (var property in Properties) {
				if (property.Ns != reader.NamespaceURI || property.Name != reader.LocalName)
					continue;
				var key = Key (property.Prefix, property.Name);
				if (!values.ContainsKey (key) && IsValid (key, [reader.Value.Trim ()]))
					values[key] = [reader.Value.Trim ()];
			}
		} while (reader.MoveToNextAttribute ());
		reader.MoveToElement ();
	}

	static void ReadItemAttributes (XmlReader reader, Dictionary<string, string> item)
	{
		if (!reader.MoveToFirstAttribute ())
			return;
		do {
			if (reader.NamespaceURI == Item && Array.IndexOf (ItemFields, reader.LocalName) >= 0 && !item.ContainsKey (reader.LocalName) && IsItemValue (reader.LocalName, reader.Value.Trim ()))
				item[reader.LocalName] = reader.Value.Trim ();
		} while (reader.MoveToNextAttribute ());
		reader.MoveToElement ();
	}

	static string Key (string prefix, string name) => prefix + ":" + name;

	static Shape ShapeOf (string key)
	{
		foreach (var property in Properties) {
			if (Key (property.Prefix, property.Name) == key)
				return property.Shape;
		}

		return Shape.Real;
	}

	static bool IsValid (string key, List<string> values)
	{
		var shape = ShapeOf (key);
		if (shape == Shape.RealOrThree)
			return values.Count == 1 ? IsReal (values[0]) : values.Count == 3 && values.TrueForAll (IsReal);
		if (values.Count != 1)
			return false;
		var value = values[0];
		return shape switch {
			Shape.Version => IsVersion (value),
			Shape.Boolean => value is "True" or "False",
			Shape.Real => IsReal (value),
			Shape.Integer => IsInteger (value),
			Shape.Long => IsInteger (value) || (value.Length > 1 && value[0] == '-' && IsInteger (value.Substring (1))),
			Shape.AppleType => value == AppleGainMapType,
			_ => false
		};
	}

	static bool IsItemValue (string field, string value) => field switch {
		"Semantic" => Array.IndexOf (Semantics, value) >= 0,
		"Mime" => Array.IndexOf (MimeTypes, value) >= 0,
		_ => IsInteger (value)
	};

	// "1.0": digits, one dot, digits.
	static bool IsVersion (string value)
	{
		if (value.Length is 0 or > 16)
			return false;
		var dots = 0;
		for (var i = 0; i < value.Length; i++) {
			if (value[i] == '.') {
				if (i == 0 || i == value.Length - 1 || ++dots > 1)
					return false;
			} else if (value[i] is < '0' or > '9') {
				return false;
			}
		}

		return dots == 1;
	}

	static bool IsReal (string value) =>
		value.Length is > 0 and <= 32
		&& double.TryParse (value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
		&& !double.IsNaN (number) && !double.IsInfinity (number);

	static bool IsInteger (string value) =>
		value.Length is > 0 and <= 19 && value.All (c => c is >= '0' and <= '9');

	// The first item is the primary; the next lengths.Count items are the later MPF images in file order. Items
	// after those (a motion-photo video, which Clear copies as it is) keep their lengths.
	static void SetItemLengths (List<Dictionary<string, string>> items, IReadOnlyList<long> lengths)
	{
		for (var n = 1; n < items.Count && n - 1 < lengths.Count; n++)
			items[n]["Length"] = lengths[n - 1].ToString (CultureInfo.InvariantCulture);
	}

	static string Write (Found found)
	{
		var builder = new StringBuilder (512);
		builder.Append ("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
		builder.Append ("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"").Append (Rdf).Append ("\">");
		builder.Append ("<rdf:Description rdf:about=\"\"");
		WriteNamespaces (builder, found);
		WriteAttributes (builder, found);
		builder.Append ('>');
		WriteElements (builder, found);
		if (found.Items is not null)
			WriteDirectory (builder, found.Items);
		builder.Append ("</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");
		return builder.ToString ();
	}

	static void WriteNamespaces (StringBuilder builder, Found found)
	{
		var used = new HashSet<string> ();
		foreach (var property in Properties) {
			if (found.Values.ContainsKey (Key (property.Prefix, property.Name)) && used.Add (property.Prefix))
				builder.Append (" xmlns:").Append (property.Prefix).Append ("=\"").Append (property.Ns).Append ('"');
		}

		if (found.Items is not null)
			builder.Append (" xmlns:Container=\"").Append (Container).Append ("\" xmlns:Item=\"").Append (Item).Append ('"');
	}

	// Ultra HDR writes single values as attributes (the format's examples); arrays and Apple's properties
	// (as ImageIO writes them) are elements.
	static bool IsAttribute (string ns, List<string> values) => ns is Hdrgm or Camera && values.Count == 1;

	static void WriteAttributes (StringBuilder builder, Found found)
	{
		foreach (var property in Properties) {
			var key = Key (property.Prefix, property.Name);
			if (found.Values.TryGetValue (key, out var values) && IsAttribute (property.Ns, values))
				builder.Append (' ').Append (key).Append ("=\"").Append (Escape (values[0])).Append ('"');
		}
	}

	static void WriteElements (StringBuilder builder, Found found)
	{
		foreach (var property in Properties) {
			var key = Key (property.Prefix, property.Name);
			if (!found.Values.TryGetValue (key, out var values) || IsAttribute (property.Ns, values))
				continue;
			builder.Append ('<').Append (key).Append ('>');
			if (values.Count == 1) {
				builder.Append (Escape (values[0]));
			} else {
				builder.Append ("<rdf:Seq>");
				foreach (var value in values)
					builder.Append ("<rdf:li>").Append (Escape (value)).Append ("</rdf:li>");
				builder.Append ("</rdf:Seq>");
			}

			builder.Append ("</").Append (key).Append ('>');
		}
	}

	static void WriteDirectory (StringBuilder builder, List<Dictionary<string, string>> items)
	{
		builder.Append ("<Container:Directory><rdf:Seq>");
		foreach (var item in items) {
			builder.Append ("<rdf:li rdf:parseType=\"Resource\"><Container:Item");
			foreach (var field in ItemFields) {
				if (item.TryGetValue (field, out var value))
					builder.Append (" Item:").Append (field).Append ("=\"").Append (Escape (value)).Append ('"');
			}

			builder.Append ("/></rdf:li>");
		}

		builder.Append ("</rdf:Seq></Container:Directory>");
	}

	static string Escape (string value)
	{
		var builder = new StringBuilder (value.Length);
		foreach (var c in value) {
			if (c == '&')
				builder.Append ("&amp;");
			else if (c == '<')
				builder.Append ("&lt;");
			else if (c == '"')
				builder.Append ("&quot;");
			else
				builder.Append (c);
		}

		return builder.ToString ();
	}
}
