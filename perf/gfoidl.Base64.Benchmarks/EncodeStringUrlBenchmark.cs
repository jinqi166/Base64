﻿using System;
using BenchmarkDotNet.Attributes;

namespace gfoidl.Base64.Benchmarks
{
    [MemoryDiagnoser]
    public class EncodeStringUrlBenchmark
    {
        private byte[] _data;
        //---------------------------------------------------------------------
        [Params(5, 16, 1_000)]
        public int DataLen { get; set; } = 16;
        //---------------------------------------------------------------------
        [GlobalSetup]
        public void GlobalSetup()
        {
            _data = new byte[this.DataLen];

            var rnd = new Random();
            rnd.NextBytes(_data);
        }
        //---------------------------------------------------------------------
        [Benchmark(Baseline = true)]
        public string ConvertToBase64()
        {
            string base64 = Convert.ToBase64String(_data);
            return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
        //---------------------------------------------------------------------
        [Benchmark]
        public string gfoidlBase64Url()
        {
            return Base64.Url.Encode(_data);
        }
    }
}
