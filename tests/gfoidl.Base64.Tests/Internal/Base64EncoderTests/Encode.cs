﻿using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using gfoidl.Base64.Internal;
using NUnit.Framework;

#if NETCOREAPP
using System.Runtime.Intrinsics.X86;
#endif

namespace gfoidl.Base64.Tests.Internal.Base64EncoderTests
{
    [TestFixture(typeof(byte))]
    [TestFixture(typeof(char))]
    public class Encode<T> where T : unmanaged
    {
        [Test]
        public void Empty_input()
        {
            var sut                   = new Base64Encoder();
            ReadOnlySpan<byte> source = ReadOnlySpan<byte>.Empty;

            Span<T> encoded        = new T[sut.GetEncodedLength(source.Length)];
            OperationStatus status = sut.EncodeCore(source, encoded, out int consumed, out int written);

            Assert.AreEqual(OperationStatus.Done, status);
            Assert.AreEqual(0, consumed);
            Assert.AreEqual(0, written);
            Assert.IsTrue(encoded.IsEmpty);
        }
        //---------------------------------------------------------------------
        [Test]
        public void Empty_input_encode_to_string___empty_string()
        {
            var sut                   = new Base64Encoder();
            ReadOnlySpan<byte> source = ReadOnlySpan<byte>.Empty;

            string actual = sut.Encode(source);

            Assert.AreEqual(string.Empty, actual);
        }
        //---------------------------------------------------------------------
        [Test]
        public void Data_of_various_length()
        {
            var sut   = new Base64Encoder();
            var bytes = new byte[byte.MaxValue + 1];

            for (int i = 0; i < bytes.Length; ++i)
                bytes[i] = (byte)(255 - i);

            for (int i = 0; i < 256; ++i)
            {
                Span<byte> source      = bytes.AsSpan(0, i + 1);
                Span<T> encoded        = new T[sut.GetEncodedLength(source.Length)];
                OperationStatus status = sut.EncodeCore(source, encoded, out int consumed, out int written);

                Assert.AreEqual(OperationStatus.Done, status);
                Assert.AreEqual(source.Length, consumed);
                Assert.AreEqual(encoded.Length, written);

                string encodedText;

                if (typeof(T) == typeof(byte))
                {
                    Span<byte> encodedBytes = MemoryMarshal.AsBytes(encoded);
#if NETCOREAPP
                    encodedText = Encoding.ASCII.GetString(encodedBytes);
#else
                    encodedText = Encoding.ASCII.GetString(encodedBytes.ToArray());
#endif
                }
                else if (typeof(T) == typeof(char))
                {
#if NETCOREAPP
                    encodedText = new string(MemoryMarshal.Cast<T, char>(encoded));
#else
                    encodedText = new string(MemoryMarshal.Cast<T, char>(encoded).ToArray());
#endif
                }
                else
                {
                    throw new NotSupportedException(); // just in case new types are introduced in the future
                }
#if NETCOREAPP
                string expected = Convert.ToBase64String(source);
#else
                string expected = Convert.ToBase64String(source.ToArray());
#endif
                Assert.AreEqual(expected, encodedText);
            }
        }
        //---------------------------------------------------------------------
        [Test]
        public void Data_of_various_length_encoded_to_string()
        {
            var sut   = new Base64Encoder();
            var bytes = new byte[byte.MaxValue + 1];

            for (int i = 0; i < bytes.Length; ++i)
                bytes[i] = (byte)(255 - i);

            for (int value = 0; value < 256; ++value)
            {
                Span<byte> source = bytes.AsSpan(0, value + 1);
                string actual     = sut.Encode(source);
#if NETCOREAPP
                string expected = Convert.ToBase64String(source);
#else
                string expected = Convert.ToBase64String(source.ToArray());
#endif
                Assert.AreEqual(expected, actual);
            }
        }
        //---------------------------------------------------------------------
#if NETCOREAPP3_0 && DEBUG
        [Test]
        public void Large_data___avx2_event_fired()
        {
            Assume.That(Avx2.IsSupported);

            var sut  = new Base64Encoder();
            var data = new byte[50];
            var rnd  = new Random();
            rnd.NextBytes(data);

            int encodedLength = sut.GetEncodedLength(data.Length);
            Span<T> encoded   = new T[encodedLength];

            bool avx2Executed = false;
            Base64Encoder.Avx2Encoded += (s, e) => avx2Executed = true;

            OperationStatus status = sut.EncodeCore(data, encoded, out int consumed, out int written);

            Assert.IsTrue(avx2Executed);
        }
#endif
        //---------------------------------------------------------------------
#if NETCOREAPP && DEBUG
        [Test]
        public void Guid___ssse3_event_fired()
        {
            var sut  = new Base64Encoder();
            var data = Guid.NewGuid().ToByteArray();

            int encodedLength = sut.GetEncodedLength(data.Length);
            Span<T> encoded   = new T[encodedLength];

            bool ssse3Executed = false;
            Base64Encoder.Ssse3Encoded += (s, e) => ssse3Executed = true;

            OperationStatus status = sut.EncodeCore(data, encoded, out int consumed, out int written);

            Assert.IsTrue(ssse3Executed);
        }
#endif
        //---------------------------------------------------------------------
        [Test]
        public void Buffer_chain_basic_encode()
        {
            var sut  = new Base64Encoder();
            var data = new byte[200];
            var rnd  = new Random(0);
            rnd.NextBytes(data);

            int encodedLength = sut.GetEncodedLength(data.Length);
            Span<T> encoded   = new T[encodedLength];

            OperationStatus status = sut.EncodeCore(data.AsSpan(0, data.Length / 2), encoded, out int consumed, out int written1, isFinalBlock: false);
            Assert.AreEqual(OperationStatus.NeedMoreData, status);

            status = sut.EncodeCore(data.AsSpan(consumed), encoded.Slice(written1), out consumed, out int written2, isFinalBlock: true);
            Assert.AreEqual(OperationStatus.Done, status);
            Assert.AreEqual(encodedLength, written1 + written2);

            Span<T> expected = new T[encodedLength];
            sut.EncodeCore(data, expected, out int _, out int _);

            string encodedText;
            string expectedText;

            if (typeof(T) == typeof(byte))
            {
                Span<byte> encodedBytes  = MemoryMarshal.AsBytes(encoded);
                Span<byte> expectedBytes = MemoryMarshal.AsBytes(expected);
#if NETCOREAPP
                encodedText  = Encoding.ASCII.GetString(encodedBytes);
                expectedText = Encoding.ASCII.GetString(expectedBytes);
#else
                encodedText  = Encoding.ASCII.GetString(encodedBytes .ToArray());
                expectedText = Encoding.ASCII.GetString(expectedBytes.ToArray());
#endif
            }
            else if (typeof(T) == typeof(char))
            {
#if NETCOREAPP
                encodedText  = new string(MemoryMarshal.Cast<T, char>(encoded));
                expectedText = new string(MemoryMarshal.Cast<T, char>(expected));
#else
                encodedText  = new string(MemoryMarshal.Cast<T, char>(encoded) .ToArray());
                expectedText = new string(MemoryMarshal.Cast<T, char>(expected).ToArray());
#endif
            }
            else
            {
                throw new NotSupportedException(); // just in case new types are introduced in the future
            }

            Assert.AreEqual(expectedText, encodedText);
        }
        //---------------------------------------------------------------------
        [Test]
        public void Buffer_chain_various_length_encode()
        {
            var sut = new Base64Encoder();
            var rnd = new Random(0);

            for (int i = 2; i < 200; ++i)
            {
                var data = new byte[i];
                rnd.NextBytes(data);

                int encodedLength = sut.GetEncodedLength(data.Length);
                Span<T> encoded   = new T[encodedLength];

                OperationStatus status = sut.EncodeCore(data.AsSpan(0, data.Length / 2), encoded, out int consumed, out int written1, isFinalBlock: false);
                Assert.AreEqual(OperationStatus.NeedMoreData, status);

                status = sut.EncodeCore(data.AsSpan(consumed), encoded.Slice(written1), out consumed, out int written2, isFinalBlock: true);
                Assert.AreEqual(OperationStatus.Done, status);
                Assert.AreEqual(encodedLength, written1 + written2);

                Span<T> expected = new T[encodedLength];
                sut.EncodeCore(data, expected, out int _, out int _);

                string encodedText;
                string expectedText;

                if (typeof(T) == typeof(byte))
                {
                    Span<byte> encodedBytes  = MemoryMarshal.AsBytes(encoded);
                    Span<byte> expectedBytes = MemoryMarshal.AsBytes(expected);
#if NETCOREAPP
                    encodedText  = Encoding.ASCII.GetString(encodedBytes);
                    expectedText = Encoding.ASCII.GetString(expectedBytes);
#else
                    encodedText  = Encoding.ASCII.GetString(encodedBytes .ToArray());
                    expectedText = Encoding.ASCII.GetString(expectedBytes.ToArray());
#endif
                }
                else if (typeof(T) == typeof(char))
                {
#if NETCOREAPP
                    encodedText  = new string(MemoryMarshal.Cast<T, char>(encoded));
                    expectedText = new string(MemoryMarshal.Cast<T, char>(expected));
#else
                    encodedText  = new string(MemoryMarshal.Cast<T, char>(encoded) .ToArray());
                    expectedText = new string(MemoryMarshal.Cast<T, char>(expected).ToArray());
#endif
                }
                else
                {
                    throw new NotSupportedException(); // just in case new types are introduced in the future
                }

                Assert.AreEqual(expectedText, encodedText);
            }
        }
        //---------------------------------------------------------------------
        [Test]
        [TestCase(3)]
        [TestCase(24)]
        [TestCase(60)]
        public void DestinationLength_too_small___status_DestinationTooSmall(int dataLength)
        {
            var sut         = new Base64Encoder();
            var data        = new byte[dataLength];
            Span<T> encoded = new T[1];

            OperationStatus status = sut.EncodeCore(data, encoded, out int consumed, out int written);

            Assert.Multiple(() =>
            {
                Assert.AreEqual(OperationStatus.DestinationTooSmall, status);
                Assert.AreEqual(0, written);
            });
        }
        //---------------------------------------------------------------------
        [Test]
        public void DestiantationLength_large_but_too_small___status_DestinationTooSmall()
        {
            const int dataLength = 300;
            const int destLength = 350;

            var sut         = new Base64Encoder();
            var data        = new byte[dataLength];
            Span<T> encoded = new T[destLength];

            OperationStatus status = sut.EncodeCore(data, encoded, out int consumed, out int written);

            Assert.Multiple(() =>
            {
                const int expectedConsumed = destLength / 4 * 3;
                const int expectedWritten  = expectedConsumed / 3 * 4;

                Assert.AreEqual(OperationStatus.DestinationTooSmall, status);
                Assert.AreEqual(expectedConsumed, consumed);
                Assert.AreEqual(expectedWritten,  written);
            });
        }
    }
}
