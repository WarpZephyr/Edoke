using Edoke.Helpers;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Edoke.IO
{
    /// <summary>
    /// A binary writer for spans supporting endianness.
    /// </summary>
    public ref struct BinarySpanWriter
    {
        #region Constants

        // Buffer Thresholds below not tested.

        /// <summary>
        /// The threshold to use a stackalloc for pattern writing.
        /// </summary>
        private const int PatternStackThreshold = 128;

        /// <summary>
        /// The minimum threshold to use a buffer for padding.
        /// </summary>
        private const int PadBufferMinThreshold = 128;

        /// <summary>
        /// The maximum threshold to use a buffer for padding.
        /// </summary>
        private const int PadBufferMaxThreshold = 64000;

        /// <summary>
        /// The minimum threshold to use a buffer for vector array writing.
        /// </summary>
        private const int VectorBufferMinThreshold = 32;

        /// <summary>
        /// The maximum threshold to use a buffer for vector array writing.
        /// </summary>
        private const int VectorBufferMaxThreshold = 4096;

        #endregion

        #region Members

        /// <summary>
        /// The underlying span.
        /// </summary>
        private readonly Span<byte> Buffer;

        /// <summary>
        /// The current position of the writer.
        /// </summary>
        private int BufferOffset;

        /// <summary>
        /// Whether or not to write in big endian.
        /// </summary>
        public bool BigEndian { get; set; }

        /// <summary>
        /// The type of varint for writing varints.
        /// </summary>
        public bool VarintLong { get; set; }

        #endregion

        #region Properties

        /// <summary>
        /// Whether or not endianness is reversed.
        /// </summary>
        private readonly bool IsEndiannessReversed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BigEndian != !BitConverter.IsLittleEndian;
        }

        /// <summary>
        /// The current position of the writer.
        /// </summary>
        public int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => BufferOffset;
            set
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)value, (uint)Length, nameof(value));
                BufferOffset = value;
            }
        }

        /// <summary>
        /// The current size of varints in bytes.
        /// </summary>
        public readonly int VarintSize
            => VarintLong ? 8 : 4;

        /// <summary>
        /// The length of the span.
        /// </summary>
        public readonly int Length
            => Buffer.Length;

        /// <summary>
        /// The remaining length of the span from the current position.
        /// </summary>
        public readonly int Remaining
            => Buffer.Length - BufferOffset;

        #endregion

        #region Constructors

        /// <summary>
        /// Create a new <see cref="BinarySpanWriter"/> from a <see cref="Span{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="Span{T}"/> to write to.</param>
        /// <param name="bigEndian">Whether or not to write in big endian.</param>
        public BinarySpanWriter(Span<byte> buffer, bool bigEndian)
        {
            Buffer = buffer;
            BigEndian = bigEndian;
        }

        /// <summary>
        /// Create a new <see cref="BinarySpanWriter"/> from an <see cref="Array"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="Array"/> to write to.</param>
        /// <param name="bigEndian">Whether or not to write in big endian.</param>
        public BinarySpanWriter(byte[] buffer, bool bigEndian) : this(new Span<byte>(buffer), bigEndian) { }

        /// <summary>
        /// Create a new <see cref="BinarySpanWriter"/> from a <see cref="Span{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="Span{T}"/> to write to.</param>
        public BinarySpanWriter(Span<byte> buffer) : this((Span<byte>)buffer, !BitConverter.IsLittleEndian) { }

        /// <summary>
        /// Create a new <see cref="BinarySpanWriter"/> from an <see cref="Array"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="Array"/> to write to.</param>
        public BinarySpanWriter(byte[] buffer) : this(new Span<byte>(buffer), !BitConverter.IsLittleEndian) { }

        #endregion

        #region Validation

        /// <summary>
        /// Validate the specified length argument.
        /// </summary>
        /// <param name="length">The length.</param>
        /// <exception cref="ArgumentOutOfRangeException">The argument was out of range.</exception>
        private readonly void ValidateLength(int length)
        {
            if ((uint)length > (uint)Remaining)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Cannot write beyond the specified span.");
            }
        }

        /// <summary>
        /// Validate the specified position argument.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <exception cref="ArgumentOutOfRangeException">The argument was out of range.</exception>
        private readonly void ValidatePosition(int position)
        {
            if ((uint)position > (uint)BufferOffset)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Cannot write beyond the specified span.");
            }
        }

        /// <summary>
        /// Validate the specified position and length arguments.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="length">The length.</param>
        /// <exception cref="ArgumentOutOfRangeException">An argument was out of range.</exception>
        private readonly void ValidateArguments(int position, int length)
        {
            if ((uint)position > (uint)BufferOffset)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Cannot write beyond the specified span.");
            }

            if ((uint)length > (uint)(Buffer.Length - position))
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Cannot write beyond the specified span.");
            }
        }

        #endregion

        #region Seek

        /// <summary>
        /// Go back the specified count of bytes.
        /// </summary>
        /// <param name="count">The amount to rewind.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rewind(int count)
            => Position -= count;

        /// <summary>
        /// Go forward the specified count of bytes.
        /// </summary>
        /// <param name="count">The amount to skip.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Skip(int count)
            => Position += count;

        /// <summary>
        /// Seek to the specified position based on the start of the span.
        /// </summary>
        /// <param name="position">The position to seek to.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Seek(int position)
            => Position = position;

        /// <summary>
        /// Seek to the specified offset from the specified <see cref="SeekOrigin"/>.
        /// </summary>
        /// <param name="offset">The offset to seek to.</param>
        /// <param name="origin">The origin to seek from.</param>
        /// <exception cref="NotSupportedException">The specified <see cref="SeekOrigin"/> was unknown.</exception>
        public void Seek(int offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length - offset;
                    break;
                default:
                    throw new NotSupportedException($"Unknown {nameof(SeekOrigin)}: {origin}");
            }
        }

        #endregion

        #region Pad

        /// <summary>
        /// Write the specified padding value until the specified alignment relative to the current position is met.
        /// </summary>
        /// <param name="alignment">The specified alignment.</param>
        /// <param name="padding">The padding value to write.</param>
        /// <exception cref="ArgumentOutOfRangeException">The alignment argument was negative or zero.</exception>
        /// <exception cref="InvalidOperationException">The next alignment position was out of range.</exception>
        public void Pad(int alignment, byte padding)
        {
            long remainder = BufferOffset % alignment;
            if (remainder > 0)
            {
                long count = alignment - remainder;
                if (count == 1)
                {
                    WriteByte(padding);
                }
                else if (count >= PadBufferMinThreshold && count <= PadBufferMaxThreshold)
                {
                    WritePattern((int)count, padding);
                }
                else
                {
                    while (count > 0)
                    {
                        WriteByte(padding);
                        count--;
                    }
                }
            }
        }

        /// <summary>
        /// Write the specified padding value until the specified alignment relative to the specified position is met.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="alignment">The specified alignment.</param>
        /// <param name="padding">The padding value to write.</param>
        /// <exception cref="ArgumentOutOfRangeException">An argument was out of range.</exception>
        /// <exception cref="InvalidOperationException">An argument or the next alignment position was out of range.</exception>
        public void PadFrom(int position, int alignment, byte padding)
        {
            long remainder = (BufferOffset - position) % alignment;
            if (remainder > 0)
            {
                long count = alignment - remainder;
                if (count == 1)
                {
                    WriteByte(padding);
                }
                else if (count >= PadBufferMinThreshold && count <= PadBufferMaxThreshold)
                {
                    WritePattern((int)count, padding);
                }
                else
                {
                    while (count > 0)
                    {
                        WriteByte(padding);
                        count--;
                    }
                }
            }
        }

        #endregion

        #region Write

        /// <summary>
        /// Write an unmanaged value.<br/>
        /// Field endianness is not accounted for; Machine endianness is used.
        /// </summary>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        /// <param name="value">The value to write.</param>
        public unsafe void Write<T>(T value) where T : unmanaged
        {
            int pos = BufferOffset;
            Position += sizeof(T);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(Buffer), pos), value);
        }

        /// <summary>
        /// Write a <see cref="ReadOnlySpan{T}"/> of unmanaged values.<br/>
        /// Field endianness is not accounted for; Machine endianness is used.
        /// </summary>
        /// <typeparam name="T">The type of the values to write.</typeparam>
        /// <param name="values">The values to write.</param>
        public unsafe void Write<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            int size = sizeof(T);
            int length = size * values.Length;

            fixed (T* ptr = values)
            {
                WriteByteSpan(new ReadOnlySpan<byte>(ptr, length));
            }
        }

        #endregion

        #region SByte

        /// <summary>
        /// Writes an <see cref="sbyte"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value)
            => WriteByte((byte)value);

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="sbyte"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteSBytes(IList<sbyte> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                WriteSByte(values[i]);
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="sbyte"/>.
        /// </summary>
        /// <param name="sbytes">The values to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByteSpan(ReadOnlySpan<sbyte> values)
            => WriteByteSpan(CastHelper.ToByteSpan(values));

        #endregion

        #region Byte

        /// <summary>
        /// Writes a <see cref="byte"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteByte(byte value)
        {
            int pos = BufferOffset;
            Position += 1;
            Buffer[pos] = value;
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(byte[] values)
            => WriteByteSpan(values);

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteBytes(IList<byte> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                WriteByte(values[i]);
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="bytes">The values to write.</param>
        public void WriteByteSpan(ReadOnlySpan<byte> values)
        {
            int pos = BufferOffset;
            Position += values.Length;
            values.CopyTo(Buffer.Slice(pos, values.Length));
        }

        #endregion

        #region Int16

        /// <summary>
        /// Writes an <see cref="short"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt16(short value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt16s(IList<short> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt16Span(ReadOnlySpan<short> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(short);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToInt16Span(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region UInt16

        /// <summary>
        /// Writes an <see cref="ushort"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt16(ushort value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="ushort"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt16s(IList<ushort> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt16Span(ReadOnlySpan<ushort> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(ushort);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToUInt16Span(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region Int32

        /// <summary>
        /// Writes an <see cref="int"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt32(int value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="int"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt32s(IList<int> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt32Span(ReadOnlySpan<int> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(int);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToInt32Span(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region UInt32

        /// <summary>
        /// Writes an <see cref="uint"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt32(uint value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="uint"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt32s(IList<uint> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt32Span(ReadOnlySpan<uint> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(uint);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToUInt32Span(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region Int64

        /// <summary>
        /// Writes an <see cref="long"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt64(long value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="long"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt64s(IList<long> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt64Span(ReadOnlySpan<long> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(long);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToInt64Span(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region UInt64

        /// <summary>
        /// Writes an <see cref="ulong"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt64(ulong value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="ulong"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt64s(IList<ulong> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt64Span(ReadOnlySpan<ulong> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(ulong);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToUInt64Span(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region Int128

        /// <summary>
        /// Writes an <see cref="Int128"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt128(Int128 value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="Int128"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt128s(IList<Int128> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt128Span(ReadOnlySpan<Int128> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * 16;
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToInt128Span(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region UInt128

        /// <summary>
        /// Writes an <see cref="uInt128"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt128(UInt128 value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="uInt128"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt128s(IList<UInt128> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt128Span(ReadOnlySpan<UInt128> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * 16;
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToUInt128Span(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region Half

        /// <summary>
        /// Writes an <see cref="Half"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteHalf(Half value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.HalfToUInt16Bits(value)));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="Half"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteHalfs(IList<Half> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.HalfToUInt16Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteHalfSpan(ReadOnlySpan<Half> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(ushort);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToHalfSpan(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region Single

        /// <summary>
        /// Writes an <see cref="float"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteSingle(float value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value)));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="float"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteSingles(IList<float> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteSingleSpan(ReadOnlySpan<float> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(uint);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToSingleSpan(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region Double

        /// <summary>
        /// Writes an <see cref="double"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteDouble(double value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToUInt64Bits(value)));
            }
            else
            {
                Write(value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="double"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteDoubles(IList<double> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToUInt64Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteDoubleSpan(ReadOnlySpan<double> values)
        {
            if (IsEndiannessReversed)
            {
                int pos = BufferOffset;
                Position += values.Length * sizeof(ulong);
                EndianHelper.CopyEndianReversedTo(values, CastHelper.ToDoubleSpan(Buffer[pos..], values.Length));
            }
            else
            {
                Write(values);
            }
        }

        #endregion

        #region Varint

        /// <summary>
        /// Writes a varint according to <see cref="VarintLong"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteVarint(long value)
        {
            if (VarintLong)
            {
                WriteInt64(value);
            }
            else
            {
                WriteInt32((int)value);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of varints according to <see cref="VarintLong"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteVarints(IList<long> values)
        {
            if (VarintLong)
            {
                WriteInt64s(values);
            }
            else
            {
                if (IsEndiannessReversed)
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        Write(BinaryPrimitives.ReverseEndianness((uint)values[i]));
                    }
                }
                else
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        Write((int)values[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of varints according to <see cref="VarintLong"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteVarintSpan(ReadOnlySpan<long> values)
        {
            if (VarintLong)
            {
                WriteInt64Span(values);
            }
            else
            {
                if (IsEndiannessReversed)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        Write(BinaryPrimitives.ReverseEndianness((uint)values[i]));
                    }
                }
                else
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        Write((int)values[i]);
                    }
                }
            }
        }

        #endregion

        #region Boolean

        /// <summary>
        /// Writes a <see cref="bool"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBoolean(bool value)
            => WriteByte((byte)(value ? 1 : 0));

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="bool"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteBooleans(IList<bool> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                WriteByte((byte)(values[i] ? 1 : 0));
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="bool"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteBooleanSpan(ReadOnlySpan<bool> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                WriteByte((byte)(values[i] ? 1 : 0));
            }
        }

        #endregion

        #region Vector2

        /// <summary>
        /// Writes a <see cref="Vector2"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteVector2(Vector2 value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.X)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Y)));
            }
            else
            {
                Write(value.X);
                Write(value.Y);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="Vector2"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteVector2s(IList<Vector2> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i].X);
                    Write(values[i].Y);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="Vector2"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteVector2s(Vector2[] values)
            => WriteVector2Span(values);

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector2"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public unsafe void WriteVector2Span(ReadOnlySpan<Vector2> values)
        {
            if (IsEndiannessReversed)
            {
                if (values.Length >= VectorBufferMinThreshold && values.Length <= VectorBufferMaxThreshold)
                {
                    int byteLen = values.Length << 3;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(byteLen);
                    try
                    {
                        fixed (Vector2* pf = &values[0])
                        fixed (byte* pb = &buffer[0])
                        {
                            var uintSpan = new Span<uint>(pf, values.Length << 1);
                            var uintCopySpan = new Span<uint>(pb, byteLen);
                            uintSpan.CopyTo(uintCopySpan);
                            for (int i = 0; i < uintSpan.Length; i++)
                            {
                                uintCopySpan[i] = BinaryPrimitives.ReverseEndianness(uintSpan[i]);
                            }

                            WriteByteSpan(buffer);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                else
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                    }
                }
            }
            else
            {
                fixed (Vector2* pf = &values[0])
                {
                    var byteSpan = new Span<byte>(pf, values.Length << 3);
                    WriteByteSpan(byteSpan);
                }
            }
        }

        #endregion

        #region Vector3

        /// <summary>
        /// Writes a <see cref="Vector3"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteVector3(Vector3 value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.X)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Y)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Z)));
            }
            else
            {
                Write(value.X);
                Write(value.Y);
                Write(value.Z);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="Vector3"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteVector3s(IList<Vector3> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i].X);
                    Write(values[i].Y);
                    Write(values[i].Z);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="Vector3"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteVector3s(Vector3[] values)
            => WriteVector3Span(values);

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector3"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public unsafe void WriteVector3Span(ReadOnlySpan<Vector3> values)
        {
            if (IsEndiannessReversed)
            {
                if (values.Length >= VectorBufferMinThreshold && values.Length <= VectorBufferMaxThreshold)
                {
                    int byteLen = values.Length / 12;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(byteLen);
                    try
                    {
                        fixed (Vector3* pf = &values[0])
                        fixed (byte* pb = &buffer[0])
                        {
                            var uintSpan = new Span<uint>(pf, values.Length / 3);
                            var uintCopySpan = new Span<uint>(pb, byteLen);
                            uintSpan.CopyTo(uintCopySpan);
                            for (int i = 0; i < uintSpan.Length; i++)
                            {
                                uintCopySpan[i] = BinaryPrimitives.ReverseEndianness(uintSpan[i]);
                            }

                            WriteByteSpan(buffer);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                else
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                    }
                }
            }
            else
            {
                fixed (Vector3* pf = &values[0])
                {
                    var byteSpan = new Span<byte>(pf, values.Length / 12);
                    WriteByteSpan(byteSpan);
                }
            }
        }

        #endregion

        #region Vector4

        /// <summary>
        /// Writes a <see cref="Vector4"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteVector4(Vector4 value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.X)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Y)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Z)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.W)));
            }
            else
            {
                Write(value.X);
                Write(value.Y);
                Write(value.Z);
                Write(value.W);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="Vector4"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteVector4s(IList<Vector4> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i].X);
                    Write(values[i].Y);
                    Write(values[i].Z);
                    Write(values[i].W);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="Vector4"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteVector4s(Vector4[] values)
            => WriteVector4Span(values);

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector4"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public unsafe void WriteVector4Span(ReadOnlySpan<Vector4> values)
        {
            if (IsEndiannessReversed)
            {
                if (values.Length >= VectorBufferMinThreshold && values.Length <= VectorBufferMaxThreshold)
                {
                    int byteLen = values.Length << 4;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(byteLen);
                    try
                    {
                        fixed (Vector4* pf = &values[0])
                        fixed (byte* pb = &buffer[0])
                        {
                            var uintSpan = new Span<uint>(pf, values.Length << 2);
                            var uintCopySpan = new Span<uint>(pb, byteLen);
                            uintSpan.CopyTo(uintCopySpan);
                            for (int i = 0; i < uintSpan.Length; i++)
                            {
                                uintCopySpan[i] = BinaryPrimitives.ReverseEndianness(uintSpan[i]);
                            }

                            WriteByteSpan(buffer);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                else
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
                    }
                }
            }
            else
            {
                fixed (Vector4* pf = &values[0])
                {
                    var byteSpan = new Span<byte>(pf, values.Length << 4);
                    WriteByteSpan(byteSpan);
                }
            }
        }

        #endregion

        #region Quaternion

        /// <summary>
        /// Writes a <see cref="Quaternion"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteQuaternion(Quaternion value)
        {
            if (IsEndiannessReversed)
            {
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.X)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Y)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Z)));
                Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.W)));
            }
            else
            {
                Write(value.X);
                Write(value.Y);
                Write(value.Z);
                Write(value.W);
            }
        }

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="Quaternion"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteQuaternions(IList<Quaternion> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                    Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Write(values[i].X);
                    Write(values[i].Y);
                    Write(values[i].Z);
                    Write(values[i].W);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="Quaternion"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteQuaternions(Quaternion[] values)
            => WriteQuaternionSpan(values);

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="Quaternion"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public unsafe void WriteQuaternionSpan(ReadOnlySpan<Quaternion> values)
        {
            if (IsEndiannessReversed)
            {
                if (values.Length >= VectorBufferMinThreshold && values.Length <= VectorBufferMaxThreshold)
                {
                    int byteLen = values.Length << 4;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(byteLen);
                    try
                    {
                        fixed (Quaternion* pf = &values[0])
                        fixed (byte* pb = &buffer[0])
                        {
                            var uintSpan = new Span<uint>(pf, values.Length << 2);
                            var uintCopySpan = new Span<uint>(pb, byteLen);
                            uintSpan.CopyTo(uintCopySpan);
                            for (int i = 0; i < uintSpan.Length; i++)
                            {
                                uintCopySpan[i] = BinaryPrimitives.ReverseEndianness(uintSpan[i]);
                            }

                            WriteByteSpan(buffer);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                else
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                        Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
                    }
                }
            }
            else
            {
                fixed (Quaternion* pf = &values[0])
                {
                    var byteSpan = new Span<byte>(pf, values.Length << 4);
                    WriteByteSpan(byteSpan);
                }
            }
        }

        #endregion

        #region Color

        /// <summary>
        /// Writes a <see cref="Color"/> in RGBA order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteRgba(Color color)
        {
            Write(color.R);
            Write(color.G);
            Write(color.B);
            Write(color.A);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in ARGB order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteArgb(Color color)
        {
            Write(color.A);
            Write(color.R);
            Write(color.G);
            Write(color.B);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in BGRA order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteBgra(Color color)
        {
            Write(color.B);
            Write(color.G);
            Write(color.R);
            Write(color.A);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in ABGR order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteAbgr(Color color)
        {
            Write(color.A);
            Write(color.B);
            Write(color.G);
            Write(color.R);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in RGB order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteRgb(Color color)
        {
            Write(color.R);
            Write(color.G);
            Write(color.B);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in BGR order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteBgr(Color color)
        {
            Write(color.B);
            Write(color.G);
            Write(color.R);
        }

        #endregion

        #region Pattern

        /// <summary>
        /// Writes the specified <see cref="byte"/> value the specified number of times.
        /// </summary>
        /// <param name="length">The length of to write.</param>
        /// <param name="value">The value to write.</param>
        public void WritePattern(int length, byte value)
        {
            if (length <= PatternStackThreshold)
            {
                Span<byte> buffer = stackalloc byte[length];
                if (value != 0)
                {
                    for (int i = 0; i < length; i++)
                    {
                        buffer[i] = value;
                    }
                }

                int pos = BufferOffset;
                Position += length;
                buffer.CopyTo(Buffer.Slice(pos, length));
                return;
            }

            byte[] rental = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                for (int i = 0; i < length; i++)
                {
                    rental[i] = value;
                }

                WriteByteSpan(rental.AsSpan()[..length]);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rental);
            }
        }

        #endregion

        #region String

        /// <summary>
        /// Write a <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="encoding">The encoding to use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteString(string value, Encoding encoding)
            => WriteByteSpan(encoding.GetBytes(value));

        /// <summary>
        /// Write an optionally null-terminated 8-Bit <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        private void Write8BitString(string value, Encoding encoding, bool terminate)
        {
            WriteByteSpan(encoding.GetBytes(value));
            if (terminate)
            {
                WriteByte(0);
            }
        }

        /// <summary>
        /// Write an optionally null-terminated 16-Bit <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        private void Write16BitString(string value, Encoding encoding, bool terminate)
        {
            WriteByteSpan(encoding.GetBytes(value));
            if (terminate)
            {
                WriteByte(0);
                WriteByte(0);
            }
        }

        /// <summary>
        /// Write an optionally null-terminated 8-bit <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        private void Write8BitFixedString(string value, Encoding encoding, int length, byte padding, bool terminate)
        {
            Span<byte> fixstr = stackalloc byte[length];
            for (int i = 0; i < length; i++)
                fixstr[i] = padding;

            int encodedCount = encoding.GetBytes(value, fixstr);
            if (terminate && encodedCount < length)
                fixstr[encodedCount] = 0;

            int pos = BufferOffset;
            Position += fixstr.Length;
            fixstr.CopyTo(Buffer.Slice(pos, length));
        }

        /// <summary>
        /// Write an optionally null-terminated 16-bit <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        private void Write16BitFixedString(string value, Encoding encoding, int length, byte padding, bool terminate)
        {
            Span<byte> fixstr = stackalloc byte[length];
            for (int i = 0; i < length; i++)
                fixstr[i] = padding;

            int encodedCount = encoding.GetBytes(value, fixstr);
            if (terminate && encodedCount < (length - 1))
            {
                fixstr[encodedCount] = 0;
                fixstr[encodedCount + 1] = 0;
            }

            int pos = BufferOffset;
            Position += fixstr.Length;
            fixstr.CopyTo(Buffer.Slice(pos, length));
        }

        #endregion

        #region String ASCII

        /// <summary>
        /// Writes an ASCII encoded <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteASCII(string value, bool terminate = false)
            => Write8BitString(value, Encoding.ASCII, terminate);

        /// <summary>
        /// Writes an ASCII encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="padding">The padding value to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteASCII(string value, int length, byte padding = 0, bool terminate = false)
            => Write8BitFixedString(value, Encoding.ASCII, length, padding, terminate);

        #endregion

        #region String UTF8

        /// <summary>
        /// Writes a UTF8 encoded <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF8(string value, bool terminate = false)
            => Write8BitString(value, Encoding.UTF8, terminate);

        /// <summary>
        /// Writes a UTF8 encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="padding">The padding value to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF8(string value, int length, byte padding = 0, bool terminate = false)
            => Write8BitFixedString(value, Encoding.UTF8, length, padding, terminate);

        #endregion

        #region String ShiftJIS

        /// <summary>
        /// Writes a ShiftJIS encoded <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteShiftJIS(string value, bool terminate = false)
            => Write8BitString(value, EncodingHelper.ShiftJIS, terminate);

        /// <summary>
        /// Writes a ShiftJIS encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="padding">The padding value to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteShiftJIS(string value, int length, byte padding = 0, bool terminate = false)
            => Write8BitFixedString(value, EncodingHelper.ShiftJIS, length, padding, terminate);

        #endregion

        #region String UTF16

        /// <summary>
        /// Writes a UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16(string value, bool terminate = false)
            => Write16BitString(value, BigEndian ? EncodingHelper.UTF16BE : EncodingHelper.UTF16LE, terminate);

        /// <summary>
        /// Writes a UTF16 encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="padding">The padding value to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16(string value, int length, byte padding = 0, bool terminate = false)
            => Write16BitFixedString(value, BigEndian ? EncodingHelper.UTF16BE : EncodingHelper.UTF16LE, length, padding, terminate);

        #endregion

        #region String UTF16 Big Endian

        /// <summary>
        /// Writes a UTF16 big-endian encoded <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16BigEndian(string value, bool terminate = false)
            => Write16BitString(value, EncodingHelper.UTF16BE, terminate);

        /// <summary>
        /// Writes a UTF16 big-endian encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="padding">The padding value to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16BigEndian(string value, int length, byte padding = 0, bool terminate = false)
            => Write16BitFixedString(value, EncodingHelper.UTF16BE, length, padding, terminate);

        #endregion

        #region String UTF16 Little Endian

        /// <summary>
        /// Writes a UTF16 little-endian encoded <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16LittleEndian(string value, bool terminate = false)
            => Write16BitString(value, EncodingHelper.UTF16LE, terminate);

        /// <summary>
        /// Writes a UTF16 little-endian encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="padding">The padding value to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16LittleEndian(string value, int length, byte padding = 0, bool terminate = false)
            => Write16BitFixedString(value, EncodingHelper.UTF16LE, length, padding, terminate);

        #endregion
    }
}
