using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using static UnityEngine.Mesh;

namespace ViJMeshTools
{
    public struct MeshVolumeData
    {
        public float Volume;
        public float3 LocalCenterOfMass;

        public override string ToString() => $"Volume: {Volume}, CenterOfMass: {LocalCenterOfMass}";
    }

    public static partial class MeshAnalizers
    {
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        public struct MeshVolumeJob : IJob, IDisposable
        {
            private MeshData mInitialMeshData;

            private bool mUseTRS;
            private float4x4 mToWorldTRS;
            private NativeArray<MeshVolumeData> mResult;

            public MeshVolumeData Result => mResult[0];

            public void InitializeJob(MeshData initialMeshData)
            {
                mUseTRS = false;
                mInitialMeshData = initialMeshData;

                mResult = new NativeArray<MeshVolumeData>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            }

            public void InitializeJob(MeshData initialMeshData, float4x4 toWorldTRS)
            {
                mUseTRS = true;
                mToWorldTRS = toWorldTRS;
                mInitialMeshData = initialMeshData;

                mResult = new NativeArray<MeshVolumeData>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            }

            //https://people.sc.fsu.edu/~jburkardt/presentations/cg_lab_tetrahedrons.pdf
            //http://chenlab.ece.cornell.edu/Publication/Cha/icip01_Cha.pdf
            public void Execute()
            {
                var submeshCount = mInitialMeshData.subMeshCount;
                var vertices = new NativeArray<float3>(mInitialMeshData.vertexCount, Allocator.Temp);
                mInitialMeshData.GetVertices(vertices.Reinterpret<Vector3>());
                var volume = 0.0f;
                var centerOfMass = float3.zero;
                for (int i = 0; i < submeshCount; i++)
                {
                    var indicies = new NativeArray<int>(mInitialMeshData.GetSubMesh(i).indexCount, Allocator.Temp);
                    var indexCount = indicies.Length;
                    mInitialMeshData.GetIndices(indicies, i, true);
                    for (int j = 0; j < indexCount; j += 3)
                    {
                        float3 vertA;
                        float3 vertB;
                        float3 vertC;
                        if (mUseTRS)
                        {
                            vertA = math.mul(mToWorldTRS, new float4(vertices[indicies[j]], 1)).xyz;
                            vertB = math.mul(mToWorldTRS, new float4(vertices[indicies[j + 1]], 1)).xyz;
                            vertC = math.mul(mToWorldTRS, new float4(vertices[indicies[j + 2]], 1)).xyz;
                        }
                        else
                        {
                            vertA = vertices[indicies[j]];
                            vertB = vertices[indicies[j + 1]];
                            vertC = vertices[indicies[j + 2]];
                        }
                        //http://chenlab.ece.cornell.edu/Publication/Cha/icip01_Cha.pdf (formula 6)
                        var normal = math.cross(vertB - vertC, vertB - vertA);
                        var sign = math.dot(vertA, normal);
                        var volumeOACB = (-vertC.x * vertB.y * vertA.z + vertB.x * vertC.y * vertA.z + vertC.x * vertA.y * vertB.z
                                         - vertA.x * vertC.y * vertB.z - vertB.x * vertA.y * vertC.z + vertA.x * vertB.y * vertC.z) / 6;
                        var centroid = (vertA + vertB + vertC) / 4;

                        if (sign > 0)
                        {
                            volume += volumeOACB;
                            centerOfMass += centroid;
                        }
                        else
                        {
                            volume -= volumeOACB;
                            centerOfMass -= centroid;
                        }
                    }
                    indicies.Dispose();
                }

                mResult[0] = new MeshVolumeData()
                {
                    LocalCenterOfMass = centerOfMass,
                    Volume = volume,
                };
            }

            public void Dispose()
            {
                mResult.Dispose();
            }
        }
    }
}