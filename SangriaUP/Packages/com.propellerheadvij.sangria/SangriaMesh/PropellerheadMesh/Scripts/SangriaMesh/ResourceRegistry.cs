using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public struct ResourceEntry
    {
        public UnsafeList<byte> Buffer;
        public int TypeHash;
        public int SizeBytes;
    }

    public unsafe struct ResourceRegistry : IDisposable
    {
        private readonly Allocator m_Allocator;
        private bool m_IsDisposed;

        private UnsafeParallelHashMap<int, ResourceEntry> m_Resources;

        public int Count => m_Resources.IsCreated ? m_Resources.Count() : 0;

        public ResourceRegistry(int initialCapacity, Allocator allocator)
        {
            m_Allocator = allocator;
            m_IsDisposed = false;
            m_Resources = new UnsafeParallelHashMap<int, ResourceEntry>(math_max(1, initialCapacity), allocator);
        }

        public CoreResult SetResource<T>(int resourceId, in T value) where T : unmanaged
        {
            int typeHash = BurstRuntime.GetHashCode32<T>();
            int sizeBytes = UnsafeUtility.SizeOf<T>();

            if (m_Resources.TryGetValue(resourceId, out var existing))
            {
                if (existing.TypeHash != typeHash || existing.SizeBytes != sizeBytes)
                {
                    if (existing.Buffer.IsCreated)
                        existing.Buffer.Dispose();

                    existing = CreateEntry<T>(typeHash, sizeBytes);
                }

                T temp = value;
                UnsafeUtility.CopyStructureToPtr(ref temp, existing.Buffer.Ptr);
                m_Resources[resourceId] = existing;
                return CoreResult.Success;
            }

            var entry = CreateEntry<T>(typeHash, sizeBytes);
            T newValue = value;
            UnsafeUtility.CopyStructureToPtr(ref newValue, entry.Buffer.Ptr);
            m_Resources[resourceId] = entry;
            return CoreResult.Success;
        }

        public CoreResult TryGetResource<T>(int resourceId, out T value) where T : unmanaged
        {
            value = default;

            if (!m_Resources.TryGetValue(resourceId, out var entry))
                return CoreResult.NotFound;

            int typeHash = BurstRuntime.GetHashCode32<T>();
            if (entry.TypeHash != typeHash)
                return CoreResult.TypeMismatch;

            value = UnsafeUtility.ReadArrayElement<T>(entry.Buffer.Ptr, 0);
            return CoreResult.Success;
        }

        public bool ContainsResource(int resourceId)
        {
            return m_Resources.ContainsKey(resourceId);
        }

        public CoreResult RemoveResource(int resourceId)
        {
            if (!m_Resources.TryGetValue(resourceId, out var entry))
                return CoreResult.NotFound;

            if (entry.Buffer.IsCreated)
                entry.Buffer.Dispose();

            m_Resources.Remove(resourceId);
            return CoreResult.Success;
        }

        public CompiledResourceSet Compile(Allocator allocator)
        {
            int count = m_Resources.Count();
            var descriptors = new NativeArray<CompiledResourceDescriptor>(count, allocator);
            var idToDescriptor = new NativeParallelHashMap<int, int>(math_max(1, count), allocator);

            int totalBytes = 0;
            using (var pass = m_Resources.GetEnumerator())
            {
                while (pass.MoveNext())
                    totalBytes += pass.Current.Value.SizeBytes;
            }

            var data = new NativeArray<byte>(math_max(1, totalBytes), allocator, NativeArrayOptions.UninitializedMemory);
            byte* basePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(data);

            int descriptorIndex = 0;
            int runningOffset = 0;
            using (var enumerator = m_Resources.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    var kv = enumerator.Current;
                    int resourceId = kv.Key;
                    var entry = kv.Value;

                    descriptors[descriptorIndex] = new CompiledResourceDescriptor
                    {
                        ResourceId = resourceId,
                        TypeHash = entry.TypeHash,
                        SizeBytes = entry.SizeBytes,
                        OffsetBytes = runningOffset
                    };

                    idToDescriptor[resourceId] = descriptorIndex;
                    UnsafeUtility.MemCpy(basePtr + runningOffset, entry.Buffer.Ptr, entry.SizeBytes);

                    runningOffset += entry.SizeBytes;
                    descriptorIndex++;
                }
            }

            return new CompiledResourceSet(descriptors, idToDescriptor, data);
        }

        public void Clear()
        {
            using var enumerator = m_Resources.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var entry = enumerator.Current.Value;
                if (entry.Buffer.IsCreated)
                    entry.Buffer.Dispose();
            }

            m_Resources.Clear();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            if (m_Resources.IsCreated)
            {
                using var enumerator = m_Resources.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var entry = enumerator.Current.Value;
                    if (entry.Buffer.IsCreated)
                        entry.Buffer.Dispose();
                }

                m_Resources.Dispose();
            }

            m_IsDisposed = true;
        }

        private ResourceEntry CreateEntry<T>(int typeHash, int sizeBytes) where T : unmanaged
        {
            var buffer = new UnsafeList<byte>(sizeBytes, m_Allocator);
            buffer.Length = sizeBytes;

            return new ResourceEntry
            {
                Buffer = buffer,
                TypeHash = typeHash,
                SizeBytes = sizeBytes
            };
        }

        private static int math_max(int a, int b) => a > b ? a : b;
    }
}
