using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.Mesh;

namespace ViJMeshTools
{
    public static partial class MeshAnalizers
    {
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        public struct MeshWatertightValidationJob : IJob, IDisposable
        {
            [WriteOnly] private NativeArray<bool> mIsMeshWatertightResult;

            private MeshData mMeshData;

            public bool IsMeshWatertight => mIsMeshWatertightResult[0];

            public void InitializeJob(MeshData meshData)
            {
                mMeshData = meshData;
                mIsMeshWatertightResult = new NativeArray<bool>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            }

            //The idea behind this algorithm is criteria, that each edge is part of two polygons. Otherwize it is a hole
            public void Execute()
            {
                var subMeshCount = mMeshData.subMeshCount;
                var edgeCounter = new NativeHashMap<Edge, int>(mMeshData.vertexCount, Allocator.Temp);
                var vertices = new NativeArray<float3>(mMeshData.vertexCount, Allocator.Temp);

                //now work for each submesh
                for (int i = 0; i < subMeshCount; i++)
                {
                    var submesh = mMeshData.GetSubMesh(i);
                    var indicies = new NativeArray<int>(submesh.indexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    mMeshData.GetIndices(indicies, i);
                    mMeshData.GetVertices(vertices.Reinterpret<Vector3>());
                    for (int pIndex = 0; pIndex < submesh.indexCount; pIndex += 3)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            var edge = new Edge(vertices[indicies[pIndex + (j % 3)]], vertices[indicies[pIndex + (j + 1) % 3]]);
                            if (edgeCounter.TryGetValue(edge, out var count))
                                edgeCounter[edge] = ++count;
                            else
                                edgeCounter[edge] = 1;
                        }
                    }

                    indicies.Dispose();
                    vertices.Dispose();
                }

                var edgeEnumerator = edgeCounter.GetEnumerator();
                mIsMeshWatertightResult[0] = true;
                while (edgeEnumerator.MoveNext())
                {
                    if (edgeEnumerator.Current.Value != 2)
                    {
                        mIsMeshWatertightResult[0] = false;
                        break;
                    }
                }
                edgeEnumerator.Dispose();
                edgeCounter.Dispose();
            }

            public void Dispose()
            {
                mIsMeshWatertightResult.Dispose();
            }
        }
    }
}