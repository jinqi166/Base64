﻿using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace gfoidl.Base64.Internal
{
    partial class Base64UrlEncoder
    {
        // PERF: can't be in base class due to inlining (generic virtual)
        public override unsafe string Encode(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return string.Empty;

            int encodedLength = this.GetEncodedLength(data.Length);

            // Threshoulds found by testing -- may not be ideal on all targets

            if (data.Length < 64)
            {
                char* ptr              = stackalloc char[encodedLength];
                ref char encoded       = ref Unsafe.AsRef<char>(ptr);
                ref byte srcBytes      = ref MemoryMarshal.GetReference(data);
                OperationStatus status = this.EncodeImpl(ref srcBytes, data.Length, ref encoded, encodedLength, encodedLength, out int consumed, out int written);

                Debug.Assert(status        == OperationStatus.Done);
                Debug.Assert(data.Length   == consumed);
                Debug.Assert(encodedLength == written);

                return new string(ptr, 0, written);
            }

            fixed (byte* ptr = data)
            {
                return string.Create(encodedLength, (Ptr: (IntPtr)ptr, data.Length, encodedLength), (encoded, state) =>
                {
                    ref byte srcBytes      = ref Unsafe.AsRef<byte>(state.Ptr.ToPointer());
                    ref char dest          = ref MemoryMarshal.GetReference(encoded);
                    OperationStatus status = this.EncodeImpl(ref srcBytes, state.Length, ref dest, encoded.Length, encoded.Length, out int consumed, out int written);

                    Debug.Assert(status         == OperationStatus.Done);
                    Debug.Assert(state.Length   == consumed);
                    Debug.Assert(encoded.Length == written);
                });
            }
        }
    }
}
