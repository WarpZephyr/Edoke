using System;
using System.Buffers.Binary;
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
    }
}
