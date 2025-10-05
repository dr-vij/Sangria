using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ViJMeshTools
{
    public static class Stopwatcher
    {
        private static Dictionary<string, Stopwatch> mStopwathcersDictionary = new Dictionary<string, Stopwatch>();

        public static void Clear()
        {
            foreach (var sw in mStopwathcersDictionary.Values)
                sw.Stop();
            mStopwathcersDictionary.Clear();
        }

        public static void ResetAll()
        {
            foreach (var sw in mStopwathcersDictionary.Values)
            {
                sw.Stop();
                sw.Reset();
            }
        }

        public static void Start(string key)
        {
            var sw = GetStopwatch(key);
            sw.Start();
        }

        public static void Pause(string key)
        {
            var sw = GetStopwatch(key, true);
            sw.Stop();
        }

        public static void Reset(string key)
        {
            var sw = GetStopwatch(key, true);
            sw.Stop();
            sw.Reset();
        }

        public static Tuple<string, long>[] GetMilliseconds()
        {
            var arr = new Tuple<string, long>[mStopwathcersDictionary.Count];
            int counter = 0;
            foreach (var swPair in mStopwathcersDictionary)
            {
                arr[counter++] = new Tuple<string, long>(swPair.Key, swPair.Value.ElapsedMilliseconds);
            }
            return arr;
        }
        
        public static Tuple<string, double>[] GetMicroseconds()
        {
            var arr = new Tuple<string, double>[mStopwathcersDictionary.Count];
            int counter = 0;
            foreach (var swPair in mStopwathcersDictionary)
            {
                double microseconds = (double)swPair.Value.ElapsedTicks / System.Diagnostics.Stopwatch.Frequency * 1000000;
                arr[counter++] = new Tuple<string, double>(swPair.Key, microseconds);
            }
            return arr;
        }

        
        public static void DebugLogMilliseconds()
        {
            Debug.Log("------");
            var sws = GetMilliseconds();
            foreach (var sw in sws)
            {
                Debug.Log($"StopwatchKey: {sw.Item1}, milliseconds {sw.Item2}");
            }
            Debug.Log("------");
        }

        public static void DebugLogMicroseconds()
        {
            Debug.Log("------");
            var sws = GetMicroseconds();
            foreach (var sw in sws)
            {
                Debug.Log($"StopwatchKey: {sw.Item1}, microseconds {sw.Item2}");
            }
            Debug.Log("------");
        }

        private static Stopwatch GetStopwatch(string key, bool logIfNotFound = false)
        {
            if (!mStopwathcersDictionary.TryGetValue(key, out var sw))
            {
                if (logIfNotFound)
                    Debug.LogError($"Stopwatch {key} not found");
                sw = new Stopwatch();
                mStopwathcersDictionary.Add(key, sw);
            }
            return sw;
        }
    }
}