using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;

namespace Edoke.IO
{
    /// <summary>
    /// A zero-copy view on a <see cref="Stream"/>.
    /// </summary>
    public class SubStream : Stream
    {
        #region Constants

        /// <summary>
        /// The default size for when a buffer is required.
        /// </summary>
        private const int DefaultBufferSize = 4096;

        #endregion

        #region SubStream Members

        /// <summary>
        /// The <see cref="SubStream"/> position.
        /// </summary>
        private long _position;

        /// <summary>
        /// The offset into the <see cref="BaseStream"/> where this <see cref="SubStream"/> begins.
        /// </summary>
        private readonly long _offset;

        /// <summary>
        /// The <see cref="SubStream"/> length.
        /// </summary>
        private long _length;

        /// <summary>
        /// Whether or not to leave the <see cref="BaseStream"/> open when disposing.
        /// </summary>
        private readonly bool _leaveOpen;

        /// <summary>
        /// Whether or not this <see cref="SubStream"/> has been disposed.
        /// </summary>
        private bool disposedValue;

        /// <summary>
        /// The underlying <see cref="Stream"/>.
        /// </summary>
        public Stream BaseStream { get; init; }

        #endregion

        #region Stream Properties

        public override bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BaseStream.CanRead;
        }

        public override bool CanSeek
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BaseStream.CanSeek;
        }

        public override bool CanWrite
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BaseStream.CanWrite;
        }

        public override bool CanTimeout
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BaseStream.CanTimeout;
        }

        public override long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        public override long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _position;
            set
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((ulong)value, (ulong)_length, nameof(value));
                _position = value;
                BaseStream.Position = _offset + _position;
            }
        }

        public override int ReadTimeout
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BaseStream.ReadTimeout;
            set => throw new NotSupportedException($"Cannot set {nameof(ReadTimeout)} on a {nameof(SubStream)}.");
        }

        public override int WriteTimeout
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BaseStream.WriteTimeout;
            set => throw new NotSupportedException($"Cannot set {nameof(WriteTimeout)} on a {nameof(SubStream)}.");
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Create a new <see cref="SubStream"/> over a <see cref="Stream"/>.
        /// </summary>
        /// <param name="baseStream">The underlying <see cref="Stream"/>.</param>
        /// <param name="offset">The offset into the underlying <see cref="Stream"/> the <see cref="SubStream"/> begins at.</param>
        /// <param name="length">The length of the <see cref="SubStream"/>.</param>
        public SubStream(Stream baseStream, long offset, long length, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(baseStream);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((ulong)offset, (ulong)baseStream.Length, nameof(offset));
            ArgumentOutOfRangeException.ThrowIfGreaterThan((ulong)length, (ulong)baseStream.Length, nameof(length));

            BaseStream = baseStream;

            _offset = offset;
            _length = length;
            _leaveOpen = leaveOpen;
        }

        #endregion

        #region SubStream Helpers

        /// <summary>
        /// Seek until the <see cref="BaseStream"/> position is correct.
        /// </summary>
        private void SeekUntilPosition()
        {
            long finalPosition = _offset + _position;
            if (finalPosition == BaseStream.Position)
            {
                // We are already there
                return;
            }

            if (CanSeek)
            {
                // We can seek there
                BaseStream.Seek(finalPosition, SeekOrigin.Begin);
                return;
            }

            // Seek in unseekable stream
            ReadSeek(finalPosition);
        }

        /// <summary>
        /// Seek forward in an unseekable <see cref="Stream"/> by reading.
        /// </summary>
        /// <param name="position">The position to seek to.</param>
        /// <exception cref="NotSupportedException">Cannot seek in the given base <see cref="Stream"/>.</exception>
        /// <exception cref="Exception">Failed to seek by reading.</exception>
        private void ReadSeek(long position)
        {
            if (!CanRead)
            {
                // We cannot seek or read
                throw new NotSupportedException($"Cannot seek for read or write in an unseekable and unreadable {nameof(Stream)}.");
            }

            if (position < BaseStream.Position)
            {
                // We can only seek forward by reading but have already past it
                throw new NotSupportedException($"Cannot seek backwards for read or write in an unseekable {nameof(Stream)}.");
            }

            // We can seek forward by reading
            byte[] buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
            long remaining = position - BaseStream.Position;

            int remainder = (int)(remaining % DefaultBufferSize);
            long chunkCount = remaining / DefaultBufferSize;
            for (int i = 0; i < chunkCount; i++)
            {
                if (BaseStream.Read(buffer, 0, DefaultBufferSize) == 0)
                {
                    throw new Exception($"Failed to seek forward for read or write by reading in unseekable {nameof(Stream)}.");
                }
            }

            if (remainder > 0)
            {
                if (BaseStream.Read(buffer, 0, remainder) == 0)
                {
                    throw new Exception($"Failed to seek forward for read or write by reading in unseekable {nameof(Stream)}.");
                }
            }

            ArrayPool<byte>.Shared.Return(buffer);
        }

        #endregion

        #region Stream Methods

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!CanRead)
            {
                throw new NotSupportedException($"Underlying {nameof(Stream)} does not support reading.");
            }

            ArgumentOutOfRangeException.ThrowIfNegative(count);
            SeekUntilPosition();
            int read = BaseStream.Read(buffer, offset, Math.Min(count, (int)(_length - _position)));
            _position += read;
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!CanWrite)
            {
                throw new NotSupportedException($"Underlying {nameof(Stream)} does not support writing.");
            }

            ArgumentOutOfRangeException.ThrowIfNegative(count);
            SeekUntilPosition();
            int written = Math.Min(count, (int)(_length - _position));
            BaseStream.Write(buffer, offset, written);
            _position += written;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!CanSeek)
            {
                throw new NotSupportedException($"Underlying {nameof(Stream)} does not support seeking.");
            }

            long tempPosition;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    if (offset >= _length)
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek beyond the end of the stream.");
                    }

                    BaseStream.Seek(_offset + offset, SeekOrigin.Begin);
                    _position = offset;
                    break;
                case SeekOrigin.Current:
                    tempPosition = _position + offset;
                    if (tempPosition >= _length)
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek beyond the end of the stream.");
                    }

                    BaseStream.Seek(offset, SeekOrigin.Current);
                    _position = tempPosition;
                    break;
                case SeekOrigin.End:
                    tempPosition = _length + offset;
                    if (tempPosition >= _length)
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek beyond the end of the stream.");
                    }

                    long end = _offset + _length;
                    long endSpace = BaseStream.Length - end;
                    BaseStream.Seek(offset - endSpace, SeekOrigin.End);
                    _position = tempPosition;
                    break;
                default:
                    throw new NotSupportedException($"Unknown or invalid {nameof(SeekOrigin)}: {origin}");
            }

            return _position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Flush()
            => BaseStream.Flush();

        public override void SetLength(long value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value >= BaseStream.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"{nameof(value)} must be less than or equal to the underlying stream length.");
            }

            if (_position >= value)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"{nameof(Position)} would go beyond the end of the stream if length is set to {nameof(value)}: {value}");
            }

            _length = value;
        }

        #endregion

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (!_leaveOpen)
                    {
                        BaseStream.Dispose();
                    }
                }

                disposedValue = true;
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
