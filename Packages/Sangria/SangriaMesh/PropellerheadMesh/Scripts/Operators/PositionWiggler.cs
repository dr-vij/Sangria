using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Legacy.Misc;
using Unity.Collections.LowLevel.Unsafe;

namespace PropellerheadMesh
{
    /// <summary>
    /// Burst-compiled job for wiggling/shaking point positions using noise for NativeDetail
    /// </summary>
    [BurstCompile]
    public struct NativePositionWiggleJob : IJobParallelFor
    {
        // Input parameters
        [ReadOnly] public float NoiseScale;
        [ReadOnly] public float WiggleAmplitude;
        [ReadOnly] public float3 TimeMultiplier;
        [ReadOnly] public float3 NoiseOffset;
        [ReadOnly] public float Time;
        
        // Original positions (read-only)
        [ReadOnly] public NativeArray<float3> OriginalPositions;
        
        // Valid points mask (read-only)
        [ReadOnly] public NativeArray<bool> ValidPoints;
        
        // Current positions (read-write)
        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CurrentPositions;

        public void Execute(int index)
        {
            if (!ValidPoints[index])
                return;

            var originalPos = OriginalPositions[index];
            var wiggledPos = CalculateWiggledPosition(originalPos, Time);
            CurrentPositions[index] = wiggledPos;
        }

        private float3 CalculateWiggledPosition(float3 originalPos, float time)
        {
            // Generate 3D noise for each axis using Legacy.Misc.Noises
            var noiseX = Noises.Noise3D(
                originalPos.x * NoiseScale + time * TimeMultiplier.x, 
                originalPos.y * NoiseScale + time * NoiseOffset.x,
                originalPos.z * NoiseScale
            );
    
            var noiseY = Noises.Noise3D(
                originalPos.y * NoiseScale + time * TimeMultiplier.y, 
                originalPos.z * NoiseScale + time * NoiseOffset.y,
                originalPos.x * NoiseScale
            );
    
            var noiseZ = Noises.Noise3D(
                originalPos.z * NoiseScale + time * TimeMultiplier.z, 
                originalPos.x * NoiseScale + time * NoiseOffset.z,
                originalPos.y * NoiseScale
            );

            // Apply wiggle offset
            var wiggleOffset = new float3(noiseX, noiseY, noiseZ) * WiggleAmplitude;
            return originalPos + wiggleOffset;
        }
    }

    /// <summary>
    /// Operator for wiggling/shaking point positions using noise with NativeDetail
    /// </summary>
    public class NativePositionWiggleOperator : System.IDisposable
    {
        private bool m_IsDisposed;
        
        // Store original positions to keep wiggling around the original shape
        private NativeArray<float3> m_OriginalPositions;
        private NativeArray<bool> m_ValidPoints;
        private NativeArray<float3> m_CurrentPositions;
        private bool m_IsInitialized;
        private int m_Capacity;
        
        // Wiggle parameters
        public float NoiseScale { get; set; } = 1f;
        public float WiggleAmplitude { get; set; } = 0.3f;
        public float3 TimeMultiplier { get; set; } = new float3(0.8f, 0.6f, 0.4f);
        public float3 NoiseOffset { get; set; } = new float3(0.3f, 0.9f, 0.7f);

        /// <summary>
        /// Initialize the wiggler with a NativeDetail. Stores original positions.
        /// </summary>
        /// <param name="detail">The NativeDetail to wiggle</param>
        public void Initialize(ref NativeDetail detail)
        {
            if (m_IsInitialized)
                Dispose();

            // Use point capacity instead of count for proper allocation
            m_Capacity = math.max(detail.PointCount, 128); // Minimum capacity
            
            // Allocate arrays
            m_OriginalPositions = new NativeArray<float3>(m_Capacity, Allocator.Persistent);
            m_ValidPoints = new NativeArray<bool>(m_Capacity, Allocator.Persistent);
            m_CurrentPositions = new NativeArray<float3>(m_Capacity, Allocator.Persistent);

            // Store original positions and validity
            StoreOriginalPositions(ref detail);
            m_IsInitialized = true;
        }

        /// <summary>
        /// Apply wiggle effect to all points in the NativeDetail
        /// </summary>
        /// <param name="detail">The NativeDetail to wiggle</param>
        /// <param name="time">Time value for animation (usually Time.time)</param>
        /// <param name="dependency">Job dependency</param>
        /// <returns>Job handle for the wiggle operation</returns>
        public JobHandle Apply(ref NativeDetail detail, float time, JobHandle dependency = default)
        {
            if (!m_IsInitialized)
            {
                Initialize(ref detail);
                if (!m_IsInitialized)
                    return dependency;
            }

            // Get position accessor
            if (detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor) 
                != AttributeMapResult.Success)
            {
                return dependency;
            }

            // Create and schedule the job
            var job = new NativePositionWiggleJob
            {
                NoiseScale = NoiseScale,
                WiggleAmplitude = WiggleAmplitude,
                TimeMultiplier = TimeMultiplier,
                NoiseOffset = NoiseOffset,
                Time = time,
                OriginalPositions = m_OriginalPositions,
                ValidPoints = m_ValidPoints,
                CurrentPositions = m_CurrentPositions
            };

            var handle = job.Schedule(m_Capacity, 64, dependency);
            
            // Schedule completion job to copy results back
            var copyJob = new CopyResultsJob
            {
                Results = m_CurrentPositions,
                ValidPoints = m_ValidPoints,
                PositionAccessor = positionAccessor
            };
            
