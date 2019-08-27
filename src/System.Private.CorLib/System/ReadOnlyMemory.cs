// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

using Internal.Runtime.CompilerServices;

#pragma warning disable SA1121 // explicitly using type aliases instead of built-in types
#if BIT64
using nuint = System.UInt64;
#else // BIT64
using nuint = System.UInt32;
#endif // BIT64

namespace System
{
    /// <summary>
    /// Represents a contiguous region of memory, similar to <see cref="ReadOnlySpan{T}"/>.
    /// Unlike <see cref="ReadOnlySpan{T}"/>, it is not a byref-like type.
    /// </summary>
    [DebuggerDisplay("{ToString(),raw}")]
    public readonly struct ReadOnlyMemory<T> : IEquatable<ReadOnlyMemory<T>>
    {
        // NOTE: With the current implementation, Memory<T> and ReadOnlyMemory<T> must have the same layout,
        // as code uses Unsafe.As to cast between them.

        // The highest order bit of _index is used to discern whether _object is a pre-pinned array.
        // (_index < 0) => _object is a pre-pinned array, so Pin() will not allocate a new GCHandle
        //       (else) => Pin() needs to allocate a new GCHandle to pin the object.
        private readonly object? _object;
        private readonly int _index;
        private readonly int _length;

        internal const int RemoveFlagsBitMask = 0x7FFFFFFF;

        /// <summary>
        /// Creates a new memory over the entirety of the target array.
        /// </summary>
        /// <param name="array">The target array.</param>
        /// <remarks>Returns default when <paramref name="array"/> is null.</remarks>
        /// <exception cref="System.ArrayTypeMismatchException">Thrown when <paramref name="array"/> is covariant and array's type is not exactly T[].</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory(T[]? array)
        {
            if (array == null)
            {
                this = default;
                return; // returns default
            }

            _object = array;
            _index = 0;
            _length = array.Length;
        }

        /// <summary>
        /// Creates a new memory over the portion of the target array beginning
        /// at 'start' index and ending at 'end' index (exclusive).
        /// </summary>
        /// <param name="array">The target array.</param>
        /// <param name="start">The index at which to begin the memory.</param>
        /// <param name="length">The number of items in the memory.</param>
        /// <remarks>Returns default when <paramref name="array"/> is null.</remarks>
        /// <exception cref="System.ArrayTypeMismatchException">Thrown when <paramref name="array"/> is covariant and array's type is not exactly T[].</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the specified <paramref name="start"/> or end index is not in the range (&lt;0 or &gt;Length).
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory(T[]? array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException();
                this = default;
                return; // returns default
            }
#if BIT64
            // See comment in Span<T>.Slice for how this works.
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)array.Length)
                ThrowHelper.ThrowArgumentOutOfRangeException();
#else
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start))
                ThrowHelper.ThrowArgumentOutOfRangeException();
