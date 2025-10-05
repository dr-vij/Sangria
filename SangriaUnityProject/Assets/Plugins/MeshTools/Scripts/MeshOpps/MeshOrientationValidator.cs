using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.Mesh;

namespace ViJMeshTools
{
    public partial class MeshAnalizers
    {
        /// <summary>
        /// The Idea of this algorithm is to rotate mesh in different ways and check its volume.
        /// If volume is orientation independent it means that mesh is watertight
        /// </summary>
        /// <param name="meshData"></param>
        /// <param name="inverseTolerance"></param>
        /// <returns></returns>
        public static bool IsMeshVolumeOrientationIndependent(MeshData meshData, float inverseTolerance = 1e3f)
        {
            var volumeMinMax = new float2(float.PositiveInfinity, float.NegativeInfinity);

            var checksCount = 8;
            var volumeJobs = new MeshVolumeJob[checksCount];
            var volumeJobHandles = new NativeArray<JobHandle>(checksCount, Allocator.Temp);

            var rotations = new NativeArray<quaternion>(checksCount, Allocator.Temp);
            rotations[0] = quaternion.AxisAngle(new float3(0, 1, 0), 0);                                //top
            rotations[1] = quaternion.AxisAngle(new float3(0, 0, 1), math.PI);                          //bottom
            rotations[2] = quaternion.AxisAngle(new float3(0, 0, 1), math.PI / 2);                      //right
            rotations[3] = quaternion.AxisAngle(new float3(0, 0, 1), -math.PI / 2);                     //left
            rotations[4] = quaternion.AxisAngle(new float3(1, 0, 0), math.PI / 2);                      //forward
            rotations[5] = quaternion.AxisAngle(new float3(1, 0, 0), -math.PI / 2);                     //backward
            rotations[6] = quaternion.AxisAngle(math.normalize(new float3(1, 1, 1)), math.PI / 4);      //diagonal positive
            rotations[7] = quaternion.AxisAngle(math.normalize(new float3(1, 1, 1)), -math.PI / 4);     //diagonal negative

            var zeroPosition = new float3(0, 0, 0);
            var oneScale = new float3(1, 1, 1);

            //shedule jobs and wait for there results
            for (int i = 0; i < checksCount; i++)
            {
                volumeJobs[i] = new MeshVolumeJob();
                volumeJobs[i].InitializeJob(meshData, float4x4.TRS(zeroPosition, rotations[i], oneScale));
                volumeJobHandles[i] = volumeJobs[i].Schedule();
            }
            JobHandle.CombineDependencies(volumeJobHandles).Complete();

            //get the result and dispose all containers
            for (int i = 0; i < volumeJobs.Length; i++)
            {
                var result = volumeJobs[i].Result;
                if (result.Volume < volumeMinMax.x)
                    volumeMinMax.x = result.Volume;
                if (result.Volume > volumeMinMax.y)
                    volumeMinMax.y = result.Volume;
                //dispose the result. we don't need it anymore
                volumeJobs[i].Dispose();
            }
            volumeJobHandles.Dispose();
            rotations.Dispose();

            //volume is negative. something wrong with normals or object is not manifold
            if (volumeMinMax.x < 0 || volumeMinMax.y < 0)
                return false;

            var assertionDelta = volumeMinMax.x / inverseTolerance;
            //check if all results are close enought, if difference is too big the mesh is not manifold
            return (volumeMinMax.y - volumeMinMax.x) < assertionDelta;
        }
    }
}