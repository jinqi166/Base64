using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace gfoidl.Base64.Internal
{
    internal static class AssertHelper
    {
        [Conditional("DEBUG")]
        public static unsafe void AssertRead<TVector, T>(this ref T src, ref T srcStart, int srcLength) where T : unmanaged
        {
            fixed (T* pSrc      = &src)
            fixed (T* pSrcStart = &srcStart)
            {
                int vectorElements = GetCount<TVector, T>();
                T* readEnd         = pSrc + vectorElements;
                T* srcEnd          = pSrcStart + srcLength;

                if (readEnd > srcEnd)
                {
                    int srcIndex = (int)(pSrc - pSrcStart);
                    throw new InvalidOperationException($"Read for {typeof(TVector)} is not within safe bounds. srcIndex: {srcIndex}, srcLength: {srcLength}");
                }
            }
        }
        //---------------------------------------------------------------------
        [Conditional("DEBUG")]
        public static unsafe void AssertWrite<TVector, T>(this ref T dest, ref T destStart, int destLength) where T : unmanaged
        {
            fixed (T* pDest      = &dest)
            fixed (T* pDestStart = &destStart)
            {
                int vectorElements = GetCount<TVector, T>();
                T* writeEnd        = pDest + vectorElements;
                T* destEnd         = pDestStart + destLength;

                if (writeEnd > destEnd)
                {
                    int destIndex = (int)(pDest - pDestStart);
                    throw new InvalidOperationException($"Write for {typeof(TVector)} is not within safe bounds. destIndex: {destIndex}, destLength: {destLength}");
                }
            }
        }
        //---------------------------------------------------------------------
        private static int GetCount<TVector, T>() where T : unmanaged
        {
            int vectorSize  = Unsafe.SizeOf<TVector>();
            int elementSize = Unsafe.SizeOf<T>();

            return vectorSize / elementSize;
        }
    }
}
