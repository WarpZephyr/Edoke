using System;

namespace Edoke.IO
{
    /// <summary>
    /// An interface for binary writers.
    /// </summary>
    public interface IBinaryWriter
    {
        /// <summary>
        /// Writes an <see cref="sbyte"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteSByte(sbyte value);

        /// <summary>
        /// Writes a <see cref="sbyte"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteByte(byte value);

        /// <summary>
        /// Writes a <see cref="short"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt16(short value);

        /// <summary>
        /// Writes a <see cref="ushort"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt16(ushort value);

        /// <summary>
        /// Writes an <see cref="int"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt32(int value);

        /// <summary>
        /// Writes a <see cref="uint"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt32(uint value);

        /// <summary>
        /// Writes a <see cref="long"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteInt64(long value);

        /// <summary>
        /// Writes a <see cref="ulong"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt64(ulong value);

        /// <summary>
        /// Writes a <see cref="Half"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteHalf(Half value);

        /// <summary>
        /// Writes a <see cref="float"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteSingle(float value);

        /// <summary>
        /// Writes a <see cref="double"/>.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteDouble(double value);
    }
}
