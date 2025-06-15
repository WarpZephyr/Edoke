using Edoke.Helpers;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Edoke.IO
{
    /// <summary>
    /// A binary reader for streams supporting endianness.
    /// </summary>
    public class BinaryStreamReader : IBinaryReader, IDisposable, IAsyncDisposable
    {
        #region Constants

        /// <summary>
        /// The default buffer capacity for reading null-terminated 8-Bit strings.
        /// </summary>
        private const int StringDefaultCapacity = 16;

        /// <summary>
        /// The default buffer capacity for reading null-terminated 16-Bit strings.
        /// </summary>
        private const int WStringDefaultCapacity = 32;

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
        /// The underlying <see cref="Stream"/>.
        /// </summary>
        private readonly Stream InternalStream;

        /// <summary>
        /// The underlying <see cref="BinaryReader"/> assisting in reading.
        /// </summary>
        private readonly BinaryReader Reader;

        /// <summary>
        /// A jump stack for step-ins.
        /// </summary>
        private readonly Stack<long> Steps;

        /// <summary>
        /// The stream buffer for when the byte array constructor is used.
        /// </summary>
        private readonly byte[] StreamBuffer;

        /// <summary>
        /// Whether or not the stream buffer is available.
        /// </summary>
        private readonly bool HasStreamBuffer;

        /// <summary>
        /// Whether or not endianness is reversed.
        /// </summary>
        private bool IsEndiannessReversed;

        /// <summary>
        /// The backing field for <see cref="BigEndian"/>.
        /// </summary>
        private bool BigEndianField;

        /// <summary>
        /// Whether or not this <see cref="BinaryStreamReader"/> has been disposed.
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
                IsEndiannessReversed = value != !BitConverter.IsLittleEndian;
                BigEndianField = value;
            }
        }

        /// <summary>
        /// The current position of the reader.
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
        /// The depth of steps on the reader.
        /// </summary>
        public int StepDepth
            => Steps.Count;

        /// <summary>
        /// Whether or not this <see cref="BinaryStreamReader"/> has been disposed.
        /// </summary>
        public bool IsDisposed
            => disposedValue;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new <see cref="BinaryStreamReader"/> from the specified options.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read.</param>
        /// <param name="bigEndian">Whether or not to read in big endian.</param>
        /// <param name="leaveOpen">Whether or not to leave the <see cref="Stream"/> open when disposing.</param>
        public BinaryStreamReader(Stream stream, bool bigEndian, bool leaveOpen)
        {
            StreamBuffer = [];
            HasStreamBuffer = false;
            BigEndian = bigEndian;
            InternalStream = stream;
            Steps = [];
            Reader = new BinaryReader(stream, Encoding.Default, leaveOpen);
        }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamReader"/> from the specified options.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read.</param>
        /// <param name="bigEndian">Whether or not to read in big endian.</param>
        public BinaryStreamReader(Stream stream, bool bigEndian) : this(stream, bigEndian, false) { }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamReader"/> reading the specified <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read.</param>
        public BinaryStreamReader(Stream stream) : this(stream, !BitConverter.IsLittleEndian, false) { }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamReader"/> from the specified options.
        /// </summary>
        /// <param name="bytes">A <see cref="byte"/> <see cref="Array"/> to read from.</param>
        /// <param name="bigEndian">Whether or not to read in big endian.</param>
        public BinaryStreamReader(byte[] bytes, bool bigEndian)
        {
            StreamBuffer = bytes;
            HasStreamBuffer = true;
            BigEndian = bigEndian;
            InternalStream = new MemoryStream(bytes, false);
            Steps = [];
            Reader = new BinaryReader(InternalStream, Encoding.Default, false);
        }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamReader"/> reading the specified <see cref="byte"/> <see cref="Array"/>.
        /// </summary>
        /// <param name="bytes">A <see cref="byte"/> <see cref="Array"/> to read from.</param>
        public BinaryStreamReader(byte[] bytes) : this(bytes, !BitConverter.IsLittleEndian) { }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamReader"/> from the specified options.
        /// </summary>
        /// <param name="path">The path to a file to read from.</param>
        /// <param name="bigEndian">Whether or not to read in big endian.</param>
        public BinaryStreamReader(string path, bool bigEndian) : this(File.OpenRead(path), bigEndian) { }

        /// <summary>
        /// Creates a new <see cref="BinaryStreamReader"/> reading a file from the specified path.
        /// </summary>
        /// <param name="path">The path to a file to read from.</param>
        public BinaryStreamReader(string path) : this(File.OpenRead(path), !BitConverter.IsLittleEndian) { }

        #endregion

        #region Step

        /// <summary>
        /// Store the current position of the <see cref="Stream"/> on a stack, then move to the specified offset.
        /// </summary>
        public void StepIn(long offset)
        {
            Steps.Push(InternalStream.Position);
            InternalStream.Position = offset;
        }

        /// <summary>
        /// Restore the previous position of the <see cref="Stream"/> from a stack.
        /// </summary>
        public void StepOut()
        {
            if (Steps.Count == 0)
            {
                throw new InvalidOperationException("Reader is already stepped all the way out.");
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

            long remainder = Position % alignment;
            if (remainder > 0)
            {
                long finalPosition = checked(Position + (alignment - remainder));
                Position = finalPosition;
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
                Position = finalPosition;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// Attempt to read directly from the <see cref="Stream"/> buffer if it exists.
        /// </summary>
        /// <param name="buffer">The buffer to read into if the <see cref="Stream"/> buffer is not available.</param>
        /// <returns>A <see cref="byte"/> <see cref="ReadOnlySpan{T}"/>.</returns>
        /// <exception cref="EndOfStreamException">The requested read went beyond the end of the <see cref="Stream"/>.</exception>
        private ReadOnlySpan<byte> ReadDirect(Span<byte> buffer)
        {
            if (HasStreamBuffer)
            {
                int position = (int)InternalStream.Position;
                int next = position + buffer.Length;
                if (next > StreamBuffer.Length)
                {
                    throw new EndOfStreamException("Cannot read beyond the end of the stream.");
                }

                buffer = StreamBuffer.AsSpan(position, buffer.Length);
                InternalStream.Position = next;
                return buffer;
            }
            else
            {
                InternalStream.ReadExactly(buffer);
                return buffer;
            }
        }

        /// <summary>
        /// Attempt to read directly from the <see cref="Stream"/> buffer if it exists.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>A <see cref="byte"/> <see cref="ReadOnlySpan{T}"/>.</returns>
        /// <exception cref="EndOfStreamException">The requested read went beyond the end of the <see cref="Stream"/>.</exception>
        private ReadOnlySpan<byte> ReadDirect(int count)
        {
            if (HasStreamBuffer)
            {
                int position = (int)InternalStream.Position;
                int next = position + count;
                if (next > StreamBuffer.Length)
                {
                    throw new EndOfStreamException("Cannot read beyond the end of the stream.");
                }

                var buffer = StreamBuffer.AsSpan(position, count);
                InternalStream.Position = next;
                return buffer;
            }
            else
            {
                return Reader.ReadBytes(count);
            }
        }

        #endregion

        #region SByte

        /// <summary>
        /// Reads an <see cref="sbyte"/>.
        /// </summary>
        /// <returns>An <see cref="sbyte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte ReadSByte()
            => Reader.ReadSByte();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="sbyte"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="sbyte"/>.</returns>
        public sbyte[] ReadSBytes(int count)
        {
            var values = new sbyte[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = Reader.ReadSByte();
            }
            return values;
        }

        /// <summary>
        /// Gets an <see cref="sbyte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>An <see cref="sbyte"/>.</returns>
        public sbyte GetSByte(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadSByte();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="sbyte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="sbyte"/>.</returns>
        public sbyte[] GetSBytes(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadSBytes(count);
            Position = origPos;
            return values;
        }

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
            => Reader.ReadByte();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="byte"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="byte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[] ReadBytes(int count)
            => Reader.ReadBytes(count);

        /// <summary>
        /// Reads to fill the specified buffer.
        /// </summary>
        /// <param name="buffer">The buffer to fill.</param>
        public void ReadBytes(Span<byte> buffer)
            => BaseStream.ReadExactly(buffer);

        /// <summary>
        /// Reads to fill the specified buffer with the specified number of bytes.
        /// </summary>
        /// <param name="buffer">The buffer to fill.</param>
        /// <param name="count">The number of bytes to fill.</param>
        public void ReadBytes(byte[] buffer, int count)
            => BaseStream.ReadExactly(buffer, 0, count);

        /// <summary>
        /// Reads to fill the specified buffer at the specified offset with the specified number of bytes.
        /// </summary>
        /// <param name="buffer">The buffer to fill.</param>
        /// <param name="offset">The offset to fill at in the buffer.</param>
        /// <param name="count">The number of bytes to fill.</param>
        public void ReadBytes(byte[] buffer, int offset, int count)
            => BaseStream.ReadExactly(buffer, offset, count);

        /// <summary>
        /// Gets a <see cref="byte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="byte"/>.</returns>
        public byte GetByte(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadByte();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="byte"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="byte"/>.</returns>
        public byte[] GetBytes(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadBytes(count);
            Position = origPos;
            return values;
        }

        /// <summary>
        /// Reads from the specified position to fill the specified buffer at the specified offset with the specified number of bytes.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="buffer">The buffer to fill.</param>
        /// <param name="offset">The offset to fill at in the buffer.</param>
        /// <param name="count">The number of bytes to fill.</param>
        /// <returns>An <see cref="Array"/> of <see cref="byte"/>.</returns>
        public void GetBytes(long position, byte[] buffer, int offset, int count)
        {
            long origPos = Position;
            Position = position;
            ReadBytes(buffer, offset, count);
            Position = origPos;
        }

        /// <summary>
        /// Reads from the specified position to fill the specified buffer with the specified number of bytes.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="buffer">The buffer to fill.</param>
        /// <param name="count">The number of bytes to fill.</param>
        /// <returns>An <see cref="Array"/> of <see cref="byte"/>.</returns>
        public void GetBytes(long position, byte[] buffer, int count)
        {
            long origPos = Position;
            Position = position;
            ReadBytes(buffer, 0, count);
            Position = origPos;
        }

        /// <summary>
        /// Reads from the specified position to fill the specified buffer with the specified number of bytes.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="buffer">The buffer to fill.</param>
        /// <returns>An <see cref="Array"/> of <see cref="byte"/>.</returns>
        public void GetBytes(long position, Span<byte> buffer)
        {
            long origPos = Position;
            Position = position;
            ReadBytes(buffer);
            Position = origPos;
        }

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
            ? BinaryPrimitives.ReverseEndianness(Reader.ReadInt16())
            : Reader.ReadInt16();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="short"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="short"/>.</returns>
        public short[] ReadInt16s(int count)
        {
            var values = new short[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReverseEndianness(Reader.ReadInt16());
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadInt16();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="short"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="short"/>.</returns>
        public short GetInt16(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadInt16();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="short"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="short"/>.</returns>
        public short[] GetInt16s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadInt16s(count);
            Position = origPos;
            return values;
        }

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
            ? BinaryPrimitives.ReverseEndianness(Reader.ReadUInt16())
            : Reader.ReadUInt16();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="ushort"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="ushort"/>.</returns>
        public ushort[] ReadUInt16s(int count)
        {
            var values = new ushort[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReverseEndianness(Reader.ReadUInt16());
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadUInt16();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="ushort"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="ushort"/>.</returns>
        public ushort GetUInt16(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadUInt16();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="ushort"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="ushort"/>.</returns>
        public ushort[] GetUInt16s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadUInt16s(count);
            Position = origPos;
            return values;
        }

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
            ? BinaryPrimitives.ReverseEndianness(Reader.ReadInt32())
            : Reader.ReadInt32();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="int"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="int"/>.</returns>
        public int[] ReadInt32s(int count)
        {
            var values = new int[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReverseEndianness(Reader.ReadInt32());
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadInt32();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets an <see cref="int"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>An <see cref="int"/>.</returns>
        public int GetInt32(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadInt32();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="int"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="int"/>.</returns>
        public int[] GetInt32s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadInt32s(count);
            Position = origPos;
            return values;
        }

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
        /// Reads a <see cref="uint"/>.
        /// </summary>
        /// <returns>A <see cref="uint"/>.</returns>
        public uint ReadUInt32()
            => IsEndiannessReversed
            ? BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32())
            : Reader.ReadUInt32();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="uint"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="uint"/>.</returns>
        public uint[] ReadUInt32s(int count)
        {
            var values = new uint[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32());
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadUInt32();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="uint"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="uint"/>.</returns>
        public uint GetUInt32(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadUInt32();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="uint"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="uint"/>.</returns>
        public uint[] GetUInt32s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadUInt32s(count);
            Position = origPos;
            return values;
        }

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
            ? BinaryPrimitives.ReverseEndianness(Reader.ReadInt64())
            : Reader.ReadInt64();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="long"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="long"/>.</returns>
        public long[] ReadInt64s(int count)
        {
            var values = new long[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReverseEndianness(Reader.ReadInt64());
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadInt64();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="long"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="long"/>.</returns>
        public long GetInt64(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadInt64();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="long"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="long"/>.</returns>
        public long[] GetInt64s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadInt64s(count);
            Position = origPos;
            return values;
        }

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
            ? BinaryPrimitives.ReverseEndianness(Reader.ReadUInt64())
            : Reader.ReadUInt64();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="ulong"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="ulong"/>.</returns>
        public ulong[] ReadUInt64s(int count)
        {
            var values = new ulong[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReverseEndianness(Reader.ReadUInt64());
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadUInt64();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="ulong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="ulong"/>.</returns>
        public ulong GetUInt64(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadUInt64();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="ulong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="ulong"/>.</returns>
        public ulong[] GetUInt64s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadUInt64s(count);
            Position = origPos;
            return values;
        }

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

        #region Half

        /// <summary>
        /// Reads a <see cref="Half"/>.
        /// </summary>
        /// <returns>A <see cref="Half"/>.</returns>
        public Half ReadHalf()
            => IsEndiannessReversed
            ? BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt16()))
            : Reader.ReadHalf();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Half"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Half"/>.</returns>
        public Half[] ReadHalfs(int count)
        {
            var values = new Half[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt16()));
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadHalf();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="Half"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Half"/>.</returns>
        public Half GetHalf(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadHalf();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Half"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Half"/>.</returns>
        public Half[] GetHalfs(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadHalfs(count);
            Position = origPos;
            return values;
        }

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
            ? BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()))
            : Reader.ReadSingle();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="float"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="float"/>.</returns>
        public float[] ReadSingles(int count)
        {
            var values = new float[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadSingle();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="float"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="float"/>.</returns>
        public float GetSingle(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadSingle();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="float"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="float"/>.</returns>
        public float[] GetSingles(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadSingles(count);
            Position = origPos;
            return values;
        }

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
            ? BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt64()))
            : Reader.ReadDouble();

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="double"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="double"/>.</returns>
        public double[] ReadDoubles(int count)
        {
            var values = new double[count];
            if (IsEndiannessReversed)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt64()));
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = Reader.ReadDouble();
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="double"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="double"/>.</returns>
        public double GetDouble(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadDouble();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="double"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="double"/>.</returns>
        public double[] GetDoubles(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadDoubles(count);
            Position = origPos;
            return values;
        }

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
        /// Reads a varint according to <see cref="VarintLong"/>.
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
        /// Reads an <see cref="Array"/> of varints according to <see cref="VarintLong"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>The read values.</returns>
        public long[] ReadVarints(int count)
        {
            long[] values = new long[count];
            if (VarintLong)
            {
                values = ReadInt64s(count);
            }
            else
            {
                if (IsEndiannessReversed)
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReverseEndianness(Reader.ReadInt32());
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = Reader.ReadInt32();
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Gets a varint according to <see cref="VarintLong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>The read value.</returns>
        public long GetVarint(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadVarint();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of varints according to <see cref="VarintLong"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to read.</param>
        /// <returns>The read values.</returns>
        public long[] GetVarints(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var value = ReadVarints(count);
            Position = origPos;
            return value;
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
            byte value = Reader.ReadByte();
            return value == 1 || (value == 0 ? false : throw new InvalidDataException($"Read boolean value was not 0 or 1: {value}"));
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="bool"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="bool"/>.</returns>
        public bool[] ReadBooleans(int count)
        {
            var values = new bool[count];
            for (int i = 0; i < count; i++)
            {
                byte value = Reader.ReadByte();
                values[i] = value == 1 || (value == 0 ? false : throw new InvalidDataException($"Read boolean value was not 0 or 1: {value}"));
            }
            return values;
        }

        /// <summary>
        /// Gets a <see cref="bool"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="bool"/>.</returns>
        public bool GetBoolean(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadBoolean();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="bool"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="bool"/>.</returns>
        public bool[] GetBooleans(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadBooleans(count);
            Position = origPos;
            return values;
        }

        /// <summary>
        /// Reads a <see cref="bool"/> and throws if it is not the specified option.
        /// </summary>
        /// <param name="option">The option to assert the value as.</param>
        /// <returns>A <see cref="bool"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AssertBoolean(bool option)
            => AssertHelper.Assert(ReadBoolean(), nameof(Boolean), BooleanFormat, option);

        /// <summary>
        /// Reads a <see cref="bool"/> and throws if it is not one of the specified options.
        /// </summary>
        /// <param name="options">The options to assert the value as.</param>
        /// <returns>A <see cref="bool"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            => AssertHelper.AssertEnum<TEnum, sbyte>(ReadSByte());

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
            float x;
            float y;

            if (IsEndiannessReversed)
            {
                x = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                y = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
            }
            else
            {
                x = Reader.ReadSingle();
                y = Reader.ReadSingle();
            }

            return new Vector2(x, y);
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Vector2"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector2"/>.</returns>
        public unsafe Vector2[] ReadVector2s(int count)
        {
            var values = new Vector2[count];
            var span = new Span<Vector2>(values);

            fixed (Vector2* pf = &span[0])
            {
                var byteSpan = new Span<byte>(pf, count << 3);
                InternalStream.ReadExactly(byteSpan);

                if (IsEndiannessReversed)
                {
                    int reverseCount = count << 1;
                    var uintSpan = new Span<uint>(pf, reverseCount);
                    for (int i = 0; i < reverseCount; i++)
                    {
                        uintSpan[i] = BinaryPrimitives.ReverseEndianness(uintSpan[i]);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Gets a <see cref="Vector2"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Vector2"/>.</returns>
        public Vector2 GetVector2(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadVector2();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Vector2"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector2"/>.</returns>
        public Vector2[] GetVector2s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadVector2s(count);
            Position = origPos;
            return values;
        }

        #endregion

        #region Vector3

        /// <summary>
        /// Reads a <see cref="Vector3"/>.
        /// </summary>
        /// <returns>A <see cref="Vector3"/>.</returns>
        public Vector3 ReadVector3()
        {
            float x;
            float y;
            float z;

            if (IsEndiannessReversed)
            {
                x = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                y = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                z = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
            }
            else
            {
                x = Reader.ReadSingle();
                y = Reader.ReadSingle();
                z = Reader.ReadSingle();
            }

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Vector3"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector3"/>.</returns>
        public unsafe Vector3[] ReadVector3s(int count)
        {
            var values = new Vector3[count];
            var span = new Span<Vector3>(values);

            fixed (Vector3* pf = &span[0])
            {
                var byteSpan = new Span<byte>(pf, count * 12);
                InternalStream.ReadExactly(byteSpan);

                if (IsEndiannessReversed)
                {
                    int reverseCount = count * 3;
                    var uintSpan = new Span<uint>(pf, reverseCount);
                    for (int i = 0; i < reverseCount; i++)
                    {
                        uintSpan[i] = BinaryPrimitives.ReverseEndianness(uintSpan[i]);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Gets a <see cref="Vector3"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Vector3"/>.</returns>
        public Vector3 GetVector3(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadVector3();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Vector3"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector3"/>.</returns>
        public Vector3[] GetVector3s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadVector3s(count);
            Position = origPos;
            return values;
        }

        #endregion

        #region Vector4

        /// <summary>
        /// Reads a <see cref="Vector4"/>.
        /// </summary>
        /// <returns>A <see cref="Vector4"/>.</returns>
        public Vector4 ReadVector4()
        {
            float x;
            float y;
            float z;
            float w;

            if (IsEndiannessReversed)
            {
                x = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                y = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                z = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                w = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
            }
            else
            {
                x = Reader.ReadSingle();
                y = Reader.ReadSingle();
                z = Reader.ReadSingle();
                w = Reader.ReadSingle();
            }

            return new Vector4(x, y, z, w);
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Vector4"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector4"/>.</returns>
        public unsafe Vector4[] ReadVector4s(int count)
        {
            var values = new Vector4[count];
            var span = new Span<Vector4>(values);

            fixed (Vector4* pf = &span[0])
            {
                var byteSpan = new Span<byte>(pf, count << 4);
                InternalStream.ReadExactly(byteSpan);

                if (IsEndiannessReversed)
                {
                    int reverseCount = count << 2;
                    var uintSpan = new Span<uint>(pf, reverseCount);
                    for (int i = 0; i < reverseCount; i++)
                    {
                        uintSpan[i] = BinaryPrimitives.ReverseEndianness(uintSpan[i]);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Gets a <see cref="Vector4"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Vector4"/>.</returns>
        public Vector4 GetVector4(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadVector4();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Vector4"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Vector4"/>.</returns>
        public Vector4[] GetVector4s(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadVector4s(count);
            Position = origPos;
            return values;
        }

        #endregion

        #region Quaternion

        /// <summary>
        /// Reads a <see cref="Quaternion"/>.
        /// </summary>
        /// <returns>A <see cref="Quaternion"/>.</returns>
        public Quaternion ReadQuaternion()
        {
            float x;
            float y;
            float z;
            float w;

            if (IsEndiannessReversed)
            {
                x = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                y = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                z = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
                w = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReverseEndianness(Reader.ReadUInt32()));
            }
            else
            {
                x = Reader.ReadSingle();
                y = Reader.ReadSingle();
                z = Reader.ReadSingle();
                w = Reader.ReadSingle();
            }

            return new Quaternion(x, y, z, w);
        }

        /// <summary>
        /// Reads an <see cref="Array"/> of <see cref="Quaternion"/>.
        /// </summary>
        /// <param name="count">The amount to read.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Quaternion"/>.</returns>
        public unsafe Quaternion[] ReadQuaternions(int count)
        {
            var values = new Quaternion[count];
            var span = new Span<Quaternion>(values);

            fixed (Quaternion* pf = &span[0])
            {
                var byteSpan = new Span<byte>(pf, count << 4);
                InternalStream.ReadExactly(byteSpan);

                if (IsEndiannessReversed)
                {
                    int reverseCount = count << 2;
                    var uintSpan = new Span<uint>(pf, reverseCount);
                    for (int i = 0; i < reverseCount; i++)
                    {
                        uintSpan[i] = BinaryPrimitives.ReverseEndianness(uintSpan[i]);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Gets a <see cref="Quaternion"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="Quaternion"/>.</returns>
        public Quaternion GetQuaternion(long position)
        {
            long origPos = Position;
            Position = position;
            var value = ReadQuaternion();
            Position = origPos;
            return value;
        }

        /// <summary>
        /// Gets an <see cref="Array"/> of <see cref="Quaternion"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="count">The amount to get.</param>
        /// <returns>An <see cref="Array"/> of <see cref="Quaternion"/>.</returns>
        public Quaternion[] GetQuaternions(long position, int count)
        {
            long origPos = Position;
            Position = position;
            var values = ReadQuaternions(count);
            Position = origPos;
            return values;
        }

        #endregion

        #region Color

        /// <summary>
        /// Read a <see cref="Color"/> in RGBA order.
        /// </summary>
        /// <returns>A <see cref="Color"/>.</returns>
        public Color ReadRgba()
        {
            byte r = Reader.ReadByte();
            byte g = Reader.ReadByte();
            byte b = Reader.ReadByte();
            byte a = Reader.ReadByte();
            return Color.FromArgb(a, r, g, b);
        }

        /// <summary>
        /// Read a <see cref="Color"/> in ARGB order.
        /// </summary>
        /// <returns>A <see cref="Color"/>.</returns>
        public Color ReadArgb()
            => Color.FromArgb(ReadInt32());

        /// <summary>
        /// Read a <see cref="Color"/> in BGRA order.
        /// </summary>
        /// <returns>A <see cref="Color"/>.</returns>
        public Color ReadBgra()
            => Color.FromArgb(BinaryPrimitives.ReverseEndianness(ReadInt32()));

        /// <summary>
        /// Read a <see cref="Color"/> in ABGR order.
        /// </summary>
        /// <returns>A <see cref="Color"/>.</returns>
        public Color ReadAbgr()
        {
            byte a = Reader.ReadByte();
            byte b = Reader.ReadByte();
            byte g = Reader.ReadByte();
            byte r = Reader.ReadByte();
            return Color.FromArgb(a, r, g, b);
        }

        /// <summary>
        /// Read a <see cref="Color"/> in RGB order.
        /// </summary>
        /// <returns>A <see cref="Color"/>.</returns>
        public Color ReadRgb()
        {
            byte r = Reader.ReadByte();
            byte g = Reader.ReadByte();
            byte b = Reader.ReadByte();
            return Color.FromArgb(r, g, b);
        }

        /// <summary>
        /// Read a <see cref="Color"/> in BGR order.
        /// </summary>
        /// <returns>A <see cref="Color"/>.</returns>
        public Color ReadBgr()
        {
            byte b = Reader.ReadByte();
            byte g = Reader.ReadByte();
            byte r = Reader.ReadByte();
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
            byte[] bytes = Reader.ReadBytes(length);
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
        /// Read a <see cref="byte"/> <see cref="Array"/> representing an 8-bit null-terminated <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="byte"/> <see cref="Array"/>.</returns>
        private ReadOnlySpan<byte> Read8BitStringSpan()
        {
            var list = new List<byte>(StringDefaultCapacity);

            byte b = Reader.ReadByte();
            while (b != 0)
            {
                list.Add(b);
                b = Reader.ReadByte();
            }

            return new ReadOnlySpan<byte>([.. list]);
        }

        /// <summary>
        /// Read a <see cref="Span{T}"/> of <see cref="byte"/> representing an 8-bit fixed-length <see cref="string"/>.
        /// </summary>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="Span{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Read8BitStringSpan(int length)
            => ReadDirect(length);

        /// <summary>
        /// Get a <see cref="byte"/> <see cref="Array"/> representing an 8-bit null-terminated <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="byte"/> <see cref="Array"/>.</returns>
        private ReadOnlySpan<byte> Get8BitStringSpan(long position)
        {
            long origPos = Position;
            Position = position;
            var values = Read8BitStringSpan();
            Position = origPos;
            return values;
        }

        /// <summary>
        /// Get a <see cref="Span{T}"/> of <see cref="byte"/> representing an 8-bit fixed-length <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="Span{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Get8BitStringSpan(long position, int length)
        {
            long origPos = Position;
            Position = position;
            var values = Read8BitStringSpan(length);
            Position = origPos;
            return values;
        }

        #endregion

        #region String 16-Bit

        /// <summary>
        /// Read a <see cref="byte"/> <see cref="Array"/> representing a 16-bit null-terminated <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="byte"/> <see cref="Array"/>.</returns>
        private ReadOnlySpan<byte> Read16BitStringSpan()
        {
            var buffer = new List<byte>(WStringDefaultCapacity);

            Span<byte> span = stackalloc byte[2];
            var unit = ReadDirect(span);
            while (unit[0] != 0 && unit[1] != 0)
            {
                buffer.AddRange(unit);
                unit = ReadDirect(span);
            }

            return new ReadOnlySpan<byte>([.. buffer]);
        }

        /// <summary>
        /// Read a <see cref="Span{T}"/> of <see cref="byte"/> representing a 16-bit fixed-length <see cref="string"/>.
        /// </summary>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="Span{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Read16BitStringSpan(int length)
            => ReadDirect(length);

        /// <summary>
        /// Get a <see cref="byte"/> <see cref="Array"/> representing a 16-bit null-terminated <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="byte"/> <see cref="Array"/>.</returns>
        private ReadOnlySpan<byte> Get16BitStringSpan(long position)
        {
            long origPos = Position;
            Position = position;
            var values = Read16BitStringSpan();
            Position = origPos;
            return values;
        }

        /// <summary>
        /// Get a <see cref="Span{T}"/> of <see cref="byte"/> representing a 16-bit fixed-length <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="Span{T}"/> of <see cref="byte"/>.</returns>
        private ReadOnlySpan<byte> Get16BitStringSpan(long position, int length)
        {
            long origPos = Position;
            Position = position;
            var values = Read16BitStringSpan(length);
            Position = origPos;
            return values;
        }

        #endregion

        #region String ASCII

        /// <summary>
        /// Read a null-terminated ASCII encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadASCII()
            => Encoding.ASCII.GetString(Read8BitStringSpan());

        /// <summary>
        /// Read a fixed-length ASCII encoded <see cref="string"/>.
        /// </summary>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadASCII(int length)
            => Encoding.ASCII.GetString(Read8BitStringSpan(length));

        /// <summary>
        /// Get a null-terminated ASCII encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetASCII(long position)
            => Encoding.ASCII.GetString(Get8BitStringSpan(position));

        /// <summary>
        /// Get a fixed-length ASCII encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetASCII(long position, int length)
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string AssertASCII(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadASCII(options[0].Length), "ASCII", options);

        #endregion

        #region String UTF8

        /// <summary>
        /// Read a null-terminated UTF8 encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF8()
            => Encoding.UTF8.GetString(Read8BitStringSpan());

        /// <summary>
        /// Read a fixed-length UTF8 encoded <see cref="string"/>.
        /// </summary>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF8(int length)
            => Encoding.UTF8.GetString(Read8BitStringSpan(length));

        /// <summary>
        /// Get a null-terminated UTF8 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF8(long position)
            => Encoding.UTF8.GetString(Get8BitStringSpan(position));

        /// <summary>
        /// Get a fixed-length UTF8 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF8(long position, int length)
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string AssertUTF8(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadUTF8(options[0].Length), "UTF8", options);

        #endregion

        #region String ShiftJIS

        /// <summary>
        /// Read a null-terminated ShiftJIS encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadShiftJIS()
            => EncodingHelper.ShiftJIS.GetString(Read8BitStringSpan());

        /// <summary>
        /// Read a fixed-length ShiftJIS encoded <see cref="string"/>.
        /// </summary>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadShiftJIS(int length)
            => EncodingHelper.ShiftJIS.GetString(Read8BitStringSpan(length));

        /// <summary>
        /// Get a null-terminated ShiftJIS encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetShiftJIS(long position)
            => EncodingHelper.ShiftJIS.GetString(Get8BitStringSpan(position));

        /// <summary>
        /// Get a fixed-length ShiftJIS encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The length of the fixed field.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetShiftJIS(long position, int length)
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string AssertShiftJIS(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadShiftJIS(options[0].Length), "ShiftJIS", options);

        #endregion

        #region String UTF16

        /// <summary>
        /// Read a null-terminated UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16()
            => BigEndian
            ? EncodingHelper.UTF16BE.GetString(Read16BitStringSpan())
            : EncodingHelper.UTF16LE.GetString(Read16BitStringSpan());

        /// <summary>
        /// Read a fixed-length UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <param name="length">The length of the fixed field in 16-bit chars.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16(int length)
            => BigEndian
            ? EncodingHelper.UTF16BE.GetString(Read16BitStringSpan(length))
            : EncodingHelper.UTF16LE.GetString(Read16BitStringSpan(length));

        /// <summary>
        /// Get a null-terminated UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16(long position)
            => BigEndian
            ? EncodingHelper.UTF16BE.GetString(Get16BitStringSpan(position))
            : EncodingHelper.UTF16LE.GetString(Get16BitStringSpan(position));

        /// <summary>
        /// Get a fixed-length UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The length of the fixed field in 16-bit chars.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16(long position, int length)
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string AssertUTF16(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadUTF16(options[0].Length), "UTF16", options);

        #endregion

        #region String UTF16 Big Endian

        /// <summary>
        /// Read a null-terminated big-endian UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16BigEndian()
            => EncodingHelper.UTF16BE.GetString(Read16BitStringSpan());

        /// <summary>
        /// Read a fixed-length big-endian UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <param name="length">The length of the fixed field in 16-bit chars.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16BigEndian(int length)
            => EncodingHelper.UTF16BE.GetString(Read16BitStringSpan(length));

        /// <summary>
        /// Get a null-terminated big-endian UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16BigEndian(long position)
            => EncodingHelper.UTF16BE.GetString(Get16BitStringSpan(position));

        /// <summary>
        /// Get a fixed-length big-endian UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The length of the fixed field in 16-bit chars.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16BigEndian(long position, int length)
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string AssertUTF16BigEndian(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadUTF16BigEndian(options[0].Length), "UTF16BE", options);

        #endregion

        #region String UTF16 Little Endian

        /// <summary>
        /// Read a null-terminated little-endian UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16LittleEndian()
            => EncodingHelper.UTF16LE.GetString(Read16BitStringSpan());

        /// <summary>
        /// Read a fixed-length little-endian UTF16 encoded <see cref="string"/>.
        /// </summary>
        /// <param name="length">The length of the fixed field in 16-bit chars.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string ReadUTF16LittleEndian(int length)
            => EncodingHelper.UTF16LE.GetString(Read16BitStringSpan(length));

        /// <summary>
        /// Get a null-terminated little-endian UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16LittleEndian(long position)
            => EncodingHelper.UTF16LE.GetString(Get16BitStringSpan(position));

        /// <summary>
        /// Get a fixed-length little-endian UTF16 encoded <see cref="string"/> at the specified position.
        /// </summary>
        /// <param name="position">The specified position.</param>
        /// <param name="length">The length of the fixed field in 16-bit chars.</param>
        /// <returns>A <see cref="string"/>.</returns>
        public string GetUTF16LittleEndian(long position, int length)
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string AssertUTF16LittleEndian(ReadOnlySpan<string> options)
            => AssertHelper.Assert(ReadUTF16LittleEndian(options[0].Length), "UTF16LE", options);

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
                    Reader.Dispose();
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

        public ValueTask DisposeAsync()
        {
            var task = InternalStream.DisposeAsync();
            GC.SuppressFinalize(this);
            return task;
        }

        #endregion
    }
}
