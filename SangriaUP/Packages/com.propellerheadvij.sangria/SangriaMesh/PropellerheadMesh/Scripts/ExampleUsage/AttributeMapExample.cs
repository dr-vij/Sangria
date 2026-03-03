using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace PropellerheadMesh
{
    public unsafe class AttributeMapExample : MonoBehaviour
    {
        void Start()
        {
            PointerExample();
            AccessorExample();
        }

        private void PointerExample()
        {
            var attributeMap = new AttributeMap(1, Allocator.Temp);

            // Register positions for 100 vertices
            attributeMap.RegisterAttribute<float3>(0, 100);

            // Fill position data using a pointer
            var posPtr = attributeMap.GetBasePointerUnchecked<float3>(0);
            for (int i = 0; i < 100; i++)
                posPtr[i] = new float3(i * 0.1f, math.sin(i * 0.1f), 0);

            // Read some positions
            for (int i = 0; i < 10; i++)
                Debug.Log($"Position[{i}]: {posPtr[i]}");

            // Modify positions directly
            posPtr[50] = new float3(999, 888, 777);
            Debug.Log($"Modified Position[50]: {posPtr[50]}");

            attributeMap.Dispose();
        }

        private void AccessorExample()
        {
            var attributeMap = new AttributeMap(1, Allocator.Temp);

            // Register UV coordinates
            attributeMap.RegisterAttribute<float2>(0, 100);

            // Fill UV data using accessor
            var uvAccessor = attributeMap.GetAccessorUnchecked<float2>(0);
            for (int i = 0; i < 100; i++)
                uvAccessor[i] = new float2(i / 10f, (i % 10) / 10f);

            // Read some UVs
            for (int i = 0; i < 10; i++)
                Debug.Log($"UV[{i}]: {uvAccessor[i]}");

            // Modify UV coordinates through accessor
            uvAccessor[25] = new float2(0.5f, 0.5f);
            uvAccessor[75].x = 0.123f;

            Debug.Log($"Modified UV[25]: {uvAccessor[25]}");
            Debug.Log($"Modified UV[75]: {uvAccessor[75]}");

            attributeMap.Dispose();
        }
    }
}