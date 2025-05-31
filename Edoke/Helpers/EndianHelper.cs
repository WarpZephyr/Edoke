using System;
using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Edoke.Helpers
{
    /// <summary>
    /// A helper for endianness operations.
    /// </summary>
    internal static partial class EndianHelper
    {
        /// <summary>
        /// Copies a <see cref="short"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static short[] CopyEndianReversed(ReadOnlySpan<short> span)
        {
            int count = span.Length;
            var array = new short[count];
            for (int i = 0; i < count; i++)
                array[i] = BinaryPrimitives.ReverseEndianness(span[i]);
            return array;
        }

        /// <summary>
        /// Copies a <see cref="ushort"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static ushort[] CopyEndianReversed(ReadOnlySpan<ushort> span)
        {
            int count = span.Length;
            var array = new ushort[count];
            for (int i = 0; i < count; i++)
                array[i] = BinaryPrimitives.ReverseEndianness(span[i]);
            return array;
        }

        /// <summary>
        /// Copies an <see cref="int"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static int[] CopyEndianReversed(ReadOnlySpan<int> span)
        {
            int count = span.Length;
            var array = new int[count];
            for (int i = 0; i < count; i++)
                array[i] = BinaryPrimitives.ReverseEndianness(span[i]);
            return array;
        }

        /// <summary>
        /// Copies a <see cref="uint"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static uint[] CopyEndianReversed(ReadOnlySpan<uint> span)
        {
            int count = span.Length;
            var array = new uint[count];
            for (int i = 0; i < count; i++)
                array[i] = BinaryPrimitives.ReverseEndianness(span[i]);
            return array;
        }

        /// <summary>
        /// Copies a <see cref="long"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static long[] CopyEndianReversed(ReadOnlySpan<long> span)
        {
            int count = span.Length;
            var array = new long[count];
            for (int i = 0; i < count; i++)
                array[i] = BinaryPrimitives.ReverseEndianness(span[i]);
            return array;
        }

        /// <summary>
        /// Copies a <see cref="ulong"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static ulong[] CopyEndianReversed(ReadOnlySpan<ulong> span)
        {
            int count = span.Length;
            var array = new ulong[count];
            for (int i = 0; i < count; i++)
                array[i] = BinaryPrimitives.ReverseEndianness(span[i]);
            return array;
        }

        /// <summary>
        /// Copies a <see cref="Half"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static Half[] CopyEndianReversed(ReadOnlySpan<Half> span)
        {
            int count = span.Length;
            var castSpan = MemoryMarshal.Cast<Half, ushort>(span);
            var array = new Half[count];
            var castCopySpan = MemoryMarshal.Cast<Half, ushort>(new Span<Half>(array));
            for (int i = 0; i < count; i++)
                castCopySpan[i] = BinaryPrimitives.ReverseEndianness(castSpan[i]);

            return array;
        }

        /// <summary>
        /// Copies a <see cref="float"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static float[] CopyEndianReversed(ReadOnlySpan<float> span)
        {
            int count = span.Length;
            var castSpan = MemoryMarshal.Cast<float, uint>(span);
            var array = new float[count];
            var castCopySpan = MemoryMarshal.Cast<float, uint>(new Span<float>(array));
            for (int i = 0; i < count; i++)
                castCopySpan[i] = BinaryPrimitives.ReverseEndianness(castSpan[i]);

            return array;
        }

        /// <summary>
        /// Copies a <see cref="double"/> span with reversed endianness.
        /// </summary>
        /// <param name="span">The span to copy with reversed endianness.</param>
        /// <returns>A copy with reversed endianness.</returns>
        public static double[] CopyEndianReversed(ReadOnlySpan<double> span)
        {
            int count = span.Length;
            var castSpan = MemoryMarshal.Cast<double, ulong>(span);
            var array = new double[count];
            var castCopySpan = MemoryMarshal.Cast<double, ulong>(new Span<double>(array));
            for (int i = 0; i < count; i++)
                castCopySpan[i] = BinaryPrimitives.ReverseEndianness(castSpan[i]);

            return array;
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<short> values, Span<short> buffer)
        {
            Span<short> copies = stackalloc short[values.Length];
            fixed (short* copyPtr = copies)
            {
                values.CopyTo(copies);
                for (int i = 0; i < copies.Length; i++)
                {
                    copies[i] = BinaryPrimitives.ReverseEndianness(copies[i]);
                }

                copies.CopyTo(buffer);
            }
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<ushort> values, Span<ushort> buffer)
        {
            Span<ushort> copies = stackalloc ushort[values.Length];
            fixed (ushort* copyPtr = copies)
            {
                values.CopyTo(copies);
                for (int i = 0; i < copies.Length; i++)
                {
                    copies[i] = BinaryPrimitives.ReverseEndianness(copies[i]);
                }

                copies.CopyTo(buffer);
            }
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<int> values, Span<int> buffer)
        {
            Span<int> copies = stackalloc int[values.Length];
            fixed (int* copyPtr = copies)
            {
                values.CopyTo(copies);
                for (int i = 0; i < copies.Length; i++)
                {
                    copies[i] = BinaryPrimitives.ReverseEndianness(copies[i]);
                }

                copies.CopyTo(buffer);
            }
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<uint> values, Span<uint> buffer)
        {
            Span<uint> copies = stackalloc uint[values.Length];
            fixed (uint* copyPtr = copies)
            {
                values.CopyTo(copies);
                for (int i = 0; i < copies.Length; i++)
                {
                    copies[i] = BinaryPrimitives.ReverseEndianness(copies[i]);
                }

                copies.CopyTo(buffer);
            }
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<long> values, Span<long> buffer)
        {
            Span<long> copies = stackalloc long[values.Length];
            fixed (long* copyPtr = copies)
            {
                values.CopyTo(copies);
                for (int i = 0; i < copies.Length; i++)
                {
                    copies[i] = BinaryPrimitives.ReverseEndianness(copies[i]);
                }

                copies.CopyTo(buffer);
            }
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<ulong> values, Span<ulong> buffer)
        {
            Span<ulong> copies = stackalloc ulong[values.Length];
            fixed (ulong* copyPtr = copies)
            {
                values.CopyTo(copies);
                for (int i = 0; i < copies.Length; i++)
                {
                    copies[i] = BinaryPrimitives.ReverseEndianness(copies[i]);
                }

                copies.CopyTo(buffer);
            }
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<Half> values, Span<Half> buffer)
        {
            fixed (Half* valuesPtr = values)
            fixed (Half* bufferPtr = buffer)
            {
                CopyEndianReversedTo(new ReadOnlySpan<ushort>(valuesPtr, values.Length), new Span<ushort>(bufferPtr, buffer.Length));
            }
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<float> values, Span<float> buffer)
        {
            fixed (float* valuesPtr = values)
            fixed (float* bufferPtr = buffer)
            {
                CopyEndianReversedTo(new ReadOnlySpan<uint>(valuesPtr, values.Length), new Span<uint>(bufferPtr, buffer.Length));
            }
        }

        public static unsafe void CopyEndianReversedTo(ReadOnlySpan<double> values, Span<double> buffer)
        {
            fixed (double* valuesPtr = values)
            fixed (double* bufferPtr = buffer)
            {
                CopyEndianReversedTo(new ReadOnlySpan<ulong>(valuesPtr, values.Length), new Span<ulong>(bufferPtr, buffer.Length));
            }
        }
    }
}
