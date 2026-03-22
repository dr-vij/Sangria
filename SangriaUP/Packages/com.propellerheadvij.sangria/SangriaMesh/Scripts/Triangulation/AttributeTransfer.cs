using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public enum InterpolationMode : byte
    {
        Linear = 0,
        Nearest = 1,
        None = 2
    }

    public struct InterpolationPolicy : IDisposable
    {
        private NativeParallelHashMap<int, InterpolationMode> m_Overrides;
        private InterpolationMode m_Default;
        private bool m_IsCreated;

        public static InterpolationPolicy Default => new InterpolationPolicy
        {
            m_Default = InterpolationMode.Linear,
            m_IsCreated = false
        };

        public InterpolationPolicy(int capacity, Allocator allocator,
            InterpolationMode defaultMode = InterpolationMode.Linear)
        {
            m_Overrides = new NativeParallelHashMap<int, InterpolationMode>(capacity, allocator);
            m_Default = defaultMode;
            m_IsCreated = true;
        }

        public void SetMode(int attributeId, InterpolationMode mode)
        {
            if (!m_IsCreated)
                throw new InvalidOperationException(
                    "InterpolationPolicy must be created with an allocator before setting overrides.");

            if (m_Overrides.ContainsKey(attributeId))
                m_Overrides[attributeId] = mode;
            else
                m_Overrides.Add(attributeId, mode);
        }

        public InterpolationMode GetMode(int attributeId)
        {
            if (m_IsCreated && m_Overrides.TryGetValue(attributeId, out var mode))
                return mode;
            return m_Default;
        }

        public void Dispose()
        {
            if (m_IsCreated && m_Overrides.IsCreated)
                m_Overrides.Dispose();
            m_IsCreated = false;
        }
    }

    public static class AttributeTransferOp
    {
        public static CoreResult TransferPointAttributes(
            ref NativeDetail source,
            ref NativeDetail destination,
            in ProvenanceMap provenance,
            in InterpolationPolicy policy = default)
        {
            if (!provenance.IsCreated)
                return CoreResult.InvalidOperation;

            if (provenance.OutputPointCount != destination.PointCount)
                return CoreResult.InvalidOperation;

            int sourceColumnCount = source.GetPointAttributeColumnCount();
            for (int col = 0; col < sourceColumnCount; col++)
            {
                var column = source.GetPointAttributeColumnAt(col);
                int attributeId = column.AttributeId;

                if (attributeId == AttributeID.Position)
                    continue;

                InterpolationMode mode = policy.GetMode(attributeId);
                if (mode == InterpolationMode.None)
                    continue;

                if (!destination.HasPointAttribute(attributeId))
                    destination.AddPointAttributeRaw(attributeId, column.Stride, column.TypeHash);

                var dstColumn = destination.GetPointAttributeColumnByIdUnchecked(attributeId);
                BlendColumn(in column, in dstColumn, in provenance, mode);
            }

            return CoreResult.Success;
        }

        public static CoreResult TransferAttribute<T>(
            NativeAttributeAccessor<T> sourceAccessor,
            NativeAttributeAccessor<T> destinationAccessor,
            in ProvenanceMap provenance,
            InterpolationMode mode = InterpolationMode.Linear) where T : unmanaged
        {
            if (!provenance.IsCreated)
                return CoreResult.InvalidOperation;

            unsafe
            {
                int stride = UnsafeUtility.SizeOf<T>();
                byte* srcPtr = (byte*)sourceAccessor.GetBasePointer();
                byte* dstPtr = (byte*)destinationAccessor.GetBasePointer();

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];
                    BlendSingle(srcPtr, dstPtr, i, in record, stride, mode);
                }
            }

            return CoreResult.Success;
        }

        private static void BlendColumn(
            in AttributeColumn srcColumn,
            in AttributeColumn dstColumn,
            in ProvenanceMap provenance,
            InterpolationMode mode)
        {
            unsafe
            {
                byte* srcPtr = srcColumn.Buffer.Ptr;
                byte* dstPtr = dstColumn.Buffer.Ptr;
                int stride = srcColumn.Stride;

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];
                    BlendSingle(srcPtr, dstPtr, i, in record, stride, mode);
                }
            }
        }

        private static unsafe void BlendSingle(
            byte* srcPtr, byte* dstPtr, int outputIndex,
            in ProvenanceRecord record, int stride, InterpolationMode mode)
        {
            byte* dst = dstPtr + outputIndex * stride;

            if (record.Count == 0)
            {
                UnsafeUtility.MemClear(dst, stride);
                return;
            }

            if (record.Count == 1 || mode == InterpolationMode.Nearest)
            {
                int srcIdx = record.Src0;
                UnsafeUtility.MemCpy(dst, srcPtr + srcIdx * stride, stride);
                return;
            }

            // Linear blend for float-based types (stride must be multiple of 4)
            int floatCount = stride / 4;
            if (floatCount <= 0 || stride % 4 != 0)
            {
                // Non-float type: fallback to nearest
                UnsafeUtility.MemCpy(dst, srcPtr + record.Src0 * stride, stride);
                return;
            }

            float* d = (float*)dst;

            // First source
            {
                float* s = (float*)(srcPtr + record.Src0 * stride);
                float w = record.W0;
                for (int j = 0; j < floatCount; j++)
                    d[j] = s[j] * w;
            }

            // Remaining sources
            if (record.Count > 1)
            {
                float* s = (float*)(srcPtr + record.Src1 * stride);
                float w = record.W1;
                for (int j = 0; j < floatCount; j++)
                    d[j] += s[j] * w;
            }
            if (record.Count > 2)
            {
                float* s = (float*)(srcPtr + record.Src2 * stride);
                float w = record.W2;
                for (int j = 0; j < floatCount; j++)
                    d[j] += s[j] * w;
            }
            if (record.Count > 3)
            {
                float* s = (float*)(srcPtr + record.Src3 * stride);
                float w = record.W3;
                for (int j = 0; j < floatCount; j++)
                    d[j] += s[j] * w;
            }
        }
    }
}
