using Edoke.Helpers;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Edoke.IO
{
    /// <summary>
    /// A binary writer for streams supporting endianness.
    /// </summary>
    public class BinaryStreamWriter : IDisposable
    {
        #region Constants

        /// <summary>
        /// The pattern value for reservations.
        /// </summary>
        private const byte ReservationPattern = 0xFE;

        /// <summary>
        /// The type name for 4-Byte varints.
        /// </summary>
        private const string VarintIntTypeName = "Varint32";

        /// <summary>
        /// The type name for 8-Byte varints.
        /// </summary>
        private const string VarintLongTypeName = "Varint64";

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
        /// The underlying <see cref="Stream"/>.
        /// </summary>
        private readonly Stream InternalStream;

        /// <summary>
        /// The underlying <see cref="BinaryWriter"/> assisting in writing.
        /// </summary>
        private readonly BinaryWriter Writer;

        /// <summary>
        /// A jump <see cref="Stack{T}"/> for step-ins.
        /// </summary>
        private readonly Stack<long> Steps;

        /// <summary>
        /// A jump <see cref="Dictionary{TKey, TValue}"/> for recording and filling reservations.
        /// </summary>
        private readonly Dictionary<string, long> Reservations;

        /// <summary>
        /// Whether or not endianness is reversed.
        /// </summary>
        private bool IsEndiannessReversed;

        /// <summary>
        /// The backing field for <see cref="BigEndian"/>.
        /// </summary>
        private bool BigEndianField;

        /// <summary>
        /// Whether or not this <see cref="BinaryStreamWriter"/> has been disposed.
        /// </summary>
        private bool disposedValue;

        /// <summary>
        /// The backing field for <see cref="VarintLong"/>.
        /// </summary>
        private bool VarintLongField;

        /// <summary>
        /// The current size of varints.
        /// </summary>
        public int VarintSize { get; private set; }

        #endregion

        #region Properties

        /// <summary>
        /// The underlying <see cref="Stream"/>.
        /// </summary>
        public Stream BaseStream
            => InternalStream;

        /// <summary>
        /// Whether or not to read in big endian.
        /// </summary>
        public bool BigEndian
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BigEndianField;
            set
            {
                IsEndiannessReversed = BigEndian != !BitConverter.IsLittleEndian;
                BigEndianField = value;
            }
        }

        /// <summary>
        /// The current position of the writer.
        /// </summary>
        public long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => InternalStream.Position;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => InternalStream.Position = value;
        }

        /// <summary>
        /// The type of varint for reading varints.
        /// </summary>
        public bool VarintLong
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VarintLongField;
            set
            {
                VarintSize = value ? 8 : 4;
                VarintLongField = value;
            }
        }

        /// <summary>
        /// The length of the underlying <see cref="Stream"/>.
        /// </summary>
        public long Length
            => InternalStream.Length;

        /// <summary>
        /// The remaining length of the underlying <see cref="Stream"/> from the current position.
        /// </summary>
        public long Remaining
            => InternalStream.Length - InternalStream.Position;

        /// <summary>
        /// Whether or not this <see cref="BinaryStreamReader"/> has been disposed.
        /// </summary>
        public bool IsDisposed
            => disposedValue;

        /// <summary>
        /// The depth of steps on the writer.
        /// </summary>
        public int StepDepth
            => Steps.Count;

        /// <summary>
        /// The number of reservations on the writer.
        /// </summary>
        public int ReservationCount
            => Reservations.Count;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new <see cref="BinaryStreamWriter"/> from the specified options.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        /// <param name="bigEndian">Whether or not to write in big endian.</param>
        /// <param name="leaveOpen">Whether or not to leave the <see cref="Stream"/> open when disposing.</param>
        public BinaryStreamWriter(Stream stream, bool bigEndian, bool leaveOpen)
        {
            InternalStream = stream;
            BigEndian = bigEndian;
            Steps = [];
            Reservations = [];
            Writer = new BinaryWriter(stream, Encoding.Default, leaveOpen);
        }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamWriter"/> from the specified options.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        /// <param name="bigEndian">Whether or not to write in big endian.</param>
        public BinaryStreamWriter(Stream stream, bool bigEndian) : this(stream, bigEndian, false) { }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamWriter"/> writing to the specified <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        public BinaryStreamWriter(Stream stream) : this(stream, !BitConverter.IsLittleEndian, false) { }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamWriter"/> writing to a new <see cref="MemoryStream"/>.
        /// </summary>
        /// <param name="bigEndian">Whether or not to write in big endian.</param>
        public BinaryStreamWriter(bool bigEndian) : this(new MemoryStream(), bigEndian) { }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamWriter"/> writing to a new <see cref="MemoryStream"/>.
        /// </summary>
        public BinaryStreamWriter() : this(new MemoryStream(), !BitConverter.IsLittleEndian) { }

        #endregion

        #region Finish

        /// <summary>
        /// Verify that all reservations are filled and close the <see cref="Stream"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Finish()
            => Dispose();

        /// <summary>
        /// Verify that all reservations are filled, close the <see cref="Stream"/>, and return the written data as a <see cref="byte"/> <see cref="Array"/>.
        /// </summary>
        public byte[] FinishBytes()
        {
            byte[] result = ((MemoryStream)InternalStream).ToArray();
            Dispose();
            return result;
        }

        #endregion

        #region Step

        /// <summary>
        /// Store the current position of the <see cref="Stream"/> on a stack, then move to the specified offset.
        /// </summary>
        public void StepIn(long offset)
        {
            Steps.Push(offset);
            InternalStream.Position = offset;
        }

        /// <summary>
        /// Restore the previous position of the <see cref="Stream"/> from a stack.
        /// </summary>
        public void StepOut()
        {
            if (Steps.Count == 0)
            {
                throw new InvalidOperationException("Writer is already stepped all the way out.");
            }

            InternalStream.Position = Steps.Pop();
        }

        #endregion

        #region Seek

        /// <summary>
        /// Go back the specified count of bytes.
        /// </summary>
        /// <param name="count">The amount to rewind.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rewind(long count)
            => Position -= count;

        /// <summary>
        /// Go forward the specified count of bytes.
        /// </summary>
        /// <param name="count">The amount to skip.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Skip(long count)
            => Position += count;

        /// <summary>
        /// Seek to the specified position based on the start of the underlying <see cref="Stream"/>.
        /// </summary>
        /// <param name="position">The position to seek to.</param>
        public void Seek(long position)
            => InternalStream.Seek(position, SeekOrigin.Begin);

        /// <summary>
        /// Seek to the specified offset from the specified <see cref="SeekOrigin"/>.
        /// </summary>
        /// <param name="offset">The offset to seek to.</param>
        /// <param name="origin">The origin to seek from.</param>
        /// <exception cref="NotSupportedException">The specified <see cref="SeekOrigin"/> was unknown.</exception>
        public void Seek(long offset, SeekOrigin origin)
            => InternalStream.Seek(offset, origin);

        #endregion

        #region Pad

        /// <summary>
        /// Writes the specified <see cref="byte"/> value until the stream position meets the specified alignment.
        /// </summary>
        public void Pad(int alignment, byte value)
        {
            long remainder = InternalStream.Position % alignment;
            if (remainder > 0)
            {
                long count = alignment - remainder;
                if (count == 1)
                {
                    InternalStream.WriteByte(value);
                }
                else if (count >= PadBufferMinThreshold && count <= PadBufferMaxThreshold)
                {
                    WritePattern((int)count, value);
                }
                else
                {
                    while (count > 0)
                    {
                        InternalStream.WriteByte(value);
                        count--;
                    }
                }
            }
        }

        /// <summary>
        /// Writes the specified <see cref="byte"/> value until the stream position meets the specified alignment relative to the given offset.
        /// </summary>
        public void PadFrom(long offset, int alignment, byte value)
        {
            long remainder = (InternalStream.Position - offset) % alignment;
            if (remainder > 0)
            {
                long count = alignment - remainder;
                if (count == 1)
                {
                    InternalStream.WriteByte(value);
                }
                else if (count >= PadBufferMinThreshold && count <= PadBufferMaxThreshold)
                {
                    WritePattern((int)count, value);
                }
                else
                {
                    while (count > 0)
                    {
                        InternalStream.WriteByte(value);
                        count--;
                    }
                }
            }
        }

        #endregion

        #region Reservation

        /// <summary>
        /// Creates a reservation of the specified length using the specified name and type name.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="typeName">The name of the type of reservation.</param>
        /// <param name="length">The length of the reservation.</param>
        /// <exception cref="InvalidOperationException">The reservation already exists.</exception>
        private void Reserve(string name, string typeName, int length)
        {
            string key = $"{name}:{typeName}";
            if (!Reservations.TryAdd(key, Position))
            {
                throw new InvalidOperationException($"Reservation already exists: {name}");
            }

            for (int i = 0; i < length; i++)
            {
                InternalStream.WriteByte(ReservationPattern);
            }
        }

        /// <summary>
        /// Marks a reservation of the specified name and type name as filled and returns it's position.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="typeName">The name of the type of reservation.</param>
        /// <returns>The position of the reservation.</returns>
        /// <exception cref="InvalidOperationException">The reservation doesn't exist.</exception>
        private long Fill(string name, string typeName)
        {
            string key = $"{name}:{typeName}";
            if (!Reservations.Remove(key, out long position))
            {
                throw new InvalidOperationException($"Reservation doesn't exist: {name}");
            }

            return position;
        }

        #endregion

        #region SByte

        /// <summary>
        /// Writes a <see cref="sbyte"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value)
            => InternalStream.WriteByte((byte)value);

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="sbyte"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteSBytes(IList<sbyte> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                InternalStream.WriteByte((byte)values[i]);
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="sbyte"/>.
        /// </summary>
        /// <param name="sbytes">The values to write.</param>
        public void WriteSByteSpan(ReadOnlySpan<sbyte> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                InternalStream.WriteByte((byte)values[i]);
            }
        }

        /// <summary>
        /// Reserves a <see cref="sbyte"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveSByte(string name)
            => Reserve(name, nameof(SByte), sizeof(sbyte));

        /// <summary>
        /// Fills a <see cref="sbyte"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillSByte(string name, sbyte value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(SByte));
            InternalStream.WriteByte((byte)value);
            Position = origPos;
        }

        #endregion

        #region Byte

        /// <summary>
        /// Writes a <see cref="byte"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
            => InternalStream.WriteByte(value);

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(byte[] values)
            => InternalStream.Write(values, 0, values.Length);

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteBytes(IList<byte> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                InternalStream.WriteByte(values[i]);
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="bytes">The values to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByteSpan(ReadOnlySpan<byte> values)
            => InternalStream.Write(values);

        /// <summary>
        /// Reserves a <see cref="byte"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveByte(string name)
            => Reserve(name, nameof(Byte), sizeof(byte));

        /// <summary>
        /// Fills a <see cref="byte"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillByte(string name, byte value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(Byte));
            InternalStream.WriteByte(value);
            Position = origPos;
        }

        #endregion

        #region Int16

        /// <summary>
        /// Writes a <see cref="short"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt16(short value)
        {
            if (IsEndiannessReversed)
            {
                Writer.Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
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
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves a <see cref="short"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveInt16(string name)
            => Reserve(name, nameof(Int16), sizeof(short));

        /// <summary>
        /// Fills a <see cref="short"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillInt16(string name, short value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(Int16));
            WriteInt16(value);
            Position = origPos;
        }

        #endregion

        #region UInt16

        /// <summary>
        /// Writes a <see cref="ushort"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt16(ushort value)
        {
            if (IsEndiannessReversed)
            {
                Writer.Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="ushort"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt16Span(ReadOnlySpan<ushort> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves a <see cref="ushort"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveUInt16(string name)
            => Reserve(name, nameof(UInt16), sizeof(ushort));

        /// <summary>
        /// Fills a <see cref="ushort"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillUInt16(string name, ushort value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(UInt16));
            WriteUInt16(value);
            Position = origPos;
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
                Writer.Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="int"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt32Span(ReadOnlySpan<int> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves an <see cref="int"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveInt32(string name)
            => Reserve(name, nameof(Int32), sizeof(int));

        /// <summary>
        /// Fills an <see cref="int"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillInt32(string name, int value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(Int32));
            WriteInt32(value);
            Position = origPos;
        }

        #endregion

        #region UInt32

        /// <summary>
        /// Writes a <see cref="uint"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt32(uint value)
        {
            if (IsEndiannessReversed)
            {
                Writer.Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="uint"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt32Span(ReadOnlySpan<uint> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves a <see cref="uint"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveUInt32(string name)
            => Reserve(name, nameof(UInt32), sizeof(uint));

        /// <summary>
        /// Fills a <see cref="uint"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillUInt32(string name, uint value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(UInt32));
            WriteUInt32(value);
            Position = origPos;
        }

        #endregion

        #region Int64

        /// <summary>
        /// Writes a <see cref="long"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt64(long value)
        {
            if (IsEndiannessReversed)
            {
                Writer.Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="long"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteInt64Span(ReadOnlySpan<long> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves a <see cref="long"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveInt64(string name)
            => Reserve(name, nameof(Int64), sizeof(long));

        /// <summary>
        /// Fills a <see cref="long"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillInt64(string name, long value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(Int64));
            WriteInt64(value);
            Position = origPos;
        }

        #endregion

        #region UInt64

        /// <summary>
        /// Writes a <see cref="ulong"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt64(ulong value)
        {
            if (IsEndiannessReversed)
            {
                Writer.Write(BinaryPrimitives.ReverseEndianness(value));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="ulong"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteUInt64Span(ReadOnlySpan<ulong> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(values[i]));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves a <see cref="ulong"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveUInt64(string name)
            => Reserve(name, nameof(UInt64), sizeof(ulong));

        /// <summary>
        /// Fills a <see cref="ulong"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillUInt64(string name, ulong value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(UInt64));
            WriteUInt64(value);
            Position = origPos;
        }

        #endregion

        #region Half

        /// <summary>
        /// Writes a <see cref="Half"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteHalf(Half value)
        {
            if (IsEndiannessReversed)
            {
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.HalfToUInt16Bits(value)));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.HalfToUInt16Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="Half"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteHalfSpan(ReadOnlySpan<Half> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.HalfToUInt16Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves a <see cref="Half"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveHalf(string name)
            => Reserve(name, nameof(Half), sizeof(ushort));

        /// <summary>
        /// Fills a <see cref="Half"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillHalf(string name, Half value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(Half));
            WriteHalf(value);
            Position = origPos;
        }

        #endregion

        #region Single

        /// <summary>
        /// Writes a <see cref="float"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteSingle(float value)
        {
            if (IsEndiannessReversed)
            {
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value)));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="float"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteSingleSpan(ReadOnlySpan<float> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves a <see cref="float"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveSingle(string name)
            => Reserve(name, nameof(Single), sizeof(float));

        /// <summary>
        /// Fills a <see cref="float"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillSingle(string name, float value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(Single));
            WriteSingle(value);
            Position = origPos;
        }

        #endregion

        #region Double

        /// <summary>
        /// Writes a <see cref="double"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteDouble(double value)
        {
            if (IsEndiannessReversed)
            {
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToUInt64Bits(value)));
            }
            else
            {
                Writer.Write(value);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToUInt64Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="double"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteDoubles(double[] values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToUInt64Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Writes a <see cref="ReadOnlySpan{T}"/> of <see cref="double"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteDoubleSpan(ReadOnlySpan<double> values)
        {
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToUInt64Bits(values[i])));
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Writer.Write(values[i]);
                }
            }
        }

        /// <summary>
        /// Reserves a <see cref="double"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveDouble(string name)
            => Reserve(name, nameof(Double), sizeof(double));

        /// <summary>
        /// Fills a <see cref="double"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillDouble(string name, double value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(Double));
            WriteDouble(value);
            Position = origPos;
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness((uint)values[i]));
                    }
                }
                else
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        Writer.Write((int)values[i]);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness((uint)values[i]));
                    }
                }
                else
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        Writer.Write((int)values[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Reserves a varint according to <see cref="VarintLong"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveVarint(string name)
        {
            if (VarintLong)
            {
                Reserve(name, VarintLongTypeName, sizeof(long));
            }
            else
            {
                Reserve(name, VarintIntTypeName, sizeof(int));
            }
        }

        /// <summary>
        /// Fills a varint reservation according to <see cref="VarintLong"/>.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillVarint(string name, long value)
        {
            long origPos = Position;
            if (VarintLong)
            {
                Position = Fill(name, VarintLongTypeName);
                WriteInt64(value);
            }
            else
            {
                Position = Fill(name, VarintIntTypeName);
                WriteInt32((int)value);
            }

            Position = origPos;
        }

        #endregion

        #region Boolean

        /// <summary>
        /// Writes a <see cref="bool"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBoolean(bool value)
            => InternalStream.WriteByte((byte)(value ? 1 : 0));

        /// <summary>
        /// Writes an <see cref="IList{T}"/> of <see cref="bool"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public void WriteBooleans(IList<bool> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                InternalStream.WriteByte((byte)(values[i] ? 1 : 0));
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
                InternalStream.WriteByte((byte)(values[i] ? 1 : 0));
            }
        }

        /// <summary>
        /// Reserves a <see cref="bool"/> to fill at a later time.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        public void ReserveBoolean(string name)
            => Reserve(name, nameof(Boolean), sizeof(short));

        /// <summary>
        /// Fills a <see cref="bool"/> reservation.
        /// </summary>
        /// <param name="name">The name of the reservation.</param>
        /// <param name="value">The fill value.</param>
        public void FillBoolean(string name, bool value)
        {
            long origPos = Position;
            Position = Fill(name, nameof(Boolean));
            WriteBoolean(value);
            Position = origPos;
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
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.X)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Y)));
            }
            else
            {
                Writer.Write(value.X);
                Writer.Write(value.Y);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i].X);
                    Writer.Write(values[i].Y);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="Vector2"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public unsafe void WriteVector2s(Vector2[] values)
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

                            InternalStream.Write(buffer, 0, buffer.Length);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
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

                            InternalStream.Write(buffer, 0, buffer.Length);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
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
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.X)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Y)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Z)));
            }
            else
            {
                Writer.Write(value.X);
                Writer.Write(value.Y);
                Writer.Write(value.Z);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i].X);
                    Writer.Write(values[i].Y);
                    Writer.Write(values[i].Z);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="Vector3"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public unsafe void WriteVector3s(Vector3[] values)
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

                            InternalStream.Write(buffer, 0, buffer.Length);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
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

                            InternalStream.Write(buffer, 0, buffer.Length);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
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
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.X)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Y)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Z)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.W)));
            }
            else
            {
                Writer.Write(value.X);
                Writer.Write(value.Y);
                Writer.Write(value.Z);
                Writer.Write(value.W);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i].X);
                    Writer.Write(values[i].Y);
                    Writer.Write(values[i].Z);
                    Writer.Write(values[i].W);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="Vector4"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public unsafe void WriteVector4s(Vector4[] values)
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

                            InternalStream.Write(buffer, 0, buffer.Length);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
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

                            InternalStream.Write(buffer, 0, buffer.Length);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
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
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.X)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Y)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.Z)));
                Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(value.W)));
            }
            else
            {
                Writer.Write(value.X);
                Writer.Write(value.Y);
                Writer.Write(value.Z);
                Writer.Write(value.W);
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
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                    Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
                }
            }
            else
            {
                for (int i = 0; i < values.Count; i++)
                {
                    Writer.Write(values[i].X);
                    Writer.Write(values[i].Y);
                    Writer.Write(values[i].Z);
                    Writer.Write(values[i].W);
                }
            }
        }

        /// <summary>
        /// Writes an <see cref="Array"/> of <see cref="Quaternion"/>.
        /// </summary>
        /// <param name="values">The values to write.</param>
        public unsafe void WriteQuaternions(Quaternion[] values)
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

                            InternalStream.Write(buffer, 0, buffer.Length);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
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

                            InternalStream.Write(buffer, 0, buffer.Length);
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
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].X)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Y)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].Z)));
                        Writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToUInt32Bits(values[i].W)));
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
            Writer.Write(color.R);
            Writer.Write(color.G);
            Writer.Write(color.B);
            Writer.Write(color.A);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in ARGB order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteArgb(Color color)
        {
            Writer.Write(color.A);
            Writer.Write(color.R);
            Writer.Write(color.G);
            Writer.Write(color.B);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in BGRA order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteBgra(Color color)
        {
            Writer.Write(color.B);
            Writer.Write(color.G);
            Writer.Write(color.R);
            Writer.Write(color.A);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in ABGR order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteAbgr(Color color)
        {
            Writer.Write(color.A);
            Writer.Write(color.B);
            Writer.Write(color.G);
            Writer.Write(color.R);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in RGB order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteRgb(Color color)
        {
            Writer.Write(color.R);
            Writer.Write(color.G);
            Writer.Write(color.B);
        }

        /// <summary>
        /// Writes a <see cref="Color"/> in BGR order.
        /// </summary>
        /// <param name="color">The value to write.</param>
        public void WriteBgr(Color color)
        {
            Writer.Write(color.B);
            Writer.Write(color.G);
            Writer.Write(color.R);
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

                InternalStream.Write(buffer);
                return;
            }

            byte[] rental = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                for (int i = 0; i < length; i++)
                {
                    rental[i] = value;
                }

                InternalStream.Write(rental, 0, length);
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
            => Writer.Write(encoding.GetBytes(value));

        /// <summary>
        /// Write an optionally null-terminated 8-Bit <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        private void Write8BitString(string value, Encoding encoding, bool terminate)
        {
            Writer.Write(encoding.GetBytes(value));
            if (terminate)
            {
                InternalStream.WriteByte(0);
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
            Writer.Write(encoding.GetBytes(value));
            if (terminate)
            {
                InternalStream.WriteByte(0);
                InternalStream.WriteByte(0);
            }
        }

        /// <summary>
        /// Write a null-terminated <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        private void WriteCharsFixed(string value, Encoding encoding, int length, byte padding)
        {
            byte[] fixstr = new byte[length];
            for (int i = 0; i < length; i++)
                fixstr[i] = padding;

            Writer.Write(encoding.GetBytes(value + '\0', fixstr));
        }

        #endregion

        #region String ASCII

        /// <summary>
        /// Writes a ASCII encoded <see cref="string"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="terminate">Whether or not to add a null terminator.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteASCII(string value, bool terminate = false)
            => Write8BitString(value, Encoding.ASCII, terminate);

        /// <summary>
        /// Writes a ASCII encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <param name="length">The byte length of the fixed-size field.</param>
        /// <param name="padding">The padding value to use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteASCII(string value, int length, byte padding)
            => WriteCharsFixed(value, Encoding.ASCII, length, padding);

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF8(string value, int length, byte padding)
            => WriteCharsFixed(value, Encoding.UTF8, length, padding);

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteShiftJIS(string value, int length, byte padding)
            => WriteCharsFixed(value, EncodingHelper.ShiftJIS, length, padding);

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16(string value, int length, byte padding)
            => WriteCharsFixed(value, BigEndian ? EncodingHelper.UTF16BE : EncodingHelper.UTF16LE, length, padding);

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16BigEndian(string value, int length, byte padding)
            => WriteCharsFixed(value, EncodingHelper.UTF16BE, length, padding);

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUTF16LittleEndian(string value, int length, byte padding)
            => WriteCharsFixed(value, EncodingHelper.UTF16LE, length, padding);

        #endregion

        #region IDisposable

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (Steps.Count > 0)
                    {
                        throw new InvalidOperationException($"The stream is not stepped all the way out; Depth: {Steps.Count}");
                    }

                    Steps.Clear();
                    Steps.TrimExcess();
                    if (Reservations.Count > 0)
                    {
                        throw new InvalidOperationException($"Not all stream reservations have been been filled: [{string.Join(", ", Reservations.Keys)}]");
                    }

                    Reservations.Clear();
                    Reservations.TrimExcess();
                    Writer.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
