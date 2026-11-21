// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Core;

/// <summary>
/// Shared argument rules for <see cref="Tag.GetCustomField"/>, <see cref="Tag.SetCustomField"/>,
/// and <see cref="Tag.RemoveCustomField"/>.
/// </summary>
internal static class CustomFieldValues
{
	/// <summary>
	/// An empty result for a name the tag does not hold.
	/// </summary>
	public static IReadOnlyList<string> None { get; } = Array.Empty<string> ();

	/// <summary>
	/// Throws when <paramref name="name"/> is null. Every other bad name is reported by the return value.
	/// </summary>
	public static void ThrowIfNull ([ValidatedNotNull] string name)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		Polyfills.ThrowIfNull (name, nameof (name));
#else
		ArgumentNullException.ThrowIfNull (name);
#endif
	}

	/// <summary>
	/// True when <paramref name="name"/> is not empty and has no U+0000. Every format needs at least that.
	/// </summary>
	public static bool IsUsableName (string name) =>
		name.Length > 0 && !HasNul (name);

	/// <summary>
	/// Drops null and empty values and keeps the order of the rest.
	/// </summary>
	/// <param name="values">The caller's values. Null means none.</param>
	/// <param name="kept">The values to store. Empty means the field is removed.</param>
	/// <returns>
	/// False when a value contains U+0000. ID3v2 and APE use it to separate values and RIFF INFO uses it
	/// to end the string, so no format could store that value as given.
	/// </returns>
	public static bool TryNormalize (string?[]? values, out List<string> kept)
	{
		kept = new List<string> (values?.Length ?? 0);
		if (values is null)
			return true;

		foreach (var value in values) {
			if (string.IsNullOrEmpty (value))
				continue;
			if (HasNul (value!)) {
				kept.Clear ();
				return false;
			}
			kept.Add (value!);
		}

		return true;
	}

	static bool HasNul (string text)
	{
		foreach (var c in text) {
			if (c == '\0')
				return true;
		}
		return false;
	}

	/// <summary>
	/// Splits a NUL-separated value list. Empty parts, including a trailing terminator, are not values.
	/// </summary>
	public static void AddSplit (string text, List<string> into)
	{
		if (text.Length == 0)
			return;

		foreach (var part in text.Split ('\0')) {
			if (part.Length > 0)
				into.Add (part);
		}
	}
}

/// <summary>
/// Tells CA1062 that a helper throws when this argument is null.
/// </summary>
[AttributeUsage (AttributeTargets.Parameter)]
internal sealed class ValidatedNotNullAttribute : Attribute
{
}
