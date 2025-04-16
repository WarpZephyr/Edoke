using System;

namespace Edoke.Helpers
{
    /// <summary>
    /// A helper for finding null-terminated string length.
    /// </summary>
    internal static class StringLengthHelper
    {
        /// <summary>
        /// Get the length of an 8-bit null-terminated string existing in a buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns>The found length.</returns>
        public static int Strlen(ReadOnlySpan<byte> buffer)
        {
            // Zero length buffers should pass as we check for less than in the loop
            int i = 0;
            for (; i < buffer.Length; i++)
            {
                if (buffer[i] == 0)
                {
                    break;
                }
            }

            return i;
        }

        /// <summary>
        /// Get the length of a 16-bit null-terminated string existing in a buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns>The found length.</returns>
        public static int WStrlen(ReadOnlySpan<byte> buffer)
        {
            int i = 0;
            int runLength = buffer.Length >>> 1;
            for (; i < runLength; i += 2)
            {
                if (buffer[i] == 0)
                {
                    if (buffer[i + 1] == 0)
                    {
                        break;
                    }
                }
            }

            return i;
        }
    }
}
