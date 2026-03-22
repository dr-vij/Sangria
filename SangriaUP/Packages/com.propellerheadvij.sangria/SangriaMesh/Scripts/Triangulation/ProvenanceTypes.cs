using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SangriaMesh
{
    public enum ProvenanceKind : byte
    {
        Identity = 0,
        Intersection = 1,
        Merge = 2,
        EdgeSplit = 3,
        Degenerate = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProvenanceRecord
    {
        public int Src0, Src1, Src2, Src3;
        public float W0, W1, W2, W3;
        public byte Count;
        public ProvenanceKind Kind;

        public static ProvenanceRecord Identity(int sourcePointId)
        {
            return new ProvenanceRecord
            {
                Src0 = sourcePointId,
                W0 = 1.0f,
                Count = 1,
                Kind = ProvenanceKind.Identity
            };
        }

        public static ProvenanceRecord Intersection(
            int src0, float w0,
            int src1, float w1,
            int src2, float w2,
            int src3, float w3)
        {
            var record = new ProvenanceRecord
            {
                Src0 = src0, W0 = w0,
                Src1 = src1, W1 = w1,
                Src2 = src2, W2 = w2,
                Src3 = src3, W3 = w3,
                Count = 4,
                Kind = ProvenanceKind.Intersection
            };
            record.CoalesceAndNormalize();
            return record;
        }

        public static ProvenanceRecord Combine(
            in ProvenanceRecord a,
            in ProvenanceRecord b,
            ProvenanceKind kind)
        {
            unsafe
            {
                int* ids = stackalloc int[8];
                float* ws = stackalloc float[8];
                int total = 0;

                AddSources(in a, ids, ws, ref total);
                AddSources(in b, ids, ws, ref total);

                CoalesceSortTruncate(ids, ws, ref total, 4);

                var result = new ProvenanceRecord
                {
                    Count = (byte)total,
                    Kind = kind
                };

                if (total > 0) { result.Src0 = ids[0]; result.W0 = ws[0]; }
                if (total > 1) { result.Src1 = ids[1]; result.W1 = ws[1]; }
                if (total > 2) { result.Src2 = ids[2]; result.W2 = ws[2]; }
                if (total > 3) { result.Src3 = ids[3]; result.W3 = ws[3]; }

                result.Normalize();
                return result;
            }
        }

        /// <summary>
        /// Combines up to 4 weighted ProvenanceRecords (flatten).
        /// Each record is scaled by its weight, then all sources are
        /// coalesced, sorted by weight descending, truncated to 4, sorted by sourceId, and normalized.
        /// </summary>
        public static ProvenanceRecord CombineWeighted(
            in ProvenanceRecord a, float wa,
            in ProvenanceRecord b, float wb,
            in ProvenanceRecord c, float wc,
            in ProvenanceRecord d, float wd,
            ProvenanceKind kind)
        {
            unsafe
            {
                // Up to 16 source pairs (4 records × 4 sources)
                int* ids = stackalloc int[16];
                float* ws = stackalloc float[16];
                int total = 0;

                AddSourcesScaled(in a, wa, ids, ws, ref total);
                AddSourcesScaled(in b, wb, ids, ws, ref total);
                AddSourcesScaled(in c, wc, ids, ws, ref total);
                AddSourcesScaled(in d, wd, ids, ws, ref total);

                CoalesceSortTruncate(ids, ws, ref total, 4);

                var result = new ProvenanceRecord
                {
                    Count = (byte)total,
                    Kind = kind
                };

                if (total > 0) { result.Src0 = ids[0]; result.W0 = ws[0]; }
                if (total > 1) { result.Src1 = ids[1]; result.W1 = ws[1]; }
                if (total > 2) { result.Src2 = ids[2]; result.W2 = ws[2]; }
                if (total > 3) { result.Src3 = ids[3]; result.W3 = ws[3]; }

                result.Normalize();
                return result;
            }
        }

        private static unsafe void AddSources(in ProvenanceRecord r, int* ids, float* ws, ref int total)
        {
            if (r.Count > 0) { ids[total] = r.Src0; ws[total] = r.W0; total++; }
            if (r.Count > 1) { ids[total] = r.Src1; ws[total] = r.W1; total++; }
            if (r.Count > 2) { ids[total] = r.Src2; ws[total] = r.W2; total++; }
            if (r.Count > 3) { ids[total] = r.Src3; ws[total] = r.W3; total++; }
        }

        private static unsafe void AddSourcesScaled(in ProvenanceRecord r, float scale, int* ids, float* ws, ref int total)
        {
            if (r.Count > 0) { ids[total] = r.Src0; ws[total] = r.W0 * scale; total++; }
            if (r.Count > 1) { ids[total] = r.Src1; ws[total] = r.W1 * scale; total++; }
            if (r.Count > 2) { ids[total] = r.Src2; ws[total] = r.W2 * scale; total++; }
            if (r.Count > 3) { ids[total] = r.Src3; ws[total] = r.W3 * scale; total++; }
        }

        /// <summary>
        /// Common method: coalesce duplicate source ids, sort by weight descending,
        /// truncate to maxCount, then sort by sourceId ascending for determinism.
        /// </summary>
        private static unsafe void CoalesceSortTruncate(int* ids, float* ws, ref int total, int maxCount)
        {
            // Coalesce duplicate source ids
            for (int i = 0; i < total; i++)
            {
                for (int j = i + 1; j < total; j++)
                {
                    if (ids[i] == ids[j])
                    {
                        ws[i] += ws[j];
                        ids[j] = ids[total - 1];
                        ws[j] = ws[total - 1];
                        total--;
                        j--;
                    }
                }
            }

            // Sort by weight descending for truncation
            for (int i = 1; i < total; i++)
            {
                float wKey = ws[i];
                int idKey = ids[i];
                int j = i - 1;
                while (j >= 0 && ws[j] < wKey)
                {
                    ws[j + 1] = ws[j];
                    ids[j + 1] = ids[j];
                    j--;
                }
                ws[j + 1] = wKey;
                ids[j + 1] = idKey;
            }

            // Truncate
            if (total > maxCount) total = maxCount;

            // Sort by sourceId ascending for determinism
            for (int i = 1; i < total; i++)
            {
                int idKey = ids[i];
                float wKey = ws[i];
                int j = i - 1;
                while (j >= 0 && ids[j] > idKey)
                {
                    ids[j + 1] = ids[j];
                    ws[j + 1] = ws[j];
                    j--;
                }
                ids[j + 1] = idKey;
                ws[j + 1] = wKey;
            }
        }

        public void CoalesceAndNormalize()
        {
            unsafe
            {
                int* ids = stackalloc int[4];
                float* ws = stackalloc float[4];
                int total = 0;

                if (Count > 0) { ids[total] = Src0; ws[total] = W0; total++; }
                if (Count > 1) { ids[total] = Src1; ws[total] = W1; total++; }
                if (Count > 2) { ids[total] = Src2; ws[total] = W2; total++; }
                if (Count > 3) { ids[total] = Src3; ws[total] = W3; total++; }

                CoalesceSortTruncate(ids, ws, ref total, 4);

                Src0 = Src1 = Src2 = Src3 = 0;
                W0 = W1 = W2 = W3 = 0f;
                Count = (byte)total;

                if (total > 0) { Src0 = ids[0]; W0 = ws[0]; }
                if (total > 1) { Src1 = ids[1]; W1 = ws[1]; }
                if (total > 2) { Src2 = ids[2]; W2 = ws[2]; }
                if (total > 3) { Src3 = ids[3]; W3 = ws[3]; }
            }

            Normalize();
        }

        private void Normalize()
        {
            float sum = 0f;
            if (Count > 0) sum += W0;
            if (Count > 1) sum += W1;
            if (Count > 2) sum += W2;
            if (Count > 3) sum += W3;

            if (float.IsNaN(sum) || float.IsInfinity(sum) || sum <= 0f)
            {
                // Fallback: dominant source with weight 1.0
                if (Count > 0)
                {
                    W0 = 1.0f;
                    W1 = W2 = W3 = 0f;
                    Count = 1;
                }
                return;
            }

            float invSum = 1.0f / sum;
            if (Count > 0) W0 *= invSum;
            if (Count > 1) W1 *= invSum;
            if (Count > 2) W2 *= invSum;
            if (Count > 3) W3 *= invSum;
        }
    }

    public struct ProvenanceMap : IDisposable
    {
        public NativeArray<ProvenanceRecord> Records;
        public int SourcePointCount;
        public int OutputPointCount;

        public bool IsCreated => Records.IsCreated;

        public ProvenanceMap(int outputPointCount, int sourcePointCount, Allocator allocator)
        {
            Records = new NativeArray<ProvenanceRecord>(outputPointCount, allocator);
            SourcePointCount = sourcePointCount;
            OutputPointCount = outputPointCount;
        }

        public void Dispose()
        {
            if (Records.IsCreated)
                Records.Dispose();
        }
    }
}
