using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Burst;
using static UnityEngine.Mesh;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System;
using UnityEngine.Rendering;

namespace ViJMeshTools
{
    public static partial class MeshCutter
    {
        /// <summary>
        /// The order of incoming attributes from GetVertexBufferData is:
        /// VertexAttribute.Position
        /// VertexAttribute.Normal
        /// VertexAttribute.Tangent
        /// VertexAttribute.Color
        /// VertexAttribute.TexCoord0, ..., VertexAttribute.TexCoord7
        /// VertexAttribute.BlendWeight
        /// VertexAttribute.BlendIndices.
        /// </summary>
        public struct VertexBufferData
        {
            public float3 Position;
            public float3 Normal;
            public float4 Tangent;
            public float2 TexCoord0;
        }

        //TODO:
        //Find the way to remove copypaste codes (bounds and top/bottom sections)
        //Find the way to simply implement ushort and int indicies
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        public struct CutMeshWithPlaneJob : IJob, IDisposable
        {
            //Initial cut data
            [ReadOnly] private FloatPlane mPlane;
            [ReadOnly] private MeshData mInitialMeshData;

            private NativeArray<int> mTriangle;
            private NativeArray<float> mSignedDistanceToPlane;
            private NativeArray<int> mOppositeSideCounter;

            [ReadOnly] private NativeArray<SubMeshDescriptor> mInitialSubmeshes;

            //RETURN DATA
            [WriteOnly] public NativeList<VertexBufferData> PositiveVertices;
            [WriteOnly] public NativeList<int> PositiveIndicies;
            [WriteOnly] public NativeArray<SubMeshDescriptor> PositiveSubmeshes;

            [WriteOnly] public NativeList<VertexBufferData> NegativeVertices;
            [WriteOnly] public NativeList<int> NegativeIndicies;
            [WriteOnly] public NativeArray<SubMeshDescriptor> NegativeSubmeshes;

            [WriteOnly] public NativeHashSet<CutSegment> CutSegments;

            public CutMeshWithPlaneJob(MeshData initialMesh, Plane plane, Transform initialMeshTransform = null)
            {
                mPlane = plane.ToFloatPlane();
                if (initialMeshTransform != null)
                    mPlane = mPlane.GetTransformedPlane(initialMeshTransform.worldToLocalMatrix);

                mInitialMeshData = initialMesh;

                //Initialize positive mesh to return
                PositiveVertices = new NativeList<VertexBufferData>(Allocator.Persistent);
                PositiveIndicies = new NativeList<int>(Allocator.Persistent);
                PositiveSubmeshes = new NativeArray<SubMeshDescriptor>(mInitialMeshData.subMeshCount, Allocator.Persistent);

                NegativeVertices = new NativeList<VertexBufferData>(Allocator.Persistent);
                NegativeIndicies = new NativeList<int>(Allocator.Persistent);
                NegativeSubmeshes = new NativeArray<SubMeshDescriptor>(mInitialMeshData.subMeshCount, Allocator.Persistent);

                CutSegments = new NativeHashSet<CutSegment>(100, Allocator.Persistent);

                mTriangle = new NativeArray<int>(3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                mSignedDistanceToPlane = new NativeArray<float>(3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                mOppositeSideCounter = new NativeArray<int>(3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                mInitialSubmeshes = new NativeArray<SubMeshDescriptor>(mInitialMeshData.subMeshCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < mInitialMeshData.subMeshCount; i++)
                    mInitialSubmeshes[i] = mInitialMeshData.GetSubMesh(i);
            }

            public void Execute()
            {
                //reading the initial mesh
                var initialVertices = mInitialMeshData.GetVertexData<VertexBufferData>(0);
                var initialIndicies = mInitialMeshData.GetIndexData<int>();

                //allocate native lists for return
                var capacity = mInitialMeshData.vertexCount;
                PositiveVertices.Capacity = capacity;
                PositiveIndicies.Capacity = capacity;
                NegativeVertices.Capacity = capacity;
                NegativeIndicies.Capacity = capacity;

                var rawEdgesCounter = 0;

                //we use these counters to be able to use WriteOnly NativeLists
                var positiveVertCounter = 0;
                var negativeVertCounter = 0;
                var positiveIndexCounter = 0;
                var negativeIndexCounter = 0;

                for (int submeshIndex = 0; submeshIndex < mInitialSubmeshes.Length; submeshIndex++)
                {
                    var currentSubmesh = mInitialSubmeshes[submeshIndex];

                    var firstPositiveVertex = positiveVertCounter;
                    var firstNegativeVertex = negativeVertCounter;
                    var firstPositiveIndex = positiveIndexCounter;
                    var firstNegativeIndex = negativeIndexCounter;
                    int positiveCounter = 0;
                    int negativeCounter = 0;

                    var indexFrom = currentSubmesh.indexStart;
                    var indexTo = currentSubmesh.indexStart + currentSubmesh.indexCount;

                    bool positiveBoundsInited = false;
                    Bounds positiveBounds = new Bounds();
                    bool negativeBoundsInited = false;
                    Bounds negativeBounds = new Bounds();

                    for (int i = indexFrom; i < indexTo; i += 3)
                    {
                        var positiveTrianglePointsCounter = 0;
                        var negativeTrianglePointsCounter = 0;

                        //reading the triangle
                        for (int j = 0; j < 3; j++)
                        {
                            mTriangle[j] = initialIndicies[i + j] + currentSubmesh.baseVertex;
                            mSignedDistanceToPlane[j] = mPlane.SignedDistanceToPoint(initialVertices[mTriangle[j]].Position);
                            mOppositeSideCounter[j] = 0;

                            if (mSignedDistanceToPlane[j] < 0)
                                negativeTrianglePointsCounter++;
                            else
                                positiveTrianglePointsCounter++;
                        }

                        // if triangle is not intersected and totaly positive
                        if (negativeTrianglePointsCounter == 0)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveVertices.Add(initialVertices[mTriangle[j]]);
                                positiveVertCounter++;
                                positiveIndexCounter++;

                                if (!positiveBoundsInited)
                                {
                                    positiveBounds = new Bounds(initialVertices[mTriangle[j]].Position, Vector3.zero);
                                    positiveBoundsInited = true;
                                }
                                else
                                {
                                    positiveBounds.Encapsulate(initialVertices[mTriangle[j]].Position);
                                }
                            }
                        }
                        //if triangle is not intersected and totally negative
                        else if (positiveTrianglePointsCounter == 0)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeVertices.Add(initialVertices[mTriangle[j]]);
                                negativeVertCounter++;
                                negativeIndexCounter++;

                                if (!negativeBoundsInited)
                                {
                                    negativeBounds = new Bounds(initialVertices[mTriangle[j]].Position, Vector3.zero);
                                    negativeBoundsInited = true;
                                }
                                else
                                {
                                    negativeBounds.Encapsulate(initialVertices[mTriangle[j]].Position);
                                }
                            }
                        }
                        else //the triangle was intersected
                        {
                            //find the top point of triangle and its two bottoms
                            for (int j = 0; j < 3; j++)
                            {
                                //check if they are in opposite directions from the plane
                                var nextIndex = (j + 1) % 3;
                                if ((mSignedDistanceToPlane[j] >= 0) != (mSignedDistanceToPlane[nextIndex] >= 0))
                                {
                                    mOppositeSideCounter[j]++;
                                    mOppositeSideCounter[nextIndex]++;
                                }
                            }

                            //we take the initial triangle and rotate it in such way, that first vertex is always on the one side of the plane
                            //and two others are on the other
                            for (int j = 0; j < 3; j++)
                            {
                                if (mOppositeSideCounter[j] == 2)
                                {
                                    //we rotate the indices
                                    var sideA = mTriangle[j];
                                    var sideB1 = mTriangle[(j + 1) % 3];
                                    var sideB2 = mTriangle[(j + 2) % 3];
                                    mTriangle[0] = sideA;
                                    mTriangle[1] = sideB1;
                                    mTriangle[2] = sideB2;

                                    //and stored distances, cause we need them in future
                                    var signedDistA = mSignedDistanceToPlane[j];
                                    var signedDistB1 = mSignedDistanceToPlane[(j + 1) % 3];
                                    var signedDistB2 = mSignedDistanceToPlane[(j + 2) % 3];
                                    mSignedDistanceToPlane[0] = signedDistA;
                                    mSignedDistanceToPlane[1] = signedDistB1;
                                    mSignedDistanceToPlane[2] = signedDistB2;
                                    break;
                                }
                            }

                            //Now cut the triangle
                            var aVertex = initialVertices[mTriangle[0]];
                            var b1Vertex = initialVertices[mTriangle[1]];
                            var b2Vertex = initialVertices[mTriangle[2]];
                            mPlane.IntersectWithLine(aVertex.Position, b1Vertex.Position, out var AB1Intersection);
                            mPlane.IntersectWithLine(aVertex.Position, b2Vertex.Position, out var AB2Intersection);

                            //we calc lerp val by precalculated distances instead of calculating actual distances between points and intersections :) 
                            var aSignedDistanceToPlane = mSignedDistanceToPlane[0];
                            var DistToPlaneA = math.abs(aSignedDistanceToPlane);
                            var AB1lerpVal = DistToPlaneA / (DistToPlaneA + math.abs(mSignedDistanceToPlane[1]));
                            var AB2lerpVal = DistToPlaneA / (DistToPlaneA + math.abs(mSignedDistanceToPlane[2]));

                            var AB1Vertex = new VertexBufferData()
                            {
                                Position = AB1Intersection,
                                Normal = math.lerp(aVertex.Normal, b1Vertex.Normal, AB1lerpVal),
                                Tangent = math.lerp(aVertex.Tangent, b1Vertex.Tangent, AB1lerpVal),
                                TexCoord0 = math.lerp(aVertex.TexCoord0, b1Vertex.TexCoord0, AB1lerpVal)
                            };

                            var AB2Vertex = new VertexBufferData()
                            {
                                Position = AB2Intersection,
                                Normal = math.lerp(aVertex.Normal, b2Vertex.Normal, AB2lerpVal),
                                Tangent = math.lerp(aVertex.Tangent, b2Vertex.Tangent, AB2lerpVal),
                                TexCoord0 = math.lerp(aVertex.TexCoord0, b2Vertex.TexCoord0, AB2lerpVal),
                            };

                            if (aSignedDistanceToPlane >= 0)
                                CutSegments.Add(new CutSegment(AB2Intersection, AB1Intersection, rawEdgesCounter++));
                            else
                                CutSegments.Add(new CutSegment(AB1Intersection, AB2Intersection, rawEdgesCounter++));

                            //The first triangle is positive, two others are negative
                            if (aSignedDistanceToPlane >= 0)
                            {
                                #region the triangle to the one side of the plane
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveVertices.Add(aVertex);
                                PositiveVertices.Add(AB1Vertex);
                                PositiveVertices.Add(AB2Vertex);
                                positiveIndexCounter += 3;
                                positiveVertCounter += 3;
                                #endregion

                                #region positive bounds
                                if (!positiveBoundsInited)
                                {
                                    positiveBounds = new Bounds(aVertex.Position, Vector3.zero);
                                    positiveBoundsInited = true;
                                }
                                else
                                {
                                    positiveBounds.Encapsulate(aVertex.Position);
                                }
                                positiveBounds.Encapsulate(AB1Vertex.Position);
                                positiveBounds.Encapsulate(AB2Vertex.Position);
                                #endregion

                                #region the triangles to another side of the plane
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeIndicies.Add(negativeCounter++);

                                NegativeVertices.Add(AB1Vertex);
                                NegativeVertices.Add(b1Vertex);
                                NegativeVertices.Add(b2Vertex);

                                NegativeVertices.Add(AB1Vertex);
                                NegativeVertices.Add(b2Vertex);
                                NegativeVertices.Add(AB2Vertex);
                                negativeIndexCounter += 6;
                                negativeVertCounter += 6;
                                #endregion

                                #region negative bounds
                                if (!negativeBoundsInited)
                                {
                                    negativeBounds = new Bounds(AB1Vertex.Position, Vector3.zero);
                                    negativeBoundsInited = true;
                                }
                                else
                                {
                                    negativeBounds.Encapsulate(AB1Vertex.Position);
                                }
                                negativeBounds.Encapsulate(b1Vertex.Position);
                                negativeBounds.Encapsulate(b2Vertex.Position);
                                negativeBounds.Encapsulate(AB2Vertex.Position);
                                #endregion
                            }
                            //The first triangle is negative, two others are positive
                            else if (aSignedDistanceToPlane < 0)
                            {
                                #region the triangle to the one side of the plane
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeIndicies.Add(negativeCounter++);
                                NegativeVertices.Add(aVertex);
                                NegativeVertices.Add(AB1Vertex);
                                NegativeVertices.Add(AB2Vertex);
                                negativeIndexCounter += 3;
                                negativeVertCounter += 3;
                                #endregion 

                                #region negative bounds
                                if (!negativeBoundsInited)
                                {
                                    negativeBounds = new Bounds(aVertex.Position, Vector3.zero);
                                    negativeBoundsInited = true;
                                }
                                else
                                {
                                    negativeBounds.Encapsulate(aVertex.Position);
                                }
                                negativeBounds.Encapsulate(AB1Vertex.Position);
                                negativeBounds.Encapsulate(AB2Vertex.Position);
                                #endregion

                                #region the triangles to another side of the plane
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveVertices.Add(AB1Vertex);
                                PositiveVertices.Add(b1Vertex);
                                PositiveVertices.Add(b2Vertex);

                                PositiveIndicies.Add(positiveCounter++);
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveIndicies.Add(positiveCounter++);
                                PositiveVertices.Add(AB1Vertex);
                                PositiveVertices.Add(b2Vertex);
                                PositiveVertices.Add(AB2Vertex);
                                positiveIndexCounter += 6;
                                positiveVertCounter += 6;
                                #endregion

                                #region positive bounds
                                if (!positiveBoundsInited)
                                {
                                    positiveBounds = new Bounds(AB1Vertex.Position, Vector3.zero);
                                    positiveBoundsInited = true;
                                }
                                else
                                {
                                    positiveBounds.Encapsulate(AB1Vertex.Position);
                                }
                                positiveBounds.Encapsulate(b1Vertex.Position);
                                positiveBounds.Encapsulate(b2Vertex.Position);
                                positiveBounds.Encapsulate(AB2Vertex.Position);
                                #endregion
                            }
                        }
                    }

                    #region Writing information about resulting submeshes
                    PositiveSubmeshes[submeshIndex] = new SubMeshDescriptor()
                    {
                        indexStart = firstPositiveIndex,
                        firstVertex = firstPositiveVertex,
                        baseVertex = firstPositiveVertex,

                        indexCount = positiveCounter,
                        vertexCount = positiveCounter,
                        bounds = positiveBounds,
                        topology = MeshTopology.Triangles,
                    };

                    NegativeSubmeshes[submeshIndex] = new SubMeshDescriptor()
                    {
                        indexStart = firstNegativeIndex,
                        firstVertex = firstNegativeVertex,
                        baseVertex = firstNegativeVertex,

                        indexCount = negativeCounter,
                        vertexCount = negativeCounter,
                        bounds = negativeBounds,
                        topology = MeshTopology.Triangles,
                    };
                    #endregion
                }
            }

            public void Dispose()
            {
                //Disposing inside job data 
                mTriangle.Dispose();
                mSignedDistanceToPlane.Dispose();
                mOppositeSideCounter.Dispose();

                //Disposing result
                PositiveIndicies.Dispose();
                PositiveVertices.Dispose();
                PositiveSubmeshes.Dispose();

                NegativeIndicies.Dispose();
                NegativeVertices.Dispose();
                NegativeSubmeshes.Dispose();

                CutSegments.Dispose();
                mInitialSubmeshes.Dispose();
            }
        }
    }
}