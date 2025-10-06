using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;
using ViJMeshTools;

namespace PropellerheadMesh
{
    public struct MeshData
    {
        public AttributeMap AttributeMap;
        
        public MeshData(int vertexCount, Allocator allocator)
        {
            AttributeMap = new AttributeMap(2, allocator);
            AttributeMap.RegisterAttribute<float3>(0, vertexCount); // positions
            AttributeMap.RegisterAttribute<float>(1, vertexCount);  // sums
        }
        
        public void Dispose()
        {
            AttributeMap.Dispose();
        }
    }

    [BurstCompile]
    public unsafe struct CalculateSumJob : IJobParallelFor
    {
        [ReadOnly] public AttributeMap AttributeMap;
        
        public void Execute(int index)
        {
            var posPtr = AttributeMap.GetPointerUnchecked<float3>(0, index);
            var sumPtr = AttributeMap.GetPointerUnchecked<float>(1, index);
            
            float3 pos = *posPtr;
            *sumPtr = pos.x + pos.y + pos.z;
        }
    }

    public unsafe class BurstJobExample : MonoBehaviour
    {
        void Update()
        {
            const int vertexCount = 1024 * 1024;
            
            Stopwatcher.ResetAll();
            Stopwatcher.Start("MeshDataCreation");
            var meshData = new MeshData(vertexCount, Allocator.TempJob);
            
            var posPtr = meshData.AttributeMap.GetBasePointerUnchecked<float3>(0);

            for (int i = 0; i < vertexCount; i++)
                posPtr[i] = new float3(i, i, i);
            Stopwatcher.Pause("MeshDataCreation");
            
            Stopwatcher.Start("BurstJob");
            var job = new CalculateSumJob
            {
                AttributeMap = meshData.AttributeMap
            };
            
            var jobHandle = job.Schedule(vertexCount, 512);
            jobHandle.Complete();
            Stopwatcher.Pause("BurstJob");
            Stopwatcher.DebugLogMicroseconds();
            
            // Read results
            var sumPtr = meshData.AttributeMap.GetBasePointerUnchecked<float>(1);
            for (int i = 0; i < 10; i++)
                Debug.Log($"Position[{i}]: {posPtr[i]}, Sum: {sumPtr[i]}");
            
            meshData.Dispose();
        }
    }
}