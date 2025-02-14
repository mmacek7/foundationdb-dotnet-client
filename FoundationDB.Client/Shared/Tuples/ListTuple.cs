﻿#region BSD License
/* Copyright (c) 2013-2020, Doxense SAS
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
	* Redistributions of source code must retain the above copyright
	  notice, this list of conditions and the following disclaimer.
	* Redistributions in binary form must reproduce the above copyright
	  notice, this list of conditions and the following disclaimer in the
	  documentation and/or other materials provided with the distribution.
	* Neither the name of Doxense nor the
	  names of its contributors may be used to endorse or promote products
	  derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
#endregion

#if !USE_SHARED_FRAMEWORK

namespace Doxense.Collections.Tuples
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Diagnostics.CodeAnalysis;
	using System.Linq;
	using Doxense.Diagnostics.Contracts;
	using Doxense.Runtime.Converters;
	using JetBrains.Annotations;

	/// <summary>Tuple that can hold any number of untyped items</summary>
	[PublicAPI]
	public sealed class ListTuple<T> : IVarTuple
	{
		// We could use a ListTuple<T> for tuples where all items are of type T, and ListTuple could derive from ListTuple<object>.
		// => this could speed up a bit the use case of STuple.FromArray<T> or STuple.FromSequence<T>

		/// <summary>List of the items in the tuple.</summary>
		/// <remarks>It is supposed to be immutable!</remarks>
		private readonly ReadOnlyMemory<T> m_items;

		private int? m_hashCode;

		/// <summary>Create a new tuple from a sequence of items (copied)</summary>
		public ListTuple([InstantHandle] IEnumerable<T> items)
			: this(items.ToArray().AsMemory())
		{ }

		public ListTuple(T[] items, int offset, int count)
			: this(items.AsMemory(offset, count))
		{ }

		public ListTuple(T[] items)
			: this(items.AsMemory())
		{ }

		/// <summary>Wrap a List of items</summary>
		/// <remarks>The list should not mutate and should not be exposed to anyone else!</remarks>
		public ListTuple(ReadOnlyMemory<T> items)
		{
			m_items = items.Length != 0 ? items : default;
		}

		/// <summary>Create a new list tuple by merging the items of two tuples together</summary>
		public ListTuple(ListTuple<T> a, ListTuple<T> b)
		{
			Contract.NotNull(a);
			Contract.NotNull(b);

			int nA = a.Count;
			int nB = b.Count;

			var items = new T[checked(nA + nB)];

			if (nA > 0) a.CopyTo(items, 0);
			if (nB > 0) b.CopyTo(items, nA);
			m_items = items.AsMemory();
		}

		/// <summary>Create a new list tuple by merging the items of three tuples together</summary>
		public ListTuple(ListTuple<T> a, ListTuple<T> b, ListTuple<T> c)
		{
			Contract.NotNull(a);
			Contract.NotNull(b);
			Contract.NotNull(c);

			int nA = a.Count;
			int nB = b.Count;
			int nC = c.Count;

			var items = new T[checked(nA + nB + nC)];

			if (nA > 0) a.CopyTo(items, 0);
			if (nB > 0) b.CopyTo(items, nA);
			if (nC > 0) c.CopyTo(items, nA + nB);

			m_items = items;
		}

		public int Count => m_items.Length;

		object? IReadOnlyList<object?>.this[int index] => this[index];

		object? IVarTuple.this[int index] => this[index];

		public T this[int index] => m_items.Span[TupleHelpers.MapIndex(index, m_items.Length)];

		public IVarTuple this[int? fromIncluded, int? toExcluded]
		{
			get
			{
				int count = m_items.Length;
				int begin = fromIncluded.HasValue ? TupleHelpers.MapIndexBounded(fromIncluded.Value, count) : 0;
				int end = toExcluded.HasValue ? TupleHelpers.MapIndexBounded(toExcluded.Value, count) : count;

				int len = end - begin;
				if (len <= 0) return STuple.Empty;
				if (begin == 0 && len == count) return this;

				Contract.Debug.Assert(begin >= 0);
				Contract.Debug.Assert((uint) len <= count);

				return new ListTuple<T>(m_items.Slice(begin, len));
			}
		}

#if USE_RANGE_API

		object? IVarTuple.this[Index index] => this[index];

		public T this[Index index] => m_items.Span[TupleHelpers.MapIndex(index, m_items.Length)];

		public IVarTuple this[Range range]
		{
			get
			{
				(int offset, int count) = range.GetOffsetAndLength(m_items.Length);
				if (count == 0) return STuple.Empty;
				if (offset == 0 && count == this.Count) return this;
				return new ListTuple<T>(m_items.Slice(offset, count));
			}
		}

#endif

		[return: MaybeNull]
		public TItem Get<TItem>(int index)
		{
			return TypeConverters.ConvertBoxed<TItem>(this[index]);
		}

		[return: MaybeNull]
		public TItem First<TItem>()
		{
			if (m_items.Length == 0) throw new InvalidOperationException("Tuple is empty.");
			return TypeConverters.ConvertBoxed<TItem>(m_items.Span[0]);
		}

		[return: MaybeNull]
		public TItem Last<TItem>()
		{
			if (m_items.Length == 0) throw new InvalidOperationException("Tuple is empty.");
#if USE_RANGE_API
			var value = m_items.Span[^1];
#else
			var value = m_items.Span[m_items.Length - 1];
#endif
			return TypeConverters.ConvertBoxed<TItem>(value);
		}

		public IVarTuple Append<TItem>(TItem value)
		{
			return new LinkedTuple<TItem>(this, value);
		}

		public IVarTuple Concat(IVarTuple tuple)
		{
			return STuple.Concat(this, tuple);
		}

		void IVarTuple.CopyTo(object?[] array, int offset)
		{
			int p = offset;
			foreach(var item in m_items.Span)
			{
				array[p++] = item;
			}
		}

		public void CopyTo(T[] array, int offset)
		{
			m_items.Span.CopyTo(array.AsSpan(offset));
		}

		IEnumerator<object?> IEnumerable<object?>.GetEnumerator()
		{
			for(int i = 0; i < m_items.Length; i++)
			{
				yield return m_items.Span[i];
			}
		}

		public IEnumerator<T> GetEnumerator()
		{
			for (int i = 0; i < m_items.Length; i++)
			{
				yield return m_items.Span[i];
			}
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return this.GetEnumerator();
		}

		public override string ToString()
		{
			return STuple.Formatter.ToString(m_items.Span);
		}

		private bool CompareItems(ReadOnlySpan<T> theirs, IEqualityComparer comparer)
		{
			int p = 0;
			var items = m_items.Span;
			int count = items.Length;
			foreach (var item in theirs)
			{
				if (p >= count) return false;

				if (item == null)
				{
					if (items[p] != null) return false;
				}
				else
				{
					if (!comparer.Equals(item, items[p])) return false;
				}
				p++;
			}
			return p >= count;
		}

		public override bool Equals(object? obj)
		{
			return obj != null && ((IStructuralEquatable)this).Equals(obj, SimilarValueComparer.Default);
		}

		public bool Equals(IVarTuple other)
		{
			return !object.ReferenceEquals(other, null) && ((IStructuralEquatable)this).Equals(other, SimilarValueComparer.Default);
		}

		public override int GetHashCode()
		{
			return ((IStructuralEquatable)this).GetHashCode(SimilarValueComparer.Default);
		}

		bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer)
		{
			if (object.ReferenceEquals(this, other)) return true;
			if (other == null) return false;

			if (other is ListTuple<T> list)
			{
				return list.Count == Count && CompareItems(list.m_items.Span, comparer);
			}

			return TupleHelpers.Equals(this, other, comparer);
		}

		int IStructuralEquatable.GetHashCode(System.Collections.IEqualityComparer comparer)
		{
			// the cached hashcode is only valid for the default comparer!
			bool canUseCache = object.ReferenceEquals(comparer, SimilarValueComparer.Default);
			if (m_hashCode.HasValue && canUseCache)
			{
				return m_hashCode.Value;
			}

			int h = 0;
			foreach(var item in m_items.Span)
			{
				h = HashCodes.Combine(h, comparer.GetHashCode(item));
			}
			if (canUseCache) m_hashCode = h;
			return h;
		}

	}

}

#endif
