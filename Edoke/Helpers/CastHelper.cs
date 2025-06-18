using System;

namespace Edoke.Helpers
{
    internal static class CastHelper
    {
        public static unsafe ReadOnlySpan<byte> ToByteSpan(ReadOnlySpan<sbyte> values)
        {
            fixed (sbyte* valuesPtr = values)
            {
                return new ReadOnlySpan<byte>(valuesPtr, values.Length);
            }
        }

        public static unsafe Span<short> ToInt16Span(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<short>(valuesPtr, count);
            }
        }

        public static unsafe Span<ushort> ToUInt16Span(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<ushort>(valuesPtr, count);
            }
        }

        public static unsafe Span<int> ToInt32Span(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<int>(valuesPtr, count);
            }
        }

        public static unsafe Span<uint> ToUInt32Span(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<uint>(valuesPtr, count);
            }
        }

        public static unsafe Span<long> ToInt64Span(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<long>(valuesPtr, count);
            }
        }

        public static unsafe Span<ulong> ToUInt64Span(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<ulong>(valuesPtr, count);
            }
        }

        public static unsafe Span<Int128> ToInt128Span(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<Int128>(valuesPtr, count);
            }
        }

        public static unsafe Span<UInt128> ToUInt128Span(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<UInt128>(valuesPtr, count);
            }
        }

        public static unsafe Span<Half> ToHalfSpan(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<Half>(valuesPtr, count);
            }
        }

        public static unsafe Span<float> ToSingleSpan(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<float>(valuesPtr, count);
            }
        }

        public static unsafe Span<double> ToDoubleSpan(Span<byte> values, int count)
        {
            fixed (byte* valuesPtr = values)
            {
                return new Span<double>(valuesPtr, count);
            }
        }
    }
}
