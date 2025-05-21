using System;
using System.IO;
using System.Text;

namespace Edoke.Helpers
{
    /// <summary>
    /// A helper for assertions.
    /// </summary>
    internal static class AssertHelper
    {
        /// <summary>
        /// Asserts a value is one of the specified options, throwing if it is not.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to assert.</param>
        /// <param name="typeName">The name of the value type.</param>
        /// <param name="options">The options to assert.</param>
        /// <returns>The value to assert.</returns>
        /// <exception cref="InvalidDataException">The assertion failed.</exception>
        public static T Assert<T>(T value, string typeName, ReadOnlySpan<T> options) where T : IEquatable<T>
        {
            foreach (T option in options)
            {
                if (value.Equals(option))
                {
                    return value;
                }
            }

            string strOptions = string.Join(", ", options.ToArray());
            throw new InvalidDataException($"Assertion failed for {typeName}: {value} | Expected: {strOptions}");
        }

        /// <summary>
        /// Asserts a value is one of the specified options, throwing if it is not.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to assert.</param>
        /// <param name="typeName">The name of the value type.</param>
        /// <param name="format">The formatting to use for the values in strings.</param>
        /// <param name="options">The options to assert.</param>
        /// <returns>The value to assert.</returns>
        /// <exception cref="InvalidDataException">The assertion failed.</exception>
        public static T Assert<T>(T value, string typeName, string format, ReadOnlySpan<T> options) where T : IEquatable<T>
        {
            foreach (T option in options)
            {
                if (value.Equals(option))
                {
                    return value;
                }
            }

            var sb = new StringBuilder();
            for (int i = 0; i < options.Length; i++)
            {
                sb.Append($"{string.Format(format, options[i])}, ");
            }

            if (options.Length > 0)
            {
                sb.Append($"{string.Format(format, options[^1])}");
            }

            throw new InvalidDataException($"Assertion failed for {typeName}: {string.Format(format, value)} | Expected: {sb}");
        }

        /// <summary>
        /// Asserts a value is the specified option, throwing if it is not.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to assert.</param>
        /// <param name="typeName">The name of the value type.</param>
        /// <param name="option">The option to assert.</param>
        /// <returns>The value to assert.</returns>
        /// <exception cref="InvalidDataException">The assertion failed.</exception>
        public static T Assert<T>(T value, string typeName, T option) where T : IEquatable<T>
        {
            if (value.Equals(option))
            {
                return value;
            }

            throw new InvalidDataException($"Assertion failed for {typeName}: {value} | Expected: {option}");
        }

        /// <summary>
        /// Asserts a value is the specified option, throwing if it is not.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to assert.</param>
        /// <param name="typeName">The name of the value type.</param>
        /// <param name="format">The formatting to use for the value in strings.</param>
        /// <param name="option">The option to assert.</param>
        /// <returns>The value to assert.</returns>
        /// <exception cref="InvalidDataException">The assertion failed.</exception>
        public static T Assert<T>(T value, string typeName, string format, T option) where T : IEquatable<T>
        {
            if (value.Equals(option))
            {
                return value;
            }

            throw new InvalidDataException($"Assertion failed for {typeName}: {string.Format(format, value)} | Expected: {string.Format(format, option)}");
        }

        /// <summary>
        /// Assert a value is present in an <see cref="Enum"/> type, throwing if it is not.
        /// </summary>
        /// <typeparam name="TEnum">The <see cref="Enum"/> type.</typeparam>
        /// <typeparam name="TValue">The value type.</typeparam>
        /// <param name="value">The value to assert.</param>
        /// <returns>The value to assert.</returns>
        /// <exception cref="InvalidDataException">The value was not present in the <see cref="Enum"/>.</exception>
        public static TEnum AssertEnum<TEnum, TValue>(TValue value)
            where TEnum : Enum
            where TValue : unmanaged
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
            {
                throw new InvalidDataException($"Value not present in enum: {string.Format("0x{0:X}", value)}");
            }
            return (TEnum)(object)value;
        }
    }
}
