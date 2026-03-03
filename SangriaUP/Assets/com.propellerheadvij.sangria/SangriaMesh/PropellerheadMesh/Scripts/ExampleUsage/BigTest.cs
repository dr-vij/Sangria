using UnityEngine;
using Unity.Mathematics;
using ViJMeshTools;
using Legacy;
using Unity.Collections;
using Unity.Jobs;

public class BigTest : MonoBehaviour
{
    void Update()
    {
        Debug.Log("-------------------------------");
        Stopwatcher.ResetAll();
        CheckNativeVersion();
    }

    static unsafe void CheckNativeVersion()
    {
        const int vertexCount = 1024 * 1024;
        
        Stopwatcher.Start("NativeMeshDataCreation");
        var meshData = new PropellerheadMesh.MeshData(vertexCount, Allocator.TempJob);
        
        var posPtr = meshData.AttributeMap.GetBasePointerUnchecked<float3>(0);

        for (int i = 0; i < vertexCount; i++)
            posPtr[i] = new float3(i, i, i);
        Stopwatcher.Pause("NativeMeshDataCreation");
        
        Stopwatcher.Start("BurstJobCalculation");
        var job = new PropellerheadMesh.CalculateSumJob
        {
            AttributeMap = meshData.AttributeMap
        };
        
        var jobHandle = job.Schedule(vertexCount, 512);
        jobHandle.Complete();
        Stopwatcher.Pause("BurstJobCalculation");
        
        Debug.Log("Native Version Result:");
        Stopwatcher.DebugLogMicroseconds();
        
        // Read results
        var sumPtr = meshData.AttributeMap.GetBasePointerUnchecked<float>(1);
        for (int i = 0; i < 10; i++)
            Debug.Log($"Position[{i}]: {posPtr[i]}, Sum: {sumPtr[i]}");
        
        meshData.Dispose();
    }
}