using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using System;

namespace ViJMeshTools
{
    public static class NativeCollectionsConverter
    {
        /// <summary>
        /// Converts Array to Native Array
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sourceArray"></param>
        /// <param name="allocator"></param>
        /// <returns></returns>
        public static NativeArray<T> ToNativeArray<T>(this T[] sourceArray, Allocator allocator) where T : struct
        {
            return new NativeArray<T>(sourceArray, allocator);
        }

        /// <summary>
        /// Converts NativeHashMap To Dictionary
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="initialNativeHashMap"></param>
        /// <returns></returns>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this NativeHashMap<TKey, TValue> initialNativeHashMap) where TKey : unmanaged, IEquatable<TKey> where TValue : unmanaged
        {
            var retDict = new Dictionary<TKey, TValue>(initialNativeHashMap.Count);
            using var enumerator = initialNativeHashMap.GetEnumerator();
            while (enumerator.MoveNext())
                retDict.Add(enumerator.Current.Key, enumerator.Current.Value);
            return retDict;
        }

        /// <summary>
        /// Converts NativeHashSet To HashSet
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="initialHashset"></param>
        /// <returns></returns>
        public static HashSet<T> ToHashSet<T>(this NativeHashSet<T> initialHashset) where T : unmanaged, IEquatable<T>
        {
            var retHashSet = new HashSet<T>();
            using var enumerator = initialHashset.GetEnumerator();
            while (enumerator.MoveNext())
                retHashSet.Add(enumerator.Current);
            return retHashSet;
        }
    }
}