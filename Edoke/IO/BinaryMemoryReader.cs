using Edoke.Helpers;
using System;
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
    /// A binary reader for <see cref="byte"/> <see cref="Memory{T}"/> supporting endianness.
    /// </summary>
    public class BinaryMemoryReader : IBinaryReader
    {
        #region Constants

        /// <summary>
        /// The type name for 4-Byte varints.
        /// </summary>
        private const string VarintIntTypeName = "Varint32";

        /// <summary>
        /// The type name for 8-Byte varints.
        /// </summary>
        private const string VarintLongTypeName = "Varint64";

        /// <summary>
        /// Generic string formatting.
        /// </summary>
        private const string GenericFormat = "{0}";

        /// <summary>
        /// The string formatting for booleans.
        /// </summary>
        private const string BooleanFormat = GenericFormat;

        /// <summary>
        /// The string formatting for whole numbers.
        /// </summary>
        private const string WholeNumberFormat = "0x{0:X}";

        /// <summary>
        /// The string formatting for decimal numbers.
        /// </summary>
        private const string DecimalFormat = GenericFormat;

        #endregion

        #region Members

        /// <summary>
        /// The underlying memory.
        /// </summary>
        private readonly ReadOnlyMemory<byte> Buffer;

        /// <summary>
        /// A jump stack for step-ins.
        /// </summary>
        private readonly Stack<int> Steps;

        /// <summary>
        /// The current position of the reader.
        /// </summary>
        private int BufferOffset;

        /// <summary>
        /// Whether or not endianness is reversed.
        /// </summary>
        private bool IsEndiannessReversed;

        /// <summary>
        /// The backing field for <see cref="BigEndian"/>.
        /// </summary>
        private bool BigEndianField;

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
        /// The current position of the reader.
        /// </summary>
        public int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BufferOffset;
            set
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)value, (uint)Length, nameof(value));
                BufferOffset = value;
            }
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
        /// The length of the buffer.
        /// </summary>
        public int Length
            => Buffer.Length;

        /// <summary>
        /// The remaining length of the buffer from the current position.
        /// </summary>
        public int Remaining
            => Buffer.Length - BufferOffset;

        /// <summary>
        /// The depth of steps on the reader.
        /// </summary>
        public int StepDepth
            => Steps.Count;

        #endregion

        #region Constructors

        /// <summary>
        /// Create a new <see cref="BinaryMemoryReader"/> from a <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="ReadOnlyMemory{T}"/> to read.</param>
        /// <param name="bigEndian">Whether or not to read in big endian.</param>
        public BinaryMemoryReader(ReadOnlyMemory<byte> buffer, bool bigEndian)
        {
            Buffer = buffer;
            BufferOffset = 0;
            BigEndian = bigEndian;
            Steps = [];
        }

        /// <summary>
        /// Create a new <see cref="BinaryMemoryReader"/> from a <see cref="Memory{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="Memory{T}"/> to read.</param>
        /// <param name="bigEndian">Whether or not to read in big endian.</param>
        public BinaryMemoryReader(Memory<byte> buffer, bool bigEndian) : this((ReadOnlyMemory<byte>)buffer, bigEndian) { }

        /// <summary>
        /// Create a new <see cref="BinaryMemoryReader"/> from an <see cref="Array"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="Array"/> to read.</param>
        /// <param name="bigEndian">Whether or not to read in big endian.</param>
        public BinaryMemoryReader(byte[] buffer, bool bigEndian) : this(new ReadOnlyMemory<byte>(buffer), bigEndian) { }

        /// <summary>
        /// Create a new <see cref="BinaryMemoryReader"/> from a <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="ReadOnlyMemory{T}"/> to read.</param>
        public BinaryMemoryReader(ReadOnlyMemory<byte> buffer) : this(buffer, !BitConverter.IsLittleEndian) { }

        /// <summary>
        /// Create a new <see cref="BinaryMemoryReader"/> from a <see cref="Memory{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="Memory{T}"/> to read.</param>
        public BinaryMemoryReader(Memory<byte> buffer) : this((ReadOnlyMemory<byte>)buffer, !BitConverter.IsLittleEndian) { }

        /// <summary>
        /// Create a new <see cref="BinaryMemoryReader"/> from an <see cref="Array"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="Array"/> to read.</param>
        public BinaryMemoryReader(byte[] buffer) : this(new ReadOnlyMemory<byte>(buffer), !BitConverter.IsLittleEndian) { }

        #endregion

        #region Validation

        /// <summary>
        /// Validate the specified length argument.
        /// </summary>
        /// <param name="length">The length.</param>
        /// <exception cref="ArgumentOutOfRangeException">The argument was out of range.</exception>
        private void ValidateLength(int length)
        {
            if ((uint)length > (uint)Remaining)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Cannot read beyond the specified span.");
            }
        }

        /// <summary>
        /// Validate the specified position argument.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <exception cref="ArgumentOutOfRangeException">The argument was out of range.</exception>
        private void ValidatePosition(int position)
        {
            if ((uint)position > (uint)BufferOffset)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Cannot read beyond the specified span.");
            }
        }

        /// <summary>
        /// Validate the specified position and length arguments.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="length">The length.</param>
        /// <exception cref="ArgumentOutOfRangeException">An argument was out of range.</exception>
        private void ValidateArguments(int position, int length)
        {
            if ((uint)position > (uint)BufferOffset)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Cannot read beyond the specified span.");
            }

            if ((uint)length > (uint)(Buffer.Length - position))
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Cannot read beyond the specified span.");
            }
        }

        #endregion

        #region Step

        /// <summary>
        /// Store the current position of the reader on a stack, then move to the specified offset.
        /// </summary>
        public void StepIn(int offset)
        {
            Steps.Push(Position);
            Position = offset;
        }

        /// <summary>
        /// Restore the previous position of the reader from a stack.
        /// </summary>
        public void StepOut()
        {
            if (Steps.Count == 0)
            {
                throw new InvalidOperationException("Reader is already stepped all the way out.");
            }

            BufferOffset = Steps.Pop();
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

        #region Align

        /// <summary>
        /// Align the position of the reader to the specified alignment.
        /// </summary>
        /// <param name="alignment">The specified alignment.</param>
        /// <exception cref="ArgumentOutOfRangeException">The alignment argument was negative or zero.</exception>
        /// <exception cref="InvalidOperationException">The next alignment position was out of range.</exception>
        public void Align(int alignment)
        {
            if (alignment < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), $"Alignment value must be positive and non-zero: {alignment} < {1}");
            }

            int remainder = BufferOffset % alignment;
            if (remainder > 0)
            {
                int finalPosition = checked(BufferOffset + (alignment - remainder));
                if (finalPosition > Length)
                {
                    throw new InvalidOperationException($"Next alignment position is out of range: {finalPosition} > {Length}");
                }

                BufferOffset = finalPosition;
            }
        }

        /// <summary>
        /// Align the position of the reader relative to the specified position to the specified alignment.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="alignment">The specified alignment.</param>
        /// <exception cref="ArgumentOutOfRangeException">An argument was out of range.</exception>
        /// <exception cref="InvalidOperationException">An argument or the next alignment position was out of range.</exception>
        public void AlignFrom(int position, int alignment)
        {
            if (position < 1 || position > Length)
            {
                throw new ArgumentOutOfRangeException(nameof(position), $"Position value is out of range: {position} < {1} || {position} > {Length}");
            }

            if (alignment < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), $"Alignment value must be positive and non-zero: {alignment} < {1}");
            }

            int remainder = position % alignment;
            if (remainder > 0)
            {
                int finalPosition = checked(position + (alignment - remainder));
                if (finalPosition > Length)
                {
                    throw new InvalidOperationException($"Next alignment position is out of range: {finalPosition} > {Length}");
                }

                BufferOffset = finalPosition;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// Read the specified unmanaged type.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to read.</typeparam>
        /// <returns>The read value.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        private unsafe T Read<T>() where T : unmanaged
        {
            int size = sizeof(T);
            int endPosition = BufferOffset + size;
            if (endPosition > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            var value = Unsafe.ReadUnaligned<T>(ref Unsafe.Add(ref MemoryMarshal.GetReference(Buffer.Span), BufferOffset));
            BufferOffset = endPosition;
            return value;
        }

        /// <summary>
        /// Read a <see cref="ReadOnlySpan{T}"/> of the specified unmanaged type.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to read.</typeparam>
        /// <param name="count">The amount to read.</param>
        /// <returns>The read values.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        private unsafe ReadOnlySpan<T> ReadSpan<T>(int count) where T : unmanaged
        {
            int size = sizeof(T) * count;
            int endPosition = BufferOffset + size;
            if (endPosition > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            var value = MemoryMarshal.Cast<byte, T>(Buffer.Span.Slice(BufferOffset, size));
            BufferOffset = endPosition;
            return value;
        }

        /// <summary>
        /// Read the specified unmanaged type at the specified position.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to read.</typeparam>
        /// <param name="position">The specified position.</param>
        /// <returns>The read value.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        private unsafe T Get<T>(int position) where T : unmanaged
        {
            int size = sizeof(T);
            if ((position + size) > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            return Unsafe.ReadUnaligned<T>(ref Unsafe.Add(ref MemoryMarshal.GetReference(Buffer.Span), position));
        }

        /// <summary>
        /// Get a <see cref="ReadOnlySpan{T}"/> of the specified unmanaged type at the specified position.
        /// </summary>
        /// <typeparam name="T">The unmanaged type to read.</typeparam>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to read.</param>
        /// <returns>The read values.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        private unsafe ReadOnlySpan<T> GetSpan<T>(int position, int count) where T : unmanaged
        {
            int size = sizeof(T) * count;
            if ((position + size) > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            return MemoryMarshal.Cast<byte, T>(Buffer.Span.Slice(position, size));
        }

        #endregion

        #region SByte

        /// <summary>
        /// Reads an <see cref="sbyte"/>.
        /// </summary>
        /// <returns>An <see cref="sbyte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte ReadSByte()
            => Read<sbyte>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="sbyte"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="sbyte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<sbyte> ReadSByteSpan(int count)
            => ReadSpan<sbyte>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="sbyte"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="sbyte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte[] ReadSBytes(int count)
            => ReadSpan<sbyte>(count).ToArray();

        /// <summary>
        /// Gets an <see cref="sbyte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>An <see cref="sbyte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte GetSByte(int position)
            => Get<sbyte>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="sbyte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="sbyte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<sbyte> GetSByteSpan(int position, int count)
            => GetSpan<sbyte>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="sbyte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="sbyte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte[] GetSBytes(int position, int count)
            => GetSpan<sbyte>(position, count).ToArray();

        /// <summary>
        /// Reads an <see cref="sbyte"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>An <see cref="sbyte"/>.</returns>
        public sbyte AssertSByte(sbyte option)
            => AssertHelper.Assert(ReadSByte(), nameof(SByte), WholeNumberFormat, option);

        /// <summary>
        /// Reads an <see cref="sbyte"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>An <see cref="sbyte"/>.</returns>
        public sbyte AssertSByte(ReadOnlySpan<sbyte> options)
            => AssertHelper.Assert(ReadSByte(), nameof(SByte), WholeNumberFormat, options);

        #endregion

        #region Byte

        /// <summary>
        /// Reads a <see cref="byte"/>.
        /// </summary>
        /// <returns>A <see cref="byte"/>.</returns>
        public byte ReadByte()
        {
            int endPosition = BufferOffset + 1;
            if (endPosition > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            byte value = Buffer.Span[BufferOffset];
            BufferOffset = endPosition;
            return value;
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/>.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        public ReadOnlyMemory<byte> ReadByteMemory(int count)
        {
            int endPosition = BufferOffset + count;
            if (endPosition > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            var view = Buffer.Slice(BufferOffset, Length);
            BufferOffset = endPosition;
            return view;
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        public ReadOnlySpan<byte> ReadByteSpan(int count)
        {
            int endPosition = BufferOffset + count;
            if (endPosition > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            var value = Buffer.Span.Slice(BufferOffset, count);
            BufferOffset = endPosition;
            return value;
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="byte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[] ReadBytes(int count)
            => ReadByteSpan(count).ToArray();

        /// <summary>
        /// Gets a <see cref="byte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="byte"/>.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        public byte GetByte(int position)
        {
            if ((position + 1) > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            return Buffer.Span[position];
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/>.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        public ReadOnlyMemory<byte> GetByteMemory(int position, int count)
        {
            if ((position + count) > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            return Buffer.Slice(position, Length);
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        /// <exception cref="InvalidOperationException">The read went beyond the specified memory.</exception>
        public ReadOnlySpan<byte> GetByteSpan(int position, int count)
        {
            if ((position + count) > Length)
            {
                throw new InvalidOperationException("Cannot read beyond the specified memory.");
            }

            return Buffer.Span.Slice(position, count);
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="byte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="byte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[] GetBytes(int position, int count)
            => GetByteSpan(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="byte"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="byte"/>.</returns>
        public byte AssertByte(byte option)
            => AssertHelper.Assert(ReadByte(), nameof(Byte), WholeNumberFormat, option);

        /// <summary>
        /// Reads a <see cref="byte"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="byte"/>.</returns>
        public byte AssertByte(ReadOnlySpan<byte> options)
            => AssertHelper.Assert(ReadByte(), nameof(Byte), WholeNumberFormat, options);

        #endregion

        #region Int16

        /// <summary>
        /// Reads a <see cref="short"/>.
        /// </summary>
        /// <returns>A <see cref="short"/>.</returns>
        public short ReadInt16()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Read<short>())
            : Read<short>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.</returns>
        public ReadOnlySpan<short> ReadInt16Span(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<short>(count))
            : ReadSpan<short>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="short"/>.</returns>
        public short[] ReadInt16s(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<short>(count))
            : ReadSpan<short>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="short"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="short"/>.</returns>
        public short GetInt16(int position)
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Get<short>(position))
            : Get<short>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="short"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="short"/>.</returns>
        public ReadOnlySpan<short> GetInt16Span(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<short>(position, count))
            : GetSpan<short>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="short"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="short"/>.</returns>
        public short[] GetInt16s(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<short>(position, count))
            : GetSpan<short>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="short"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="short"/>.</returns>
        public short AssertInt16(short option)
            => AssertHelper.Assert(ReadInt16(), nameof(Int16), WholeNumberFormat, option);

        /// <summary>
        /// Reads a <see cref="short"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="short"/>.</returns>
        public short AssertInt16(ReadOnlySpan<short> options)
            => AssertHelper.Assert(ReadInt16(), nameof(Int16), WholeNumberFormat, options);

        #endregion

        #region UInt16

        /// <summary>
        /// Reads a <see cref="ushort"/>.
        /// </summary>
        /// <returns>A <see cref="ushort"/>.</returns>
        public ushort ReadUInt16()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Read<ushort>())
            : Read<ushort>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="ushort"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="ushort"/>.</returns>
        public ReadOnlySpan<ushort> ReadUInt16Span(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<ushort>(count))
            : ReadSpan<ushort>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="ushort"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="ushort"/>.</returns>
        public ushort[] ReadUInt16s(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<ushort>(count))
            : ReadSpan<ushort>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="ushort"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="ushort"/>.</returns>
        public ushort GetUInt16(int position)
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Get<ushort>(position))
            : Get<ushort>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="ushort"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="ushort"/>.</returns>
        public ReadOnlySpan<ushort> GetUInt16Span(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<ushort>(position, count))
            : GetSpan<ushort>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="ushort"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="ushort"/>.</returns>
        public ushort[] GetUInt16s(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<ushort>(position, count))
            : GetSpan<ushort>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="ushort"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="ushort"/>.</returns>
        public ushort AssertUInt16(ushort option)
            => AssertHelper.Assert(ReadUInt16(), nameof(UInt16), WholeNumberFormat, option);

        /// <summary>
        /// Reads a <see cref="ushort"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="ushort"/>.</returns>
        public ushort AssertUInt16(ReadOnlySpan<ushort> options)
            => AssertHelper.Assert(ReadUInt16(), nameof(UInt16), WholeNumberFormat, options);

        #endregion

        #region Int32

        /// <summary>
        /// Reads an <see cref="int"/>.
        /// </summary>
        /// <returns>An <see cref="int"/>.</returns>
        public int ReadInt32()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Read<int>())
            : Read<int>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="int"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="int"/>.</returns>
        public ReadOnlySpan<int> ReadInt32Span(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<int>(count))
            : ReadSpan<int>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="int"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="int"/>.</returns>
        public int[] ReadInt32s(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<int>(count))
            : ReadSpan<int>(count).ToArray();

        /// <summary>
        /// Gets an <see cref="int"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>An <see cref="int"/>.</returns>
        public int GetInt32(int position)
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Get<int>(position))
            : Get<int>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="int"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="int"/>.</returns>
        public ReadOnlySpan<int> GetInt32Span(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<int>(position, count))
            : GetSpan<int>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="int"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="int"/>.</returns>
        public int[] GetInt32s(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<int>(position, count))
            : GetSpan<int>(position, count).ToArray();

        /// <summary>
        /// Reads an <see cref="int"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>An <see cref="int"/>.</returns>
        public int AssertInt32(int option)
            => AssertHelper.Assert(ReadInt32(), nameof(Int32), WholeNumberFormat, option);

        /// <summary>
        /// Reads an <see cref="int"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>An <see cref="int"/>.</returns>
        public int AssertInt32(ReadOnlySpan<int> options)
            => AssertHelper.Assert(ReadInt32(), nameof(Int32), WholeNumberFormat, options);

        #endregion

        #region UInt32

        /// <summary>
        /// Reads an <see cref="uint"/>.
        /// </summary>
        /// <returns>An <see cref="uint"/>.</returns>
        public uint ReadUInt32()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Read<uint>())
            : Read<uint>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="uint"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="uint"/>.</returns>
        public ReadOnlySpan<uint> ReadUInt32Span(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<uint>(count))
            : ReadSpan<uint>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="uint"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="uint"/>.</returns>
        public uint[] ReadUInt32s(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<uint>(count))
            : ReadSpan<uint>(count).ToArray();

        /// <summary>
        /// Gets an <see cref="uint"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>An <see cref="uint"/>.</returns>
        public uint GetUInt32(int position)
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Get<uint>(position))
            : Get<uint>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="uint"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="uint"/>.</returns>
        public ReadOnlySpan<uint> GetUInt32Span(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<uint>(position, count))
            : GetSpan<uint>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="uint"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="uint"/>.</returns>
        public uint[] GetUInt32s(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<uint>(position, count))
            : GetSpan<uint>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="uint"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="uint"/>.</returns>
        public uint AssertUInt32(uint option)
            => AssertHelper.Assert(ReadUInt32(), nameof(UInt32), WholeNumberFormat, option);

        /// <summary>
        /// Reads a <see cref="uint"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="uint"/>.</returns>
        public uint AssertUInt32(ReadOnlySpan<uint> options)
            => AssertHelper.Assert(ReadUInt32(), nameof(UInt32), WholeNumberFormat, options);

        #endregion

        #region Int64

        /// <summary>
        /// Reads a <see cref="long"/>.
        /// </summary>
        /// <returns>A <see cref="long"/>.</returns>
        public long ReadInt64()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Read<long>())
            : Read<long>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="long"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="long"/>.</returns>
        public ReadOnlySpan<long> ReadInt64Span(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<long>(count))
            : ReadSpan<long>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="long"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="long"/>.</returns>
        public long[] ReadInt64s(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<long>(count))
            : ReadSpan<long>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="long"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="long"/>.</returns>
        public long GetInt64(int position)
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Get<long>(position))
            : Get<long>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="long"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="long"/>.</returns>
        public ReadOnlySpan<long> GetInt64Span(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<long>(position, count))
            : GetSpan<long>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="long"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="long"/>.</returns>
        public long[] GetInt64s(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<long>(position, count))
            : GetSpan<long>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="long"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="long"/>.</returns>
        public long AssertInt64(long option)
            => AssertHelper.Assert(ReadInt64(), nameof(Int64), WholeNumberFormat, option);

        /// <summary>
        /// Reads a <see cref="long"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="long"/>.</returns>
        public long AssertInt64(ReadOnlySpan<long> options)
            => AssertHelper.Assert(ReadInt64(), nameof(Int64), WholeNumberFormat, options);

        #endregion   

        #region UInt64

        /// <summary>
        /// Reads a <see cref="ulong"/>.
        /// </summary>
        /// <returns>A <see cref="ulong"/>.</returns>
        public ulong ReadUInt64()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Read<ulong>())
            : Read<ulong>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="ulong"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="ulong"/>.</returns>
        public ReadOnlySpan<ulong> ReadUInt64Span(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<ulong>(count))
            : ReadSpan<ulong>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="ulong"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="ulong"/>.</returns>
        public ulong[] ReadUInt64s(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<ulong>(count))
            : ReadSpan<ulong>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="ulong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="ulong"/>.</returns>
        public ulong GetUInt64(int position)
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Get<ulong>(position))
            : Get<ulong>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="ulong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="ulong"/>.</returns>
        public ReadOnlySpan<ulong> GetUInt64Span(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<ulong>(position, count))
            : GetSpan<ulong>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="ulong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="ulong"/>.</returns>
        public ulong[] GetUInt64s(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<ulong>(position, count))
            : GetSpan<ulong>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="ulong"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="ulong"/>.</returns>
        public ulong AssertUInt64(ulong option)
            => AssertHelper.Assert(ReadUInt64(), nameof(UInt64), WholeNumberFormat, option);

        /// <summary>
        /// Reads a <see cref="ulong"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="ulong"/>.</returns>
        public ulong AssertUInt64(ReadOnlySpan<ulong> options)
            => AssertHelper.Assert(ReadUInt64(), nameof(UInt64), WholeNumberFormat, options);

        #endregion

        #region Int128

        /// <summary>
        /// Reads a <see cref="Int128"/>.
        /// </summary>
        /// <returns>A <see cref="Int128"/>.</returns>
        public Int128 ReadInt128()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Read<Int128>())
            : Read<Int128>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="Int128"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Int128"/>.</returns>
        public ReadOnlySpan<Int128> ReadInt128Span(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<Int128>(count))
            : ReadSpan<Int128>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Int128"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Int128"/>.</returns>
        public Int128[] ReadInt128s(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<Int128>(count))
            : ReadSpan<Int128>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="Int128"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Int128"/>.</returns>
        public Int128 GetInt128(int position)
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Get<Int128>(position))
            : Get<Int128>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="Int128"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Int128"/>.</returns>
        public ReadOnlySpan<Int128> GetInt128Span(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<Int128>(position, count))
            : GetSpan<Int128>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Int128"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Int128"/>.</returns>
        public Int128[] GetInt128s(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<Int128>(position, count))
            : GetSpan<Int128>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="Int128"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="Int128"/>.</returns>
        public Int128 AssertInt128(Int128 option)
            => AssertHelper.Assert(ReadInt128(), nameof(Int128), WholeNumberFormat, option);

        /// <summary>
        /// Reads a <see cref="Int128"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="Int128"/>.</returns>
        public Int128 AssertInt128(ReadOnlySpan<Int128> options)
            => AssertHelper.Assert(ReadInt128(), nameof(Int128), WholeNumberFormat, options);

        #endregion

        #region UInt128

        /// <summary>
        /// Reads a <see cref="UInt128"/>.
        /// </summary>
        /// <returns>A <see cref="UInt128"/>.</returns>
        public UInt128 ReadUInt128()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Read<UInt128>())
            : Read<UInt128>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="UInt128"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="UInt128"/>.</returns>
        public ReadOnlySpan<UInt128> ReadUInt128Span(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<UInt128>(count))
            : ReadSpan<UInt128>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="UInt128"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="UInt128"/>.</returns>
        public UInt128[] ReadUInt128s(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<UInt128>(count))
            : ReadSpan<UInt128>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="UInt128"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="UInt128"/>.</returns>
        public UInt128 GetUInt128(int position)
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Get<UInt128>(position))
            : Get<UInt128>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="UInt128"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="UInt128"/>.</returns>
        public ReadOnlySpan<UInt128> GetUInt128Span(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<UInt128>(position, count))
            : GetSpan<UInt128>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="UInt128"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="UInt128"/>.</returns>
        public UInt128[] GetUInt128s(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<UInt128>(position, count))
            : GetSpan<UInt128>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="UInt128"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="UInt128"/>.</returns>
        public UInt128 AssertUInt128(UInt128 option)
            => AssertHelper.Assert(ReadUInt128(), nameof(UInt128), WholeNumberFormat, option);

        /// <summary>
        /// Reads a <see cref="UInt128"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="UInt128"/>.</returns>
        public UInt128 AssertUInt128(ReadOnlySpan<UInt128> options)
            => AssertHelper.Assert(ReadUInt128(), nameof(UInt128), WholeNumberFormat, options);

        #endregion

        #region Half

        /// <summary>
        /// Reads a <see cref="Half"/>.
        /// </summary>
        /// <returns>A <see cref="Half"/>.</returns>
        public Half ReadHalf()
            => IsEndiannessReversed
            ? BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReverseEndianness(Read<ushort>()))
            : Read<Half>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="Half"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Half"/>.</returns>
        public ReadOnlySpan<Half> ReadHalfSpan(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<Half>(count))
            : ReadSpan<Half>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Half"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Half"/>.</returns>
        public Half[] ReadHalfs(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<Half>(count))
            : ReadSpan<Half>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="Half"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Half"/>.</returns>
        public Half GetHalf(int position)
            => IsEndiannessReversed
            ? BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReverseEndianness(Get<ushort>(position)))
            : Get<Half>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="Half"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Half"/>.</returns>
        public ReadOnlySpan<Half> GetHalfSpan(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<Half>(position, count))
            : GetSpan<Half>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Half"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Half"/>.</returns>
        public Half[] GetHalfs(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<Half>(position, count))
            : GetSpan<Half>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="Half"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="Half"/>.</returns>
        public Half AssertHalf(Half option)
            => AssertHelper.Assert(ReadHalf(), nameof(Half), DecimalFormat, option);

        /// <summary>
        /// Reads a <see cref="Half"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="Half"/>.</returns>
        public Half AssertHalf(ReadOnlySpan<Half> options)
            => AssertHelper.Assert(ReadHalf(), nameof(Half), DecimalFormat, options);

        #endregion

        #region Single

        /// <summary>
        /// Reads a <see cref="float"/>.
        /// </summary>
        /// <returns>A <see cref="float"/>.</returns>
        public float ReadSingle()
            => IsEndiannessReversed
            ? BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Read<uint>()))
            : Read<float>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="float"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="float"/>.</returns>
        public ReadOnlySpan<float> ReadSingleSpan(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<float>(count))
            : ReadSpan<float>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="float"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="float"/>.</returns>
        public float[] ReadSingles(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<float>(count))
            : ReadSpan<float>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="float"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="float"/>.</returns>
        public float GetSingle(int position)
            => IsEndiannessReversed
            ? BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Get<uint>(position)))
            : Get<float>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="float"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="float"/>.</returns>
        public ReadOnlySpan<float> GetSingleSpan(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<float>(position, count))
            : GetSpan<float>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="float"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="float"/>.</returns>
        public float[] GetSingles(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<float>(position, count))
            : GetSpan<float>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="float"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="float"/>.</returns>
        public float AssertSingle(float option)
            => AssertHelper.Assert(ReadSingle(), nameof(Single), DecimalFormat, option);

        /// <summary>
        /// Reads a <see cref="float"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="float"/>.</returns>
        public float AssertSingle(ReadOnlySpan<float> options)
            => AssertHelper.Assert(ReadSingle(), nameof(Single), DecimalFormat, options);

        #endregion

        #region Double

        /// <summary>
        /// Reads a <see cref="double"/>.
        /// </summary>
        /// <returns>A <see cref="double"/>.</returns>
        public double ReadDouble()
            => IsEndiannessReversed
            ? BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReverseEndianness(Read<ulong>()))
            : Read<double>();

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="double"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="double"/>.</returns>
        public ReadOnlySpan<double> ReadDoubleSpan(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<double>(count))
            : ReadSpan<double>(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="double"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="double"/>.</returns>
        public double[] ReadDoubles(int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(ReadSpan<double>(count))
            : ReadSpan<double>(count).ToArray();

        /// <summary>
        /// Gets a <see cref="double"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="double"/>.</returns>
        public double GetDouble(int position)
            => IsEndiannessReversed
            ? BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReverseEndianness(Get<ulong>(position)))
            : Get<double>(position);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="double"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="double"/>.</returns>
        public ReadOnlySpan<double> GetDoubleSpan(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<double>(position, count))
            : GetSpan<double>(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="double"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="double"/>.</returns>
        public double[] GetDoubles(int position, int count)
            => IsEndiannessReversed
            ? EndianHelper.CopyEndianReversed(GetSpan<double>(position, count))
            : GetSpan<double>(position, count).ToArray();

        /// <summary>
        /// Reads a <see cref="double"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="double"/>.</returns>
        public double AssertDouble(double option)
            => AssertHelper.Assert(ReadDouble(), nameof(Double), DecimalFormat, option);

        /// <summary>
        /// Reads a <see cref="double"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="double"/>.</returns>
        public double AssertDouble(ReadOnlySpan<double> options)
            => AssertHelper.Assert(ReadDouble(), nameof(Double), DecimalFormat, options);

        #endregion

        #region Varint

        /// <summary>
        /// Reads a varint according to the set <see cref="VarintType"/>.
        /// </summary>
        /// <returns>The read value.</returns>
        public long ReadVarint()
        {
            if (VarintLong)
            {
                return ReadInt64();
            }
            else
            {
                return ReadInt32();
            }
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of varints according to <see cref="VarintLong"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>The read values.</returns>
        public ReadOnlySpan<long> ReadVarintSpan(int count)
        {
            if (VarintLong)
            {
                return ReadInt64Span(count);
            }
            else
            {
                long[] values = new long[count];
                var intspan = ReadSpan<int>(count);
                if (IsEndiannessReversed)
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReverseEndianness(intspan[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = intspan[i];
                    }
                }

                return values;
            }
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of varints according to <see cref="VarintLong"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>The read values.</returns>
        public long[] ReadVarints(int count)
        {
            if (VarintLong)
            {
                return ReadInt64s(count);
            }
            else
            {
                long[] values = new long[count];
                var intspan = ReadSpan<int>(count);
                if (IsEndiannessReversed)
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReverseEndianness(intspan[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = intspan[i];
                    }
                }

                return values;
            }
        }

        /// <summary>
        /// Gets a varint according to <see cref="VarintLong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>The read value.</returns>
        public long GetVarint(int position)
        {
            if (VarintLong)
            {
                return GetInt64(position);
            }
            else
            {
                return GetInt32(position);
            }
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of varints according to <see cref="VarintLong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to read.</param>
        /// <returns>The read values.</returns>
        public ReadOnlySpan<long> GetVarintSpan(int position, int count)
        {
            if (VarintLong)
            {
                return GetInt64Span(position, count);
            }
            else
            {
                long[] values = new long[count];
                var intspan = GetSpan<int>(position, count);
                if (IsEndiannessReversed)
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReverseEndianness(intspan[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = intspan[i];
                    }
                }

                return values;
            }
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of varints according to <see cref="VarintLong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to read.</param>
        /// <returns>The read values.</returns>
        public long[] GetVarints(int position, int count)
        {
            if (VarintLong)
            {
                return GetInt64s(position, count);
            }
            else
            {
                long[] values = new long[count];
                var intspan = GetSpan<int>(position, count);
                if (IsEndiannessReversed)
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReverseEndianness(intspan[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = intspan[i];
                    }
                }

                return values;
            }
        }

        /// <summary>
        /// Reads a varint according to <see cref="VarintLong"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A varint.</returns>
        public long AssertVarint(long option)
            => AssertHelper.Assert(ReadVarint(), VarintLong ? VarintLongTypeName : VarintIntTypeName, WholeNumberFormat, option);

        /// <summary>
        /// Reads a varint according to <see cref="VarintLong"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A varint.</returns>
        public long AssertVarint(ReadOnlySpan<long> options)
            => AssertHelper.Assert(ReadVarint(), VarintLong ? VarintLongTypeName : VarintIntTypeName, WholeNumberFormat, options);

        #endregion

        #region Boolean

        /// <summary>
        /// Reads a <see cref="bool"/>.
        /// </summary>
        /// <returns>A <see cref="bool"/>.</returns>
        public bool ReadBoolean()
        {
            var value = ReadByte();
            return value == 1 || (value == 0 ? false : throw new InvalidDataException($"{nameof(ReadBoolean)} read invalid {nameof(Boolean)} value: {value}"));
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="bool"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="bool"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<bool> ReadBooleanSpan(int count)
            => ReadBooleans(count);

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="bool"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="bool"/>.</returns>
        public bool[] ReadBooleans(int count)
        {
            var array = new bool[count];
            var span = GetSpan<byte>(Position, count);
            for (int i = 0; i < count; i++)
            {
                var value = span[i];
                array[i] = value == 1 || (value == 0 ? false : throw new InvalidDataException($"{nameof(ReadBoolean)} read invalid {nameof(Boolean)} value: {value}"));
                BufferOffset++;
            }
            return array;
        }

        /// <summary>
        /// Gets a <see cref="bool"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="bool"/>.</returns>
        public bool GetBoolean(int position)
        {
            var value = GetByte(position);
            return value == 1 || (value == 0 ? false : throw new InvalidDataException($"{nameof(ReadBoolean)} read invalid {nameof(Boolean)} value: {value}"));
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="bool"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="bool"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<bool> GetBooleanSpan(int position, int count)
            => GetBooleans(position, count);

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="bool"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="bool"/>.</returns>
        public bool[] GetBooleans(int position, int count)
        {
            var array = new bool[count];
            var span = GetSpan<byte>(position, count);
            for (int i = 0; i < count; i++)
            {
                var value = span[i];
                array[i] = value == 1 || (value == 0 ? false : throw new InvalidDataException($"{nameof(ReadBoolean)} read invalid {nameof(Boolean)} value: {value}"));
            }
            return array;
        }

        /// <summary>
        /// Reads a <see cref="bool"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="bool"/>.</returns>
        public bool AssertBoolean(bool option)
            => AssertHelper.Assert(ReadBoolean(), nameof(Boolean), BooleanFormat, option);

        /// <summary>
        /// Reads a <see cref="bool"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="bool"/>.</returns>
        public bool AssertBoolean(ReadOnlySpan<bool> options)
            => AssertHelper.Assert(ReadBoolean(), nameof(Boolean), BooleanFormat, options);

        #endregion

        #region Enum

        /// <summary>
        /// Reads an <see cref="Enum"/> using <see cref="sbyte"/> as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        public TEnum ReadEnumSByte<TEnum>() where TEnum : Enum
            => AssertHelper.AssertEnum<TEnum, sbyte>((sbyte)ReadByte());

        /// <summary>
        /// Reads an <see cref="Enum"/> using <see cref="byte"/> as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        public TEnum ReadEnumByte<TEnum>() where TEnum : Enum
            => AssertHelper.AssertEnum<TEnum, byte>(ReadByte());

        /// <summary>
        /// Reads an <see cref="Enum"/> using <see cref="short"/> as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        public TEnum ReadEnumInt16<TEnum>() where TEnum : Enum
            => AssertHelper.AssertEnum<TEnum, short>(ReadInt16());

        /// <summary>
        /// Reads an <see cref="Enum"/> using <see cref="ushort"/> as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        public TEnum ReadEnumUInt16<TEnum>() where TEnum : Enum
            => AssertHelper.AssertEnum<TEnum, ushort>(ReadUInt16());

        /// <summary>
        /// Reads an <see cref="Enum"/> using <see cref="int"/> as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        public TEnum ReadEnumInt32<TEnum>() where TEnum : Enum
            => AssertHelper.AssertEnum<TEnum, int>(ReadInt32());

        /// <summary>
        /// Reads an <see cref="Enum"/> using <see cref="uint"/> as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        public TEnum ReadEnumUInt32<TEnum>() where TEnum : Enum
            => AssertHelper.AssertEnum<TEnum, uint>(ReadUInt32());

        /// <summary>
        /// Reads an <see cref="Enum"/> using <see cref="long"/> as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        public TEnum ReadEnumInt64<TEnum>() where TEnum : Enum
            => AssertHelper.AssertEnum<TEnum, long>(ReadInt64());

        /// <summary>
        /// Reads an <see cref="Enum"/> using <see cref="ulong"/> as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        public TEnum ReadEnumUInt64<TEnum>() where TEnum : Enum
            => AssertHelper.AssertEnum<TEnum, ulong>(ReadUInt64());

        /// <summary>
        /// Reads an <see cref="Enum"/> automatically detecting the underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        /// <exception cref="InvalidDataException">The underlying type could not be determined.</exception>
        public TEnum ReadEnum<TEnum>() where TEnum : Enum
        {
            Type type = Enum.GetUnderlyingType(typeof(TEnum));
            if (type == typeof(sbyte))
            {
                return ReadEnumSByte<TEnum>();
            }
            else if (type == typeof(byte))
            {
                return ReadEnumByte<TEnum>();
            }
            else if (type == typeof(short))
            {
                return ReadEnumInt16<TEnum>();
            }
            else if (type == typeof(ushort))
            {
                return ReadEnumUInt16<TEnum>();
            }
            else if (type == typeof(int))
            {
                return ReadEnumInt32<TEnum>();
            }
            else if (type == typeof(uint))
            {
                return ReadEnumUInt32<TEnum>();
            }
            else if (type == typeof(long))
            {
                return ReadEnumInt64<TEnum>();
            }
            else if (type == typeof(ulong))
            {
                return ReadEnumUInt64<TEnum>();
            }
            else
            {
                throw new InvalidDataException($"Enum {typeof(TEnum).Name} has an unknown underlying value type: {type.Name}");
            }
        }

        /// <summary>
        /// Reads an <see cref="Enum"/> using an 8-bit type as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        /// <exception cref="InvalidDataException">The underlying type could not be determined.</exception>
        public TEnum ReadEnum8<TEnum>() where TEnum : Enum
        {
            Type type = Enum.GetUnderlyingType(typeof(TEnum));
            if (type == typeof(sbyte))
            {
                return ReadEnumSByte<TEnum>();
            }
            else if (type == typeof(byte))
            {
                return ReadEnumByte<TEnum>();
            }
            else
            {
                throw new InvalidDataException($"Enum {typeof(TEnum).Name} has an invalid underlying value type: {type.Name}");
            }
        }

        /// <summary>
        /// Reads an <see cref="Enum"/> using a 16-bit type as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        /// <exception cref="InvalidDataException">The underlying type could not be determined.</exception>
        public TEnum ReadEnum16<TEnum>() where TEnum : Enum
        {
            Type type = Enum.GetUnderlyingType(typeof(TEnum));
            if (type == typeof(short))
            {
                return ReadEnumInt16<TEnum>();
            }
            else if (type == typeof(ushort))
            {
                return ReadEnumUInt16<TEnum>();
            }
            else
            {
                throw new InvalidDataException($"Enum {typeof(TEnum).Name} has an invalid underlying value type: {type.Name}");
            }
        }

        /// <summary>
        /// Reads an <see cref="Enum"/> using a 32-bit type as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        /// <exception cref="InvalidDataException">The underlying type could not be determined.</exception>
        public TEnum ReadEnum32<TEnum>() where TEnum : Enum
        {
            Type type = Enum.GetUnderlyingType(typeof(TEnum));
            if (type == typeof(int))
            {
                return ReadEnumInt32<TEnum>();
            }
            else if (type == typeof(uint))
            {
                return ReadEnumUInt32<TEnum>();
            }
            else
            {
                throw new InvalidDataException($"Enum {typeof(TEnum).Name} has an invalid underlying value type: {type.Name}");
            }
        }

        /// <summary>
        /// Reads an <see cref="Enum"/> using a 64-bit type as an underlying type.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <returns>The <see cref="Enum"/> value.</returns>
        /// <exception cref="InvalidDataException">The underlying type could not be determined.</exception>
        public TEnum ReadEnum64<TEnum>() where TEnum : Enum
        {
            Type type = Enum.GetUnderlyingType(typeof(TEnum));
            if (type == typeof(long))
            {
                return ReadEnumInt64<TEnum>();
            }
            else if (type == typeof(ulong))
            {
                return ReadEnumUInt64<TEnum>();
            }
            else
            {
                throw new InvalidDataException($"Enum {typeof(TEnum).Name} has an invalid underlying value type: {type.Name}");
            }
        }

        #endregion

        #region Vector2

        /// <summary>
        /// Reads a <see cref="Vector2"/>.
        /// </summary>
        /// <returns>A <see cref="Vector2"/>.</returns>
        public Vector2 ReadVector2()
        {
            if (IsEndiannessReversed)
            {
                uint xint = Read<uint>();
                uint yint = Read<uint>();
                xint = BinaryPrimitives.ReverseEndianness(xint);
                yint = BinaryPrimitives.ReverseEndianness(yint);
                float x = BitConverter.UInt32BitsToSingle(xint);
                float y = BitConverter.UInt32BitsToSingle(yint);
                return new Vector2(x, y);
            }
            else
            {
                return Read<Vector2>();
            }
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector2"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Vector2"/>.</returns>
        public unsafe ReadOnlySpan<Vector2> ReadVector2Span(int count)
        {
            if (IsEndiannessReversed)
            {
                var span = ReadUInt32Span(count * 2);
                fixed (uint* pf = &span[0])
                {
                    return new ReadOnlySpan<Vector2>(pf, count);
                }
            }
            else
            {
                return ReadSpan<Vector2>(count);
            }
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Vector2"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector2"/>.</returns>
        public unsafe Vector2[] ReadVector2s(int count)
        {
            if (IsEndiannessReversed)
            {
                var span = ReadUInt32Span(count * 2);
                fixed (uint* pf = &span[0])
                {
                    return new Span<Vector2>(pf, count).ToArray();
                }
            }
            else
            {
                return ReadSpan<Vector2>(count).ToArray();
            }
        }

        /// <summary>
        /// Gets a <see cref="Vector2"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Vector2"/>.</returns>
        public Vector2 GetVector2(int position)
        {
            if (IsEndiannessReversed)
            {
                uint xint = Get<uint>(position);
                uint yint = Get<uint>(position + sizeof(float));
                xint = BinaryPrimitives.ReverseEndianness(xint);
                yint = BinaryPrimitives.ReverseEndianness(yint);
                float x = BitConverter.UInt32BitsToSingle(xint);
                float y = BitConverter.UInt32BitsToSingle(yint);
                return new Vector2(x, y);
            }
            else
            {
                return Get<Vector2>(position);
            }
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector2"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Vector2"/>.</returns>
        public unsafe ReadOnlySpan<Vector2> GetVector2Span(int position, int count)
        {
            if (IsEndiannessReversed)
            {
                var span = GetUInt32Span(position, count * sizeof(Vector2));
                fixed (uint* pf = &span[0])
                {
                    return new ReadOnlySpan<Vector2>(pf, count);
                }
            }
            else
            {
                return GetSpan<Vector2>(position, count);
            }
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Vector2"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector2"/>.</returns>
        public unsafe Vector2[] GetVector2s(int position, int count)
        {
            if (IsEndiannessReversed)
            {
                var span = GetUInt32Span(position, count * 2);
                fixed (uint* pf = &span[0])
                {
                    return new Span<Vector2>(pf, count).ToArray();
                }
            }
            else
            {
                return GetSpan<Vector2>(position, count).ToArray();
            }
        }

        #endregion

        #region Vector3

        /// <summary>
        /// Reads a <see cref="Vector3"/>.
        /// </summary>
        /// <returns>A <see cref="Vector3"/>.</returns>
        public Vector3 ReadVector3()
        {
            if (IsEndiannessReversed)
            {
                uint xint = Read<uint>();
                uint yint = Read<uint>();
                uint zint = Read<uint>();
                xint = BinaryPrimitives.ReverseEndianness(xint);
                yint = BinaryPrimitives.ReverseEndianness(yint);
                zint = BinaryPrimitives.ReverseEndianness(zint);
                float x = BitConverter.UInt32BitsToSingle(xint);
                float y = BitConverter.UInt32BitsToSingle(yint);
                float z = BitConverter.UInt32BitsToSingle(zint);
                return new Vector3(x, y, z);
            }
            else
            {
                return Read<Vector3>();
            }
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector3"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Vector3"/>.</returns>
        public unsafe ReadOnlySpan<Vector3> ReadVector3Span(int count)
        {
            if (IsEndiannessReversed)
            {
                var span = ReadUInt32Span(count * 3);
                fixed (uint* pf = &span[0])
                {
                    return new ReadOnlySpan<Vector3>(pf, count);
                }
            }
            else
            {
                return ReadSpan<Vector3>(count);
            }
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Vector3"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector3"/>.</returns>
        public unsafe Vector3[] ReadVector3s(int count)
        {
            if (IsEndiannessReversed)
            {
                var span = ReadUInt32Span(count * 3);
                fixed (uint* pf = &span[0])
                {
                    return new Span<Vector3>(pf, count).ToArray();
                }
            }
            else
            {
                return ReadSpan<Vector3>(count).ToArray();
            }
        }

        /// <summary>
        /// Gets a <see cref="Vector3"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Vector3"/>.</returns>
        public Vector3 GetVector3(int position)
        {
            if (IsEndiannessReversed)
            {
                uint xint = Get<uint>(position);
                uint yint = Get<uint>(position + sizeof(float));
                uint zint = Get<uint>(position + sizeof(float) + sizeof(float));
                xint = BinaryPrimitives.ReverseEndianness(xint);
                yint = BinaryPrimitives.ReverseEndianness(yint);
                zint = BinaryPrimitives.ReverseEndianness(zint);
                float x = BitConverter.UInt32BitsToSingle(xint);
                float y = BitConverter.UInt32BitsToSingle(yint);
                float z = BitConverter.UInt32BitsToSingle(zint);
                return new Vector3(x, y, z);
            }
            else
            {
                return Get<Vector3>(position);
            }
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector3"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Vector3"/>.</returns>
        public unsafe ReadOnlySpan<Vector3> GetVector3Span(int position, int count)
        {
            if (IsEndiannessReversed)
            {
                var span = GetUInt32Span(position, count * 3);
                fixed (uint* pf = &span[0])
                {
                    return new ReadOnlySpan<Vector3>(pf, count);
                }
            }
            else
            {
                return GetSpan<Vector3>(position, count);
            }
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Vector3"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector3"/>.</returns>
        public unsafe Vector3[] GetVector3s(int position, int count)
        {
            if (IsEndiannessReversed)
            {
                var span = GetUInt32Span(position, count * 3);
                fixed (uint* pf = &span[0])
                {
                    return new Span<Vector3>(pf, count).ToArray();
                }
            }
            else
            {
                return GetSpan<Vector3>(position, count).ToArray();
            }
        }

        #endregion

        #region Vector4

        /// <summary>
        /// Reads a <see cref="Vector4"/>.
        /// </summary>
        /// <returns>A <see cref="Vector4"/>.</returns>
        public Vector4 ReadVector4()
        {
            if (IsEndiannessReversed)
            {
                uint xint = Read<uint>();
                uint yint = Read<uint>();
                uint zint = Read<uint>();
                uint wint = Read<uint>();
                xint = BinaryPrimitives.ReverseEndianness(xint);
                yint = BinaryPrimitives.ReverseEndianness(yint);
                zint = BinaryPrimitives.ReverseEndianness(zint);
                wint = BinaryPrimitives.ReverseEndianness(wint);
                float x = BitConverter.UInt32BitsToSingle(xint);
                float y = BitConverter.UInt32BitsToSingle(yint);
                float z = BitConverter.UInt32BitsToSingle(zint);
                float w = BitConverter.UInt32BitsToSingle(wint);
                return new Vector4(x, y, z, w);
            }
            else
            {
                return Read<Vector4>();
            }
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector4"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Vector4"/>.</returns>
        public unsafe ReadOnlySpan<Vector4> ReadVector4Span(int count)
        {
            if (IsEndiannessReversed)
            {
                var span = ReadUInt32Span(count * 4);
                fixed (uint* pf = &span[0])
                {
                    return new ReadOnlySpan<Vector4>(pf, count);
                }
            }
            else
            {
                return ReadSpan<Vector4>(count);
            }
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Vector4"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector4"/>.</returns>
        public unsafe Vector4[] ReadVector4s(int count)
        {
            if (IsEndiannessReversed)
            {
                var span = ReadUInt32Span(count * 4);
                fixed (uint* pf = &span[0])
                {
                    return new Span<Vector4>(pf, count).ToArray();
                }
            }
            else
            {
                return ReadSpan<Vector4>(count).ToArray();
            }
        }

        /// <summary>
        /// Gets a <see cref="Vector4"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Vector4"/>.</returns>
        public Vector4 GetVector4(int position)
        {
            if (IsEndiannessReversed)
            {
                uint xint = Get<uint>(position);
                uint yint = Get<uint>(position + sizeof(float));
                uint zint = Get<uint>(position + sizeof(float) + sizeof(float));
                uint wint = Get<uint>(position + sizeof(float) + sizeof(float) + sizeof(float));
                xint = BinaryPrimitives.ReverseEndianness(xint);
                yint = BinaryPrimitives.ReverseEndianness(yint);
                zint = BinaryPrimitives.ReverseEndianness(zint);
                wint = BinaryPrimitives.ReverseEndianness(wint);
                float x = BitConverter.UInt32BitsToSingle(xint);
                float y = BitConverter.UInt32BitsToSingle(yint);
                float z = BitConverter.UInt32BitsToSingle(zint);
                float w = BitConverter.UInt32BitsToSingle(wint);
                return new Vector4(x, y, z, w);
            }
            else
            {
                return Get<Vector4>(position);
            }
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="Vector4"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Vector4"/>.</returns>
        public unsafe ReadOnlySpan<Vector4> GetVector4Span(int position, int count)
        {
            if (IsEndiannessReversed)
            {
                var span = GetUInt32Span(position, count * 4);
                fixed (uint* pf = &span[0])
                {
                    return new ReadOnlySpan<Vector4>(pf, count);
                }
            }
            else
            {
                return GetSpan<Vector4>(position, count);
            }
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Vector4"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector4"/>.</returns>
        public unsafe Vector4[] GetVector4s(int position, int count)
        {
            if (IsEndiannessReversed)
            {
                var span = GetUInt32Span(position, count * 4);
                fixed (uint* pf = &span[0])
                {
                    return new Span<Vector4>(pf, count).ToArray();
                }
            }
            else
            {
                return GetSpan<Vector4>(position, count).ToArray();
            }
        }

        #endregion

        #region Quaternion

        /// <summary>
        /// Reads a <see cref="Quaternion"/>.
        /// </summary>
        /// <returns>A <see cref="Quaternion"/>.</returns>
        public Quaternion ReadQuaternion()
        {
            if (IsEndiannessReversed)
            {
                uint xint = Read<uint>();
                uint yint = Read<uint>();
                uint zint = Read<uint>();
                uint wint = Read<uint>();
                xint = BinaryPrimitives.ReverseEndianness(xint);
                yint = BinaryPrimitives.ReverseEndianness(yint);
                zint = BinaryPrimitives.ReverseEndianness(zint);
                wint = BinaryPrimitives.ReverseEndianness(wint);
                float x = BitConverter.UInt32BitsToSingle(xint);
                float y = BitConverter.UInt32BitsToSingle(yint);
                float z = BitConverter.UInt32BitsToSingle(zint);
                float w = BitConverter.UInt32BitsToSingle(wint);
                return new Quaternion(x, y, z, w);
            }
            else
            {
                return Read<Quaternion>();
            }
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="Quaternion"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Quaternion"/>.</returns>
        public unsafe ReadOnlySpan<Quaternion> ReadQuaternionSpan(int count)
        {
            if (IsEndiannessReversed)
            {
                var span = ReadUInt32Span(count * 4);
                fixed (uint* pf = &span[0])
                {
                    return new ReadOnlySpan<Quaternion>(pf, count);
                }
            }
            else
            {
                return ReadSpan<Quaternion>(count);
            }
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Quaternion"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Quaternion"/>.</returns>
        public unsafe Quaternion[] ReadQuaternions(int count)
        {
            if (IsEndiannessReversed)
            {
                var span = ReadUInt32Span(count * 4);
                fixed (uint* pf = &span[0])
                {
                    return new Span<Quaternion>(pf, count).ToArray();
                }
            }
            else
            {
                return ReadSpan<Quaternion>(count).ToArray();
            }
        }

        /// <summary>
        /// Gets a <see cref="Quaternion"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Quaternion"/>.</returns>
        public Quaternion GetQuaternion(int position)
        {
            if (IsEndiannessReversed)
            {
                uint xint = Get<uint>(position);
                uint yint = Get<uint>(position + sizeof(float));
                uint zint = Get<uint>(position + sizeof(float) + sizeof(float));
                uint wint = Get<uint>(position + sizeof(float) + sizeof(float) + sizeof(float));
                xint = BinaryPrimitives.ReverseEndianness(xint);
                yint = BinaryPrimitives.ReverseEndianness(yint);
                zint = BinaryPrimitives.ReverseEndianness(zint);
                wint = BinaryPrimitives.ReverseEndianness(wint);
                float x = BitConverter.UInt32BitsToSingle(xint);
                float y = BitConverter.UInt32BitsToSingle(yint);
                float z = BitConverter.UInt32BitsToSingle(zint);
                float w = BitConverter.UInt32BitsToSingle(wint);
                return new Quaternion(x, y, z, w);
            }
            else
            {
                return Get<Quaternion>(position);
            }
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="Quaternion"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="Quaternion"/>.</returns>
        public unsafe ReadOnlySpan<Quaternion> GetQuaternionSpan(int position, int count)
        {
            if (IsEndiannessReversed)
            {
                var span = GetUInt32Span(position, count * 4);
                fixed (uint* pf = &span[0])
                {
                    return new ReadOnlySpan<Quaternion>(pf, count);
                }
            }
            else
            {
                return GetSpan<Quaternion>(position, count);
            }
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Quaternion"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Quaternion"/>.</returns>
        public unsafe Quaternion[] GetQuaternions(int position, int count)
        {
            if (IsEndiannessReversed)
            {
                var span = GetUInt32Span(position, count * 4);
                fixed (uint* pf = &span[0])
                {
                    return new Span<Quaternion>(pf, count).ToArray();
                }
            }
            else
            {
                return GetSpan<Quaternion>(position, count).ToArray();
            }
        }

        #endregion

        #region Color

        public Color ReadRgba()
        {
            var span = ReadSpan<byte>(4);
            byte r = span[0];
            byte g = span[1];
            byte b = span[2];
            byte a = span[3];
            return Color.FromArgb(a, r, g, b);
        }

        public Color ReadArgb()
        {
            var span = ReadSpan<byte>(4);
            byte a = span[0];
            byte r = span[1];
            byte g = span[2];
            byte b = span[3];
            return Color.FromArgb(a, r, g, b);
        }

        public Color ReadBgra()
        {
            var span = ReadSpan<byte>(4);
            byte b = span[0];
            byte g = span[1];
            byte r = span[2];
            byte a = span[3];
            return Color.FromArgb(a, r, g, b);
        }

        public Color ReadAbgr()
        {
            var span = ReadSpan<byte>(4);
            byte a = span[0];
            byte b = span[1];
            byte g = span[2];
            byte r = span[3];
            return Color.FromArgb(a, r, g, b);
        }

        public Color ReadRgb()
        {
            var span = ReadSpan<byte>(3);
            byte r = span[0];
            byte g = span[1];
            byte b = span[2];
            return Color.FromArgb(r, g, b);
        }

        public Color ReadBgr()
        {
            var span = ReadSpan<byte>(3);
            byte b = span[0];
            byte g = span[1];
            byte r = span[2];
            return Color.FromArgb(r, g, b);
        }

        #endregion

        #region Pattern

        /// <summary>
        /// Asserts the specified length of bytes is the specified value.
        /// </summary>
        /// <param name="length">The length to assert.</param>
        /// <param name="pattern">The value to assert.</param>
        /// <exception cref="InvalidDataException">The assertion failed.</exception>
        public void AssertPattern(int length, byte pattern)
        {
            var bytes = ReadByteSpan(length);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != pattern)
                {
                    throw new InvalidDataException($"Expected {length} 0x{pattern:X2}, got {bytes[i]:X2} at position {i}");
                }
            }
        }

        #endregion

        #region String 8-Bit

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> representing an 8-bit null-terminated <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Read8BitStringSpan()
        {
            var span = Buffer.Span;
            span = span[BufferOffset..];
            int strLen = StringLengthHelper.Strlen(span);
            span = span[..strLen];
            BufferOffset += strLen;
            if (Remaining > 0)
                BufferOffset += 1; // Skip terminator
            return span;
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> representing an 8-bit <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Read8BitStringSpan(int length)
        {
            ValidateLength(length);
            var span = Buffer.Span;
            span = span[BufferOffset..length];
            BufferOffset += length;
            return span;
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> representing an 8-bit null-terminated <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Get8BitStringSpan(int position)
        {
            ValidatePosition(position);
            var span = Buffer.Span;
            span = span[position..];
            return span[..StringLengthHelper.Strlen(span)];
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> representing an 8-bit <see cref="string"/> in a fixed-size field at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Get8BitStringSpan(int position, int length)
        {
            ValidateArguments(position, length);
            return Buffer.Span[position..length];
        }

        #endregion

        #region String 16-Bit

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> representing a 16-bit null-terminated <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Read16BitStringSpan()
        {
            var span = Buffer.Span;
            span = span[BufferOffset..];
            int strLen = StringLengthHelper.WStrlen(span);
            span = span[..strLen];
            BufferOffset += strLen;
            if (Remaining > 1)
                BufferOffset += 2; // Skip terminator
            return span;
        }

        /// <summary>
        /// Reads a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> representing a 16-bit <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Read16BitStringSpan(int length)
        {
            ValidateLength(length);
            var span = Buffer.Span;
            span = span[BufferOffset..length];
            BufferOffset += length;
            return span;
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> representing a 16-bit null-terminated <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Get16BitStringSpan(int position)
        {
            ValidatePosition(position);
            var span = Buffer.Span;
            span = span[position..];
            return span[..StringLengthHelper.WStrlen(span)];
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> representing a 16-bit <see cref="string"/> in a fixed-size field at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Get16BitStringSpan(int position, int length)
        {
            ValidateArguments(position, length);
            return Buffer.Span[position..length];
        }

        #endregion

        #region String ASCII

        /// <summary>
        /// Reads a null-terminated ASCII encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadASCII()
            => Encoding.ASCII.GetString(Read8BitStringSpan());

        /// <summary>
        /// Reads a ASCII encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadASCII(int length)
            => Encoding.ASCII.GetString(Read8BitStringSpan(length));

        /// <summary>
        /// Gets a null-terminated ASCII encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetASCII(int position)
            => Encoding.ASCII.GetString(Get8BitStringSpan(position));

        /// <summary>
        /// Gets a ASCII encoded <see cref="string"/> in a fixed-size field at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetASCII(int position, int length)
            => Encoding.ASCII.GetString(Get8BitStringSpan(position, length));

        /// <summary>
        /// Reads a ASCII encoded <see cref="string"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertASCII(string option)
            => AssertHelper.Assert(ReadASCII(option.Length), "ASCII", option);

        /// <summary>
        /// Reads a ASCII encoded <see cref="string"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertASCII(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadASCII(options[0].Length), "ASCII", options);

        #endregion

        #region String UTF8

        /// <summary>
        /// Reads a null-terminated UTF8 encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF8()
            => Encoding.UTF8.GetString(Read8BitStringSpan());

        /// <summary>
        /// Reads a UTF8 encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF8(int length)
            => Encoding.UTF8.GetString(Read8BitStringSpan(length));

        /// <summary>
        /// Gets a null-terminated UTF8 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF8(int position)
            => Encoding.UTF8.GetString(Get8BitStringSpan(position));

        /// <summary>
        /// Gets a UTF8 encoded <see cref="string"/> in a fixed-size field at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF8(int position, int length)
            => Encoding.UTF8.GetString(Get8BitStringSpan(position, length));

        /// <summary>
        /// Reads a UTF8 encoded <see cref="string"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertUTF8(string option)
            => AssertHelper.Assert(ReadUTF8(option.Length), "UTF8", option);

        /// <summary>
        /// Reads a UTF8 encoded <see cref="string"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertUTF8(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadUTF8(options[0].Length), "UTF8", options);

        #endregion

        #region String ShiftJIS

        /// <summary>
        /// Reads a null-terminated ShiftJIS encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadShiftJIS()
            => EncodingHelper.ShiftJIS.GetString(Read8BitStringSpan());

        /// <summary>
        /// Reads a ShiftJIS encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadShiftJIS(int length)
            => EncodingHelper.ShiftJIS.GetString(Read8BitStringSpan(length));

        /// <summary>
        /// Gets a null-terminated ShiftJIS encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetShiftJIS(int position)
            => EncodingHelper.ShiftJIS.GetString(Get8BitStringSpan(position));

        /// <summary>
        /// Gets a ShiftJIS encoded <see cref="string"/> in a fixed-size field at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetShiftJIS(int position, int length)
            => EncodingHelper.ShiftJIS.GetString(Get8BitStringSpan(position, length));

        /// <summary>
        /// Reads a ShiftJIS encoded <see cref="string"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertShiftJIS(string option)
            => AssertHelper.Assert(ReadShiftJIS(option.Length), "ShiftJIS", option);

        /// <summary>
        /// Reads a ShiftJIS encoded <see cref="string"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertShiftJIS(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadShiftJIS(options[0].Length), "ShiftJIS", options);

        #endregion

        #region String UTF16

        /// <summary>
        /// Reads a null-terminated UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16()
            => BigEndian
            ? EncodingHelper.UTF16BE.GetString(Read16BitStringSpan())
            : EncodingHelper.UTF16LE.GetString(Read16BitStringSpan());

        /// <summary>
        /// Reads a UTF16 encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16(int length)
            => BigEndian
            ? EncodingHelper.UTF16BE.GetString(Read16BitStringSpan(length))
            : EncodingHelper.UTF16LE.GetString(Read16BitStringSpan(length));

        /// <summary>
        /// Gets a null-terminated UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16(int position)
            => BigEndian
            ? EncodingHelper.UTF16BE.GetString(Get16BitStringSpan(position))
            : EncodingHelper.UTF16LE.GetString(Get16BitStringSpan(position));

        /// <summary>
        /// Gets a UTF16 encoded <see cref="string"/> in a fixed-size field at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16(int position, int length)
            => BigEndian
            ? EncodingHelper.UTF16BE.GetString(Get16BitStringSpan(position, length))
            : EncodingHelper.UTF16LE.GetString(Get16BitStringSpan(position, length));

        /// <summary>
        /// Reads a UTF16 encoded <see cref="string"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertUTF16(string option)
            => AssertHelper.Assert(ReadUTF16(option.Length), "UTF16", option);

        /// <summary>
        /// Reads a UTF16 encoded <see cref="string"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertUTF16(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadUTF16(options[0].Length), "UTF16", options);

        #endregion

        #region String UTF16 Big Endian

        /// <summary>
        /// Reads a null-terminated big-endian UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16BigEndian()
            => EncodingHelper.UTF16BE.GetString(Read16BitStringSpan());

        /// <summary>
        /// Reads a big-endian UTF16 encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16BigEndian(int length)
            => EncodingHelper.UTF16BE.GetString(Read16BitStringSpan(length));

        /// <summary>
        /// Gets a null-terminated big-endian UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16BigEndian(int position)
            => EncodingHelper.UTF16BE.GetString(Get16BitStringSpan(position));

        /// <summary>
        /// Gets a big-endian UTF16 encoded <see cref="string"/> in a fixed-size field at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16BigEndian(int position, int length)
            => EncodingHelper.UTF16BE.GetString(Get16BitStringSpan(position, length));

        /// <summary>
        /// Reads a big-endian UTF16 encoded <see cref="string"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertUTF16BigEndian(string option)
            => AssertHelper.Assert(ReadUTF16BigEndian(option.Length), "UTF16BE", option);

        /// <summary>
        /// Reads a big-endian UTF16 encoded <see cref="string"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertUTF16BigEndian(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadUTF16BigEndian(options[0].Length), "UTF16BE", options);

        #endregion

        #region String UTF16 Little Endian

        /// <summary>
        /// Reads a null-terminated little-endian UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16LittleEndian()
            => EncodingHelper.UTF16LE.GetString(Read16BitStringSpan());

        /// <summary>
        /// Reads a little-endian UTF16 encoded <see cref="string"/> in a fixed-size field.
        /// </summary>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16LittleEndian(int length)
            => EncodingHelper.UTF16LE.GetString(Read16BitStringSpan(length));

        /// <summary>
        /// Gets a null-terminated little-endian UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16LittleEndian(int position)
            => EncodingHelper.UTF16LE.GetString(Get16BitStringSpan(position));

        /// <summary>
        /// Gets a little-endian UTF16 encoded <see cref="string"/> in a fixed-size field at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The byte length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16LittleEndian(int position, int length)
            => EncodingHelper.UTF16LE.GetString(Get16BitStringSpan(position, length));

        /// <summary>
        /// Reads a little-endian UTF16 encoded <see cref="string"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertUTF16LittleEndian(string option)
            => AssertHelper.Assert(ReadUTF16LittleEndian(option.Length), "UTF16LE", option);

        /// <summary>
        /// Reads a little-endian UTF16 encoded <see cref="string"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the <see cref="string"/> as.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string AssertUTF16LittleEndian(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadUTF16LittleEndian(options[0].Length), "UTF16LE", options);

        #endregion
    }
}
