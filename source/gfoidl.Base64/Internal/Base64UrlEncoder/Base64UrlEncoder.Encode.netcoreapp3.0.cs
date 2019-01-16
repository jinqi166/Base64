﻿using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// Scalar based on https://github.com/dotnet/corefx/tree/master/src/System.Memory/src/System/Buffers/Text
// SSE2 based on https://github.com/aklomp/base64/tree/master/lib/arch/ssse3
// AVX2 based on https://github.com/aklomp/base64/tree/master/lib/arch/avx2

namespace gfoidl.Base64.Internal
{
    partial class Base64UrlEncoder
    {
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private OperationStatus EncodeImpl<T>(
            ref byte srcBytes,
            int srcLength,
            ref T dest,
            int destLength,
            int encodedLength,
            out int consumed,
            out int written,
            bool isFinalBlock = true)
            where T : unmanaged
        {
            uint sourceIndex = 0;
            uint destIndex   = 0;
            int maxSrcLength = 0;

            if (srcLength <= MaximumEncodeLength && destLength >= encodedLength)
                maxSrcLength = srcLength;
            else
                maxSrcLength = (destLength >> 2) * 3;

            if (maxSrcLength < 16)
                goto Scalar;

            if (Avx2.IsSupported && maxSrcLength >= 32)
            {
                Avx2Encode(ref srcBytes, ref dest, maxSrcLength, destLength, ref sourceIndex, ref destIndex);

                if (sourceIndex == srcLength)
                    goto DoneExit;
            }

            if (Ssse3.IsSupported && (maxSrcLength >= (int)sourceIndex + 16))
            {
                Ssse3Encode(ref srcBytes, ref dest, maxSrcLength, destLength, ref sourceIndex, ref destIndex);

                if (sourceIndex == srcLength)
                    goto DoneExit;
            }

        Scalar:
            maxSrcLength        -= 2;
            ref byte encodingMap = ref s_encodingMap[0];

            // In order to elide the movsxd in the loop
            if (sourceIndex < maxSrcLength)
            {
                do
                {
                    EncodeThreeBytes(ref Unsafe.Add(ref srcBytes, (IntPtr)sourceIndex), ref Unsafe.Add(ref dest, (IntPtr)destIndex), ref encodingMap);
                    destIndex   += 4;
                    sourceIndex += 3;
                }
                while (sourceIndex < (uint)maxSrcLength);
            }

            if (maxSrcLength != srcLength - 2)
                goto DestinationTooSmallExit;

            if (!isFinalBlock)
                goto NeedMoreDataExit;

            if (sourceIndex == srcLength - 1)
            {
                EncodeOneByte(ref Unsafe.Add(ref srcBytes, (IntPtr)sourceIndex), ref Unsafe.Add(ref dest, (IntPtr)destIndex), ref encodingMap);
                destIndex   += 2;
                sourceIndex += 1;
            }
            else if (sourceIndex == srcLength - 2)
            {
                EncodeTwoBytes(ref Unsafe.Add(ref srcBytes, (IntPtr)sourceIndex), ref Unsafe.Add(ref dest, (IntPtr)destIndex), ref encodingMap);
                destIndex   += 3;
                sourceIndex += 2;
            }

        DoneExit:
            consumed = (int)sourceIndex;
            written  = (int)destIndex;
            return OperationStatus.Done;

        NeedMoreDataExit:
            consumed = (int)sourceIndex;
            written  = (int)destIndex;
            return OperationStatus.NeedMoreData;

        DestinationTooSmallExit:
            consumed = (int)sourceIndex;
            written  = (int)destIndex;
            return OperationStatus.DestinationTooSmall;
        }
        //---------------------------------------------------------------------
#if DEBUG
        public static event EventHandler<EventArgs> Avx2Encoded;
        public static event EventHandler<EventArgs> Ssse3Encoded;
#endif
        //---------------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Avx2Encode<T>(ref byte src, ref T dest, int sourceLength, int destLength, ref uint sourceIndex, ref uint destIndex)
            where T : unmanaged
        {
            ref byte srcStart   = ref src;
            ref T destStart     = ref dest;
            ref byte simdSrcEnd = ref Unsafe.Add(ref src, (IntPtr)((uint)sourceLength - 32));   // no +1 as the comparison is >

            // The JIT won't hoist these "constants", so help it
            Vector256<sbyte>  shuffleVec          = s_avx_encodeShuffleVec;
            Vector256<sbyte>  shuffleConstant0    = Vector256.Create(0x0fc0fc00).AsSByte();
            Vector256<sbyte>  shuffleConstant2    = Vector256.Create(0x003f03f0).AsSByte();
            Vector256<ushort> shuffleConstant1    = Vector256.Create(0x04000040).AsUInt16();
            Vector256<short>  shuffleConstant3    = Vector256.Create(0x01000010).AsInt16();
            Vector256<byte>   translationContant0 = Vector256.Create((byte)51);
            Vector256<sbyte>  translationContant1 = Vector256.Create((sbyte)25);
            Vector256<sbyte>  lut                 = s_avx_encodeLut;

            // first load is done at c-0 not to get a segfault
            src.AssertRead<Vector256<sbyte>, byte>(ref srcStart, sourceLength);
            Vector256<sbyte> str = src.ReadVector256();

            // shift by 4 bytes, as required by enc_reshuffle
            str = Avx2.PermuteVar8x32(str.AsInt32(), s_avx_encodePermuteVec).AsSByte();

            // Next loads are at c-4, so shift it once
            src = ref Unsafe.Subtract(ref src, 4);

            while (true)
            {
                // Reshuffle
                str                  = Avx2.Shuffle(str, shuffleVec);
                Vector256<sbyte>  t0 = Avx2.And(str, shuffleConstant0);
                Vector256<sbyte>  t2 = Avx2.And(str, shuffleConstant2);
                Vector256<ushort> t1 = Avx2.MultiplyHigh(t0.AsUInt16(), shuffleConstant1);
                Vector256<short>  t3 = Avx2.MultiplyLow(t2.AsInt16(), shuffleConstant3);
                str                  = Avx2.Or(t1.AsSByte(), t3.AsSByte());

                // Translation
                Vector256<byte>  indices = Avx2.SubtractSaturate(str.AsByte(), translationContant0);
                Vector256<sbyte> mask    = Avx2.CompareGreaterThan(str, translationContant1);
                Vector256<sbyte> tmp     = Avx2.Subtract(indices.AsSByte(), mask);
                str                      = Avx2.Add(str, Avx2.Shuffle(lut, tmp));

                dest.AssertWrite<Vector256<sbyte>, T>(ref destStart, destLength);
                dest.WriteVector256(str);

                src  = ref Unsafe.Add(ref src,  24);
                dest = ref Unsafe.Add(ref dest, 32);

                if (Unsafe.IsAddressGreaterThan(ref src, ref simdSrcEnd))
                    break;

                // Load at c-4, as required by enc_reshuffle
                src.AssertRead<Vector256<sbyte>, byte>(ref srcStart, sourceLength);
                str = src.ReadVector256();
            }

            // Cast to ulong to avoid the overflow-check. Codegen for x86 is still good.
            sourceIndex = (uint)(ulong)Unsafe.ByteOffset(ref srcStart,  ref src) + 4;
            destIndex   = (uint)(ulong)Unsafe.ByteOffset(ref destStart, ref dest) / (uint)Unsafe.SizeOf<T>();

            src  = ref srcStart;
            dest = ref destStart;
#if DEBUG
            Avx2Encoded?.Invoke(null, EventArgs.Empty);
#endif
        }
        //---------------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Ssse3Encode<T>(ref byte src, ref T dest, int sourceLength, int destLength, ref uint sourceIndex, ref uint destIndex)
            where T : unmanaged
        {
            ref byte srcStart   = ref src;
            ref T destStart     = ref dest;
            ref byte simdSrcEnd = ref Unsafe.Add(ref src, (IntPtr)((uint)sourceLength - 16 + 1));   //  +1 for <=

            // Shift to workspace
            src  = ref Unsafe.Add(ref src , (IntPtr)sourceIndex);
            dest = ref Unsafe.Add(ref dest, (IntPtr)destIndex);

            // The JIT won't hoist these "constants", so help it
            Vector128<sbyte>  shuffleVec          = s_sse_encodeShuffleVec;
            Vector128<sbyte>  shuffleConstant0    = Vector128.Create(0x0fc0fc00).AsSByte();
            Vector128<sbyte>  shuffleConstant2    = Vector128.Create(0x003f03f0).AsSByte();
            Vector128<ushort> shuffleConstant1    = Vector128.Create(0x04000040).AsUInt16();
            Vector128<short>  shuffleConstant3    = Vector128.Create(0x01000010).AsInt16();
            Vector128<byte>   translationContant0 = Vector128.Create((byte) 51);
            Vector128<sbyte>  translationContant1 = Vector128.Create((sbyte)25);
            Vector128<sbyte>  lut                 = s_sse_encodeLut;

            //while (remaining >= 16)
            while (Unsafe.IsAddressLessThan(ref src, ref simdSrcEnd))
            {
                src.AssertRead<Vector128<sbyte>, byte>(ref srcStart, sourceLength);
                Vector128<sbyte> str = src.ReadVector128();

                // Reshuffle
                str                  = Ssse3.Shuffle(str, shuffleVec);
                Vector128<sbyte>  t0 = Sse2.And(str, shuffleConstant0);
                Vector128<sbyte>  t2 = Sse2.And(str, shuffleConstant2);
                Vector128<ushort> t1 = Sse2.MultiplyHigh(t0.AsUInt16(), shuffleConstant1);
                Vector128<short>  t3 = Sse2.MultiplyLow(t2.AsInt16(), shuffleConstant3);
                str                  = Sse2.Or(t1.AsSByte(), t3.AsSByte());

                // Translation
                Vector128<byte>  indices = Sse2.SubtractSaturate(str.AsByte(), translationContant0);
                Vector128<sbyte> mask    = Sse2.CompareGreaterThan(str, translationContant1);
                Vector128<sbyte> tmp     = Sse2.Subtract(indices.AsSByte(), mask);
                str                      = Sse2.Add(str, Ssse3.Shuffle(lut, tmp));

                dest.AssertWrite<Vector128<sbyte>, T>(ref destStart, destLength);
                dest.WriteVector128(str);

                src  = ref Unsafe.Add(ref src,  12);
                dest = ref Unsafe.Add(ref dest, 16);
            }

            // Cast to ulong to avoid the overflow-check. Codegen for x86 is still good.
            sourceIndex = (uint)(ulong)Unsafe.ByteOffset(ref srcStart, ref src);
            destIndex   = (uint)(ulong)Unsafe.ByteOffset(ref destStart, ref dest) / (uint)Unsafe.SizeOf<T>();

            src  = ref srcStart;
            dest = ref destStart;
#if DEBUG
            Ssse3Encoded?.Invoke(null, EventArgs.Empty);
#endif
        }
        //---------------------------------------------------------------------
        private static readonly Vector128<sbyte> s_sse_encodeLut;
        private static readonly Vector256<sbyte> s_avx_encodeLut;
    }
}
