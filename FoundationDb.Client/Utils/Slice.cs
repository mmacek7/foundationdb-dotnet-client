﻿#region BSD Licence
/* Copyright (c) 2013, Doxense SARL
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
	* Redistributions of source code must retain the above copyright
	  notice, this list of conditions and the following disclaimer.
	* Redistributions in binary form must reproduce the above copyright
	  notice, this list of conditions and the following disclaimer in the
	  documentation and/or other materials provided with the distribution.
	* Neither the name of the <organization> nor the
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

namespace FoundationDb.Client
{
	using FoundationDb.Client.Tuples;
	using FoundationDb.Client.Utils;
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Runtime.InteropServices;
	using System.Text;

	public struct Slice : IEquatable<Slice>, IEquatable<ArraySegment<byte>>, IEquatable<byte[]>, IComparable<Slice>
	{
		internal static readonly byte[] EmptyArray = new byte[0];

		/// <summary>Null slice ("no segment")</summary>
		public static readonly Slice Nil = default(Slice);

		/// <summary>Empty slice ("segment of 0 bytes")</summary>
		public static readonly Slice Empty = new Slice(EmptyArray, 0, 0);

		/// <summary>Pointer to the buffer (or null for Slice.Nil)</summary>
		public readonly byte[] Array;

		/// <summary>Offset of the first byte of the slice in the parent buffer</summary>
		public readonly int Offset;

		/// <summary>Number of bytes in the slice</summary>
		public readonly int Count;

		internal Slice(byte[] array)
		{
			Contract.Requires(array != null);

			this.Array = array;
			this.Offset = 0;
			this.Count = array != null ? array.Length : 0;
		}

		internal Slice(byte[] array, int offset, int count)
		{
			Contract.Requires(array != null);
			Contract.Requires(offset >= 0 && offset <= array.Length);
			Contract.Requires(count >= 0 && offset + count <= array.Length);

			this.Array = array;
			this.Offset = offset;
			this.Count = count;
		}

		/// <summary>Creates a slice mapping an entire buffer</summary>
		/// <param name="bytes"></param>
		/// <returns></returns>
		public static Slice Create(byte[] bytes)
		{
			return bytes == null ? Slice.Nil : bytes.Length == 0 ? Slice.Empty : new Slice(bytes, 0, bytes.Length);
		}

		/// <summary>Creates a slice mapping a section of a buffer</summary>
		/// <param name="buffer"></param>
		/// <param name="offset"></param>
		/// <param name="count"></param>
		/// <returns></returns>
		public static Slice Create(byte[] buffer, int offset, int count)
		{
			if (buffer == null) return Nil;
			if (count == 0) return Empty;
			if (offset < 0 || offset >= buffer.Length) throw new ArgumentException("offset");
			if (count < 0 || offset + count > buffer.Length) throw new ArgumentException("count");
			return new Slice(buffer, offset, count);
		}

		/// <summary>Create a new empty slice of a specified size containing all zeroes</summary>
		/// <param name="size"></param>
		/// <returns></returns>
		public static Slice Create(int size)
		{
			if (size < 0) throw new ArgumentException("size");
			return size == 0 ? Slice.Empty : new Slice(new byte[size], 0, size);
		}

		/// <summary>Creates a new slice with a copy of an unmanaged memory buffer</summary>
		/// <param name="ptr">Pointer to unmanaged buffer</param>
		/// <param name="count">Number of bytes in the buffer</param>
		/// <returns>Slice with a managed copy of the data</returns>
		internal static unsafe Slice Create(byte* ptr, int count)
		{
			if (ptr == null) return Slice.Nil;
			if (count <= 0) return Slice.Empty;

			var bytes = new byte[count];
			Marshal.Copy(new IntPtr(ptr), bytes, 0, count);
			return new Slice(bytes, 0, count);
		}

		/// <summary>Decode a Base64 encoded string into a slice</summary>
		public static Slice FromBase64(string base64String)
		{
			return base64String == null ? Slice.Nil : base64String.Length == 0 ? Slice.Empty : Slice.Create(Convert.FromBase64String(base64String));
		}

		public static Slice FromInt32(int value)
		{
			//HACKHACK: use something else! (endianness depends on plateform)
			return Slice.Create(BitConverter.GetBytes(value));
		}

		public static Slice FromInt64(long value)
		{
			//HACKHACK: use something else! (endianness depends on plateform)
			return Slice.Create(BitConverter.GetBytes(value));
		}

		public static Slice FromAscii(string text)
		{
			return text == null ? Slice.Nil : text.Length == 0 ? Slice.Empty : Slice.Create(Encoding.Default.GetBytes(text));
		}

		public static Slice FromString(string value)
		{
			return value == null ? Slice.Nil : value.Length == 0 ? Slice.Empty : Slice.Create(Encoding.UTF8.GetBytes(value));
		}

		private static int NibbleToDecimal(char c)
		{
			int x = c - 48;
			if (x < 10) return x;
			if (x >= 17 && x <= 42) return x - 7;
			if (x >= 49 && x <= 74) return x - 39;
			throw new FormatException("Input is not valid hexadecimal");
		}

		public static Slice FromHexa(string hexaString)
		{
			if (string.IsNullOrEmpty(hexaString)) return hexaString == null ? Slice.Nil : Slice.Empty;

			if ((hexaString.Length & 1) != 0) throw new ArgumentException("Hexadecimal string must be of even length", "hexaString");

			var buffer = new byte[hexaString.Length >> 1];
			for (int i = 0; i < hexaString.Length; i += 2)
			{
				buffer[i >> 1] = (byte) ((NibbleToDecimal(hexaString[i]) << 4) | NibbleToDecimal(hexaString[i + 1]));
			}
			return new Slice(buffer, 0, buffer.Length);
		}

		/// <summary>Returns true is the slice is not null</summary>
		/// <remarks>An empty slice is NOT considered null</remarks>
		public bool HasValue { get { return this.Array != null; } }

		/// <summary>Return true if the slice is not null but contains 0 bytes</summary>
		/// <remarks>A null slice is NOT empty</remarks>
		public bool IsEmpty { get { return this.Count == 0 && this.Array != null; } }

		/// <summary>Returns true if the slice does not contain at least 1 byte</summary>
		public bool IsNullOrEmpty { get { return this.Count == 0; } }

		/// <summary>Return a byte array containing all the bytes of the slice, or null if the slice is null</summary>
		/// <returns>Byte array with a copy of the slice, or null</returns>
		public byte[] GetBytes()
		{
			if (this.IsNullOrEmpty) return this.Array == null ? null : Slice.EmptyArray;
			var bytes = new byte[this.Count];
			Buffer.BlockCopy(this.Array, this.Offset, bytes, 0, bytes.Length);
			return bytes;
		}

		/// <summary>Stringify a slice containing only ASCII chars</summary>
		/// <returns>ASCII string, or null if the slice is null</returns>
		public string ToAscii()
		{
			if (this.IsNullOrEmpty) return this.HasValue ? String.Empty : default(string);
			return Encoding.Default.GetString(this.Array, this.Offset, this.Count);
		}

		/// <summary>Stringify a slice containing only ASCII chars</summary>
		/// <returns>ASCII string, or null if the slice is null</returns>
		public string ToAscii(int offset, int count)
		{
			if (count == 0) return String.Empty;
			//TODO: check args
			return Encoding.Default.GetString(this.Array, this.Offset, count);
		}

		/// <summary>Stringify a slice containing an UTF-8 encoded string</summary>
		/// <returns>Unicode string, or null if the slice is null</returns>
		public string ToUnicode()
		{
			if (this.IsNullOrEmpty) return this.HasValue ? String.Empty : default(string);
			return Encoding.UTF8.GetString(this.Array, this.Offset, this.Count);
		}

		/// <summary>Stringify a slice containing an UTF-8 encoded string</summary>
		/// <returns>Unicode string, or null if the slice is null</returns>
		public string ToUnicode(int offset, int count)
		{
			if (count == 0) return String.Empty;
			//TODO: check args
			return Encoding.UTF8.GetString(this.Array, this.Offset + offset, count);
		}

		/// <summary>Converts a slice using Base64 encoding</summary>
		public string ToBase64()
		{
			if (this.IsNullOrEmpty) return this.Array == null ? null : String.Empty;
			return Convert.ToBase64String(this.Array, this.Offset, this.Count);
		}

		/// <summary>Converts a slice into a string with each byte encoded into hexadecimal (lowercase)</summary>
		public string ToHexaString()
		{
			if (this.IsNullOrEmpty) return this.Array == null ? null : String.Empty;
			var buffer = this.Array;
			int p = this.Offset;
			int n = this.Count;
			var sb = new StringBuilder(n * 2);
			while (n-- > 0)
			{
				byte b = buffer[p++];
				int x = b & 0xF;
				sb.Append((char)(x + (x < 10 ? 48 : 87)));
				x = b >> 4;
				sb.Append((char)(x + (x < 10 ? 48 : 87)));
			}
			return sb.ToString();
		}

		/// <summary>Returns a new slice that contains an isolated copy of the buffer</summary>
		/// <returns>Slice that is equivalent, but is isolated from any changes to the buffer</returns>
		internal Slice Memoize()
		{
			if (this.IsNullOrEmpty) return this.Array == null ? Slice.Nil : Slice.Empty;
			return new Slice(GetBytes(), 0, this.Count);
		}

		/// <summary>Map an offset in the slice into the absolute offset in the buffer, without any bound checking</summary>
		/// <param name="index">Relative offset (negative values mean from the end)</param>
		/// <returns>Absolute offset in the buffer</returns>
		private int UnsafeMapToOffset(int index)
		{
			int p = index;
			if (p < 0) p += this.Count;
			Contract.Requires(p >= 0 & p < this.Count);
			return this.Offset + p;
		}

		/// <summary>Map an offset in the slice into the absolute offset in the buffer</summary>
		/// <param name="index">Relative offset (negative values mean from the end)</param>
		/// <returns>Absolute offset in the buffer</returns>
		/// <exception cref="IndexOutOfRangeException">If the index is outside the slice</exception>
		private int MapToOffset(int index)
		{
			int p = index;
			if (p < 0) p += this.Count;
			if (p < 0 || p >= this.Count) FailIndexOutOfBound(index);
			return this.Offset + p;
		}

		/// <summary>Returns the value of one byte in the slice</summary>
		/// <param name="index">Offset of the byte (negative values means start from the end)</param>
		public byte this[int index]
		{
			get { return this.Array[MapToOffset(index)]; }
		}

		private static void FailIndexOutOfBound(int index)
		{
			throw new IndexOutOfRangeException("Index is outside the slice");
		}

		internal byte GetByte(int index)
		{
			return this.Array[UnsafeMapToOffset(index)];
		}

		public Slice Substring(int offset)
		{
			//TODO: param check
			if (offset < 0)
			{ // from the end
				return new Slice(this.Array, this.Offset + offset, this.Count - offset);
			}
			else
			{ // from the start
				return new Slice(this.Array, this.Offset + this.Count + offset, -offset);
			}
		}

		public Slice Substring(int offset, int count)
		{
			//TODO: param check
			return new Slice(this.Array, this.Offset + offset, count);
		}

		public ulong ReadUInt64(int offset, int bytes)
		{
			ulong value = 0;
			var buffer = this.Array;
			int p = UnsafeMapToOffset(offset) + bytes - 1;
			while (bytes-- > 0)
			{
				value <<= 8;
				value |= buffer[p--];
			}
			return value;
		}

		/// <summary>Implicitly converts a Slice into an ArraySegment&lt;byte&gt;</summary>
		public static implicit operator ArraySegment<byte>(Slice value)
		{
			return new ArraySegment<byte>(value.Array, value.Offset, value.Count);
		}

		/// <summary>Implicitly converts an ArraySegment&lt;byte&gt; into a Slice</summary>
		public static implicit operator Slice(ArraySegment<byte> value)
		{
			return new Slice(value.Array, value.Offset, value.Count);
		}

		/// <summary>Compare two slices for equality</summary>
		/// <returns>True if the slice contains the same bytes</returns>
		public static bool operator ==(Slice a, Slice b)
		{
			return a.Equals(b);
		}

		/// <summary>Compare two slices for inequality</summary>
		/// <returns>True if the slice do not contain the same bytes</returns>
		public static bool operator !=(Slice a, Slice b)
		{
			return !a.Equals(b);
		}

		public override string ToString()
		{
			return Slice.Escape(this);
		}

		public static string Escape(Slice value)
		{
			if (value.IsNullOrEmpty) return value.HasValue ? "<empty>" : "<null>";

			var buffer = value.Array;
			int n = value.Count;
			int p = value.Offset;
			var sb = new StringBuilder(n + 16);
			while (n-- > 0)
			{
				int c = buffer[p++];
				if (c < 32 || c >= 127 || c == 60) sb.Append('<').Append(c.ToString("X2")).Append('>'); else sb.Append((char)c);
			}
			return sb.ToString();
		}

		public static Slice Unescape(string value)
		{
			var writer = new FdbBufferWriter();
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (c == '<')
				{
					if (value[i + 3] != '>') throw new FormatException("Invalid escape slice string");
					c = (char)(NibbleToDecimal(value[i + 1]) << 4 | NibbleToDecimal(value[i + 2]));
					i += 3;
				}
				writer.WriteByte((byte)c);
			}
			return writer.ToSlice();
		}

		public override bool Equals(object obj)
		{
			if (obj == null) return this.Array == null;
			if (obj is Slice) return Equals((Slice)obj);
			if (obj is ArraySegment<byte>) return Equals((ArraySegment<byte>)obj);
			if (obj is byte[]) return Equals((byte[])obj);
			return false;
		}

		public override int GetHashCode()
		{
			if (this.Array != null)
			{
				//TODO: use a better hash algorithm? (CityHash, SipHash, ...)

				// <HACKHACK>: unoptimized 32 bits FNV-1a implementation
				uint h = 2166136261; // FNV1 32 bits offset basis
				var bytes = this.Array;
				int p = this.Offset;
				int count = this.Count;
				while(count-- > 0)
				{
					h = (h ^ bytes[p++]) * 16777619; // FNV1 32 prime
				}
				return (int)h;
				// </HACKHACK>
			}
			return 0;
		}

		public bool Equals(Slice other)
		{
			return this.Count == other.Count && SameBytes(this.Array, this.Offset, other.Array, other.Offset, this.Count);
		}

		/// <summary>Lexicographically compare this slice with another one</summary>
		/// <param name="other">Other slice to compare</param>
		/// <returns>0 for equal, positive if we are greater, negative if we are smaller</returns>
		public int CompareTo(Slice other)
		{
			if (!other.HasValue) return this.HasValue ? 1 : 0;
			return CompareBytes(this.Array, this.Offset, this.Count, other.Array, other.Offset, other.Count);
		}

		public bool Equals(ArraySegment<byte> other)
		{
			return this.Count == other.Count && SameBytes(this.Array, this.Offset, other.Array, other.Offset, this.Count);
		}

		public bool Equals(byte[] other)
		{
			if (other == null) return this.Array == null;
			return this.Count == other.Length && SameBytes(this.Array, this.Offset, other, 0, this.Count);
		}

		internal static bool SameBytes(byte[] left, int leftOffset, byte[] right, int rightOffset, int count)
		{
			Contract.Requires(count >= 0);
			Contract.Requires(leftOffset >= 0);
			Contract.Requires(rightOffset >= 0);

			if (left == null) return object.ReferenceEquals(right, null);
			if (object.ReferenceEquals(left, right)) return leftOffset == rightOffset;

			//TODO: ensure that there are enough bytes on both sides

			while (count-- > 0)
			{
				if (left[leftOffset++] != right[rightOffset++]) return false;
			}
			return true;
		}

		internal static int CompareBytes(byte[] left, int leftOffset, int leftCount, byte[] right, int rightOffset, int rightCount)
		{
			if (leftCount == rightCount && leftOffset == rightOffset && object.ReferenceEquals(left, right))
				return 0;

			int n = Math.Min(leftCount, rightCount);

			while (n-- > 0)
			{
				int d = right[rightOffset++] - left[leftOffset++];
				if (d != 0) return d;
			}

			return rightCount - leftCount;
		}

	}

}
