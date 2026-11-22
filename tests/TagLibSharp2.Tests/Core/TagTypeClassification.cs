// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using TagLibSharp2.Core;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// Sorts every concrete Tag type into leaf format tags, which store their own fields and read no other
/// tag, and views, which read other tags and must say which (Leaves). A type that is neither is
/// unclassified: a new view that does not name its tags would cut lists on a copy between it and one of
/// its own tags.
/// </summary>
static class TagTypeClassification
{
	/// <summary>
	/// Leaf format tags, by full name. A leaf derives directly from Tag and keeps the base Leaves.
	/// </summary>
	internal static readonly string[] LeafTagTypeNames = [
		"TagLibSharp2.Ape.ApeTag", "TagLibSharp2.Asf.AsfTag", "TagLibSharp2.Audible.AudibleTag", "TagLibSharp2.Id3.Id3v1Tag",
		"TagLibSharp2.Id3.Id3v2.Id3v2Tag", "TagLibSharp2.Matroska.MatroskaTag", "TagLibSharp2.Mp4.Mp4Tag", "TagLibSharp2.Riff.RiffInfoTag",
		"TagLibSharp2.Xiph.VorbisComment",
	];

	/// <summary>
	/// Every concrete Tag type in <paramref name="assembly"/>, the library by default.
	/// </summary>
	internal static IReadOnlyList<Type> ConcreteTagTypes (Assembly? assembly = null) =>
		(assembly ?? typeof (Tag).Assembly).GetTypes ()
			.Where (t => t.IsClass && !t.IsAbstract && typeof (Tag).IsAssignableFrom (t))
			.ToList ();

	/// <summary>
	/// True when <paramref name="type"/> declares Leaves itself. An inherited Leaves was written for the
	/// base's tags, not the ones the type adds.
	/// </summary>
	internal static bool IsView (Type type) => LeavesDeclaringType (type) == type;

	/// <summary>
	/// True when <paramref name="type"/> is a listed leaf that derives directly from Tag and keeps the base Leaves.
	/// </summary>
	internal static bool IsLeaf (Type type) =>
		LeavesDeclaringType (type) == typeof (Tag) && type.BaseType == typeof (Tag) && LeafTagTypeNames.Contains (type.FullName);

	/// <summary>
	/// Null when <paramref name="type"/> is a view or a leaf; otherwise why it is unclassified.
	/// </summary>
	internal static string? Unclassified (Type type) =>
		IsView (type) || IsLeaf (type) ? null : $"{type.FullName} (Leaves from {LeavesDeclaringType (type)!.Name})";

	/// <summary>
	/// The library's views: every concrete Tag type that declares Leaves itself.
	/// </summary>
	internal static IReadOnlyList<Type> ViewTypes () => ConcreteTagTypes ().Where (IsView).ToList ();

	/// <summary>
	/// The library's unclassified Tag types, with the reason for each; empty when every type is a leaf or a view.
	/// </summary>
	internal static IReadOnlyList<string> Unclassified () =>
		ConcreteTagTypes ().Select (Unclassified).OfType<string> ().ToList ();

	static Type? LeavesDeclaringType (Type type) =>
		type.GetMethod ("Leaves", BindingFlags.Instance | BindingFlags.NonPublic)!.DeclaringType;
}