#endif

            _object = array;
            _index = start;
            _length = length;
        }

        /// <summary>Creates a new memory over the existing object, start, and length. No validation is performed.</summary>
        /// <param name="obj">The target object.</param>
        /// <param name="start">The index at which to begin the memory.</param>
        /// <param name="length">The number of items in the memory.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlyMemory(object? obj, int start, int length)
        {
            // No validation performed in release builds; caller must provide any necessary validation.

            // 'obj is T[]' below also handles things like int[] <-> uint[] being convertible
            Debug.Assert((obj == null)
                || (typeof(T) == typeof(char) && obj is string)
#if FEATURE_UTF8STRING
                || ((typeof(T) == typeof(byte) || typeof(T) == typeof(Char8)) && obj is Utf8String)
#endif // FEATURE_UTF8STRING
                || (obj is T[])
                || (obj is MemoryManager<T>));

            _object = obj;
            _index = start;
            _length = length;
        }

        /// <summary>
        /// Defines an implicit conversion of an array to a <see cref="ReadOnlyMemory{T}"/>
        /// </summary>
        public static implicit operator ReadOnlyMemory<T>(T[]? array) => new ReadOnlyMemory<T>(array);

        /// <summary>
        /// Defines an implicit conversion of a <see cref="ArraySegment{T}"/> to a <see cref="ReadOnlyMemory{T}"/>
        /// </summary>
        public static implicit operator ReadOnlyMemory<T>(ArraySegment<T> segment) => new ReadOnlyMemory<T>(segment.Array, segment.Offset, segment.Count);

        /// <summary>
        /// Returns an empty <see cref="ReadOnlyMemory{T}"/>
        /// </summary>
        public static ReadOnlyMemory<T> Empty => default;

        /// <summary>
        /// The number of items in the memory.
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// Returns true if Length is 0.
        /// </summary>
        public bool IsEmpty => _length == 0;

        /// <summary>
        /// Forms a slice out of the given memory, beginning at 'start'.
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the specified <paramref name="start"/> index is not in range (&lt;0 or &gt;Length).
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.start);
            }

            // It is expected for _index + start to be negative if the memory is already pre-pinned.
            return new ReadOnlyMemory<T>(_object, _index + start, _length - start);
        }

        /// <summary>
        /// Forms a slice out of the given memory, beginning at 'start', of given length
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="length">The desired length for the slice (exclusive).</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the specified <paramref name="start"/> or end index is not in range (&lt;0 or &gt;Length).
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<T> Slice(int start, int length)
        {
#if BIT64
            // See comment in Span<T>.Slice for how this works.
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)_length)
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.start);
#else
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.start);
#endif

            // It is expected for _index + start to be negative if the memory is already pre-pinned.
            return new ReadOnlyMemory<T>(_object, _index + start, length);
        }

        /// <summary>
        /// Returns a span from the memory.
        /// </summary>
        public unsafe ReadOnlySpan<T> Span
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ref T refToReturn = ref Unsafe.AsRef<T>(null);
                int lengthOfUnderlyingSpan = 0;

                // Copy this field into a local so that it can't change out from under us mid-operation.

                object? tmpObject = _object;
                if (tmpObject != null)
                {
                    if (typeof(T) == typeof(char) && tmpObject.GetType() == typeof(string))
                    {
                        // Special-case string since it's the most common for ROM<char>.

                        refToReturn = ref Unsafe.As<char, T>(ref Unsafe.As<string>(tmpObject).GetRawStringData());
                        lengthOfUnderlyingSpan = Unsafe.As<string>(tmpObject).Length;
                    }
#if FEATURE_UTF8STRING
                    else if ((typeof(T) == typeof(byte) || typeof(T) == typeof(Char8)) && tmpObject.GetType() == typeof(Utf8String))
                    {
                        refToReturn = ref Unsafe.As<byte, T>(ref Unsafe.As<Utf8String>(tmpObject).DangerousGetMutableReference());
                        lengthOfUnderlyingSpan = Unsafe.As<Utf8String>(tmpObject).Length;
                    }
#endif // FEATURE_UTF8STRING
                    else if (RuntimeHelpers.ObjectHasComponentSize(tmpObject))
                    {
                        // We know the object is not null, it's not a string, and it is variable-length. The only
                        // remaining option is for it to be a T[] (or a U[] which is blittable to T[], like int[]
                        // and uint[]). Otherwise somebody used private reflection to set this field, and we're not
                        // too worried about type safety violations at this point.

                        // 'tmpObject is T[]' below also handles things like int[] <-> uint[] being convertible
                        Debug.Assert(tmpObject is T[]);

                        refToReturn = ref Unsafe.As<byte, T>(ref Unsafe.As<T[]>(tmpObject).GetRawSzArrayData());
                        lengthOfUnderlyingSpan = Unsafe.As<T[]>(tmpObject).Length;
                    }
                    else
                    {
                        // We know the object is not null, and it's not variable-length, so it must be a MemoryManager<T>.
                        // Otherwise somebody used private reflection to set this field, and we're not too worried about
                        // type safety violations at that point. Note that it can't be a MemoryManager<U>, even if U and
                        // T are blittable (e.g., MemoryManager<int> to MemoryManager<uint>), since there exists no
                        // constructor or other public API which would allow such a conversion.

                        Debug.Assert(tmpObject is MemoryManager<T>);
                        Span<T> memoryManagerSpan = Unsafe.As<MemoryManager<T>>(tmpObject).GetSpan();
                        refToReturn = ref MemoryMarshal.GetReference(memoryManagerSpan);
                        lengthOfUnderlyingSpan = memoryManagerSpan.Length;
                    }

                    // If the Memory<T> or ReadOnlyMemory<T> instance is torn, this property getter has undefined behavior.
                    // We try to detect this condition and throw an exception, but it's possible that a torn struct might
                    // appear to us to be valid, and we'll return an undesired span. Such a span is always guaranteed at
                    // least to be in-bounds when compared with the original Memory<T> instance, so using the span won't
                    // AV the process.

                    nuint desiredStartIndex = (uint)_index & (uint)RemoveFlagsBitMask;
                    int desiredLength = _length;

#if BIT64
                    // See comment in Span<T>.Slice for how this works.
                    if ((ulong)desiredStartIndex + (ulong)(uint)desiredLength > (ulong)(uint)lengthOfUnderlyingSpan)
                    {
                        ThrowHelper.ThrowArgumentOutOfRangeException();
                    }
#else
                    if ((uint)desiredStartIndex > (uint)lengthOfUnderlyingSpan || (uint)desiredLength > (uint)(lengthOfUnderlyingSpan - desiredStartIndex))
                    {
                        ThrowHelper.ThrowArgumentOutOfRangeException();
                    }
#endif

                    refToReturn = ref Unsafe.Add(ref refToReturn, (IntPtr)(void*)desiredStartIndex);
                    lengthOfUnderlyingSpan = desiredLength;
                }

                return new ReadOnlySpan<T>(ref refToReturn, lengthOfUnderlyingSpan);
            }
        }

        /// <summary>
        /// Copies the contents of the read-only memory into the destination. If the source
        /// and destination overlap, this method behaves as if the original values are in
        /// a temporary location before the destination is overwritten.
        ///
        /// <param name="destination">The Memory to copy items into.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the destination is shorter than the source.
        /// </exception>
        /// </summary>
        public void CopyTo(Memory<T> destination) => Span.CopyTo(destination.Span);

        /// <summary>
        /// Copies the contents of the readonly-only memory into the destination. If the source
        /// and destination overlap, this method behaves as if the original values are in
        /// a temporary location before the destination is overwritten.
        ///
        /// <returns>If the destination is shorter than the source, this method
        /// return false and no data is written to the destination.</returns>
        /// </summary>
        /// <param name="destination">The span to copy items into.</param>
        public bool TryCopyTo(Memory<T> destination) => Span.TryCopyTo(destination.Span);

        /// <summary>
        /// Copies the contents from the memory into a new array.  This heap
        /// allocates, so should generally be avoided, however it is sometimes
        /// necessary to bridge the gap with APIs written in terms of arrays.
        /// </summary>
        public T[] ToArray() => Span.ToArray();

        /// <summary>Determines whether the specified object is equal to the current object.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object? obj)
        {
            if (obj is ReadOnlyMemory<T> readOnlyMemory)
            {
                return Equals(readOnlyMemory);
            }
            else if (obj is Memory<T> memory)
            {
                return Equals(memory);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if the memory points to the same array and has the same length.  Note that
        /// this does *not* check to see if the *contents* are equal.
        /// </summary>
        public bool Equals(ReadOnlyMemory<T> other)
        {
            return
                _object == other._object &&
                _index == other._index &&
                _length == other._length;
        }

        /// <summary>Returns the hash code for this <see cref="ReadOnlyMemory{T}"/></summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override int GetHashCode()
        {
            // We use RuntimeHelpers.GetHashCode instead of Object.GetHashCode because the hash
            // code is based on object identity and referential equality, not deep equality (as common with string).
            return (_object != null) ? HashCode.Combine(RuntimeHelpers.GetHashCode(_object), _index, _length) : 0;
        }

        /// <summary>Gets the state of the memory as individual fields.</summary>
        /// <param name="start">The offset.</param>
        /// <param name="length">The count.</param>
        /// <returns>The object.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal object? GetObjectStartLength(out int start, out int length)
        {
            start = _index;
            length = _length;
            return _object;
        }
    }
}
