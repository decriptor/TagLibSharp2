// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image.Tiff;

/// <summary>
/// The only collection the IFD walks and the Exif removal keep per entry. Every add, read, write and sort adds
/// to <see cref="IfdSteps"/>, and it has no enumerator and no search, so any per-entry scan of it shows in the
/// operation count.
/// </summary>
sealed class CountedList<T>
{
	T[] _items = new T[8];

	public static CountedList<T> Filled (int count) => new () { _items = new T[Math.Max (count, 1)], Count = count };

	public int Count { get; private set; }

	public T this[int index] {
		get {
			IfdSteps.Count++;
			if ((uint)index >= (uint)Count)
				throw new ArgumentOutOfRangeException (nameof (index));
			return _items[index];
		}
		set {
			IfdSteps.Count++;
			if ((uint)index >= (uint)Count)
				throw new ArgumentOutOfRangeException (nameof (index));
			_items[index] = value;
		}
	}

	public void Add (T item)
	{
		IfdSteps.Count++;
		if (Count == _items.Length)
			Array.Resize (ref _items, _items.Length * 2);
		_items[Count++] = item;
	}

	public void Sort (Comparison<T> comparison)
	{
		IfdSteps.Count += Count;
		Array.Sort (_items, 0, Count, Comparer<T>.Create (comparison));
	}
}