            return copyJob.Schedule(handle);
        }

        /// <summary>
        /// Apply wiggle effect to specific points only
        /// </summary>
        /// <param name="detail">The NativeDetail to wiggle</param>
        /// <param name="pointIndices">Specific point indices to wiggle</param>
        /// <param name="time">Time value for animation</param>
        /// <param name="dependency">Job dependency</param>
        /// <returns>Job handle for the wiggle operation</returns>
        public JobHandle ApplyToPoints(ref NativeDetail detail, NativeArray<int> pointIndices, float time, JobHandle dependency = default)
        {
            if (!m_IsInitialized)
            {
                Initialize(ref detail);
                if (!m_IsInitialized)
                    return dependency;
            }

            // Get position accessor
            if (detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor) 
                != AttributeMapResult.Success)
            {
                return dependency;
            }

            // Create temporary valid points array for selected points only
            var tempValidPoints = new NativeArray<bool>(m_Capacity, Allocator.TempJob);
            
            // Mark only specified points as valid
            for (int i = 0; i < pointIndices.Length; i++)
            {
                int pointIndex = pointIndices[i];
                if (pointIndex >= 0 && pointIndex < m_Capacity)
                {
                    tempValidPoints[pointIndex] = m_ValidPoints[pointIndex];
                }
            }

            // Create and schedule the job
            var job = new NativePositionWiggleJob
            {
                NoiseScale = NoiseScale,
                WiggleAmplitude = WiggleAmplitude,
                TimeMultiplier = TimeMultiplier,
                NoiseOffset = NoiseOffset,
                Time = time,
                OriginalPositions = m_OriginalPositions,
                ValidPoints = tempValidPoints,
                CurrentPositions = m_CurrentPositions
            };

            var handle = job.Schedule(m_Capacity, 64, dependency);
            
            // Schedule cleanup and copy job
            var copyJob = new CopyResultsJob
            {
                Results = m_CurrentPositions,
                ValidPoints = tempValidPoints,
                PositionAccessor = positionAccessor
            };
            
            var copyHandle = copyJob.Schedule(handle);
            
            // Schedule cleanup job
            var cleanupJob = new DisposeJob<NativeArray<bool>>
            {
                ToDispose = tempValidPoints
            };
            
            return cleanupJob.Schedule(copyHandle);
        }

        /// <summary>
        /// Reset all points to their original positions
        /// </summary>
        /// <param name="detail">The NativeDetail to reset</param>
        public void Reset(ref NativeDetail detail)
        {
            if (!m_IsInitialized)
                return;

            if (detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor) 
                != AttributeMapResult.Success)
            {
                return;
            }

            // Copy original positions back to detail
            for (int i = 0; i < m_Capacity; i++)
            {
                if (m_ValidPoints[i])
                {
                    positionAccessor[i] = m_OriginalPositions[i];
                }
            }
        }

        /// <summary>
        /// Clear stored original positions. Call this when the detail structure changes.
        /// </summary>
        public void ClearOriginalPositions()
        {
            if (m_IsInitialized)
            {
                Dispose();
            }
        }

        /// <summary>
        /// Update original positions for new or changed points
        /// </summary>
        /// <param name="detail">The NativeDetail to update from</param>
        public void UpdateOriginalPositions(ref NativeDetail detail)
        {
            if (!m_IsInitialized)
            {
                Initialize(ref detail);
                return;
            }

            StoreOriginalPositions(ref detail);
        }

        private void StoreOriginalPositions(ref NativeDetail detail)
        {
            if (detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor) 
                != AttributeMapResult.Success)
            {
                return;
            }

            // Get valid points
            var validPoints = new NativeList<int>(Allocator.Temp);
            detail.GetAllValidPoints(validPoints);

            // Clear validity array
            for (int i = 0; i < m_Capacity; i++)
            {
                m_ValidPoints[i] = false;
            }

            // Store original positions
            for (int i = 0; i < validPoints.Length; i++)
            {
                int pointIndex = validPoints[i];
                if (pointIndex < m_Capacity)
                {
                    m_OriginalPositions[pointIndex] = positionAccessor[pointIndex];
                    m_ValidPoints[pointIndex] = true;
                }
            }

            validPoints.Dispose();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            if (m_OriginalPositions.IsCreated)
                m_OriginalPositions.Dispose();
            if (m_ValidPoints.IsCreated)
                m_ValidPoints.Dispose();
            if (m_CurrentPositions.IsCreated)
                m_CurrentPositions.Dispose();

            m_IsInitialized = false;
            m_IsDisposed = true;
        }
    }

    /// <summary>
    /// Job for copying results back to the detail
    /// </summary>
    [BurstCompile]
    public struct CopyResultsJob : IJob
    {
        [ReadOnly] public NativeArray<float3> Results;
        [ReadOnly] public NativeArray<bool> ValidPoints;
        [NativeDisableUnsafePtrRestriction] public NativeAttributeAccessor<float3> PositionAccessor;

        public void Execute()
        {
            for (int i = 0; i < Results.Length; i++)
            {
                if (ValidPoints[i])
                {
                    PositionAccessor[i] = Results[i];
                }
            }
        }
    }

    /// <summary>
    /// Generic job for disposing native containers
    /// </summary>
    [BurstCompile]
    public struct DisposeJob<T> : IJob where T : struct, System.IDisposable
    {
        public T ToDispose;

        public void Execute()
        {
            ToDispose.Dispose();
        }
    }
}