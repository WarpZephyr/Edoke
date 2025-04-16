using System.Text;

namespace Edoke.Helpers
{
    /// <summary>
    /// A helper containing encodings that are usually not easily accessed.
    /// </summary>
    internal static class EncodingHelper
    {
        /// <summary>
        /// UTF-16 or Unicode encoding in little endian.
        /// </summary>
        public static readonly Encoding UTF16LE;

        /// <summary>
        /// UTF-16 or Unicode encoding in big endian.
        /// </summary>
        public static readonly Encoding UTF16BE;

        /// <summary>
        /// Japanese Shift-JIS encoding.
        /// </summary>
        public static readonly Encoding ShiftJIS;

        /// <summary>
        /// Register the encodings.
        /// </summary>
        static EncodingHelper()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            UTF16LE = Encoding.Unicode;
            UTF16BE = Encoding.BigEndianUnicode;
            ShiftJIS = Encoding.GetEncoding("shift-jis");
        }
    }
}
