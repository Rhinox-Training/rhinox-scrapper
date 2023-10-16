using System;
using Rhinox.Perceptor;
using UnityEngine;

namespace Rhinox.Scrapper
{
    public class TraceTimer : IDisposable
    {
        private float _start;
        private string _msg;
        
        public TraceTimer(string msg)
        {
            _start = Time.realtimeSinceStartup;
            _msg = msg;
        }
        
        public void Dispose()
        {
            var curTime = Time.realtimeSinceStartup;
            PLog.Trace<ScrapperLogger>($"[PERF] {_msg} in {1000.0f*(curTime - _start):####0.#} ms");
        }
    }
}