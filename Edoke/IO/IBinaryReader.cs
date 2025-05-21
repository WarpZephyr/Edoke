using System;

namespace Edoke.IO
{
    /// <summary>
    /// An interface for binary readers.
    /// </summary>
    public interface IBinaryReader
    {
        /// <summary>
        /// Reads an <see cref="sbyte"/>.
        /// </summary>
        /// <returns>An <see cref="sbyte"/>.</returns>
        public sbyte ReadSByte();

        /// <summary>
        /// Reads a <see cref="byte"/>.
        /// </summary>
        /// <returns>A <see cref="byte"/>.</returns>
        public byte ReadByte();

        /// <summary>
        /// Reads a <see cref="short"/>.
        /// </summary>
        /// <returns>A <see cref="short"/>.</returns>
        public short ReadInt16();

        /// <summary>
        /// Reads a <see cref="ushort"/>.
        /// </summary>
        /// <returns>A <see cref="ushort"/>.</returns>
        public ushort ReadUInt16();

        /// <summary>
        /// Reads an <see cref="int"/>.
        /// </summary>
        /// <returns>An <see cref="int"/>.</returns>
        public int ReadInt32();

        /// <summary>
        /// Reads a <see cref="uint"/>.
        /// </summary>
        /// <returns>A <see cref="uint"/>.</returns>
        public uint ReadUInt32();

        /// <summary>
        /// Reads a <see cref="long"/>.
        /// </summary>
        /// <returns>A <see cref="long"/>.</returns>
        public long ReadInt64();

        /// <summary>
        /// Reads a <see cref="ulong"/>.
        /// </summary>
        /// <returns>A <see cref="ulong"/>.</returns>
        public ulong ReadUInt64();

        /// <summary>
        /// Reads a <see cref="Half"/>.
        /// </summary>
        /// <returns>A <see cref="Half"/>.</returns>
        public Half ReadHalf();

        /// <summary>
        /// Reads a <see cref="float"/>.
        /// </summary>
        /// <returns>A <see cref="float"/>.</returns>
        public float ReadSingle();

        /// <summary>
        /// Reads a <see cref="double"/>.
        /// </summary>
        /// <returns>A <see cref="double"/>.</returns>
        public double ReadDouble();
    }
}
