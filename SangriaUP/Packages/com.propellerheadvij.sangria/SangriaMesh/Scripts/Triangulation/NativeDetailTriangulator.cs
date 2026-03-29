using Unity.Collections;
using Unity.Mathematics;

namespace SangriaMesh
{
    public enum TriangulationMode : byte
    {
        Fan = 0,
        EarClipping = 1,
        Tess = 2
    }

    /// <summary>
    /// Triangulates all N-gon primitives in a NativeDetail.
    /// Triangles (3-vertex primitives) are passed through unchanged.
    /// All domain attributes (point/vertex/primitive) and resources are transferred.
    /// </summary>
    public static class NativeDetailTriangulator
    {
        /// <summary>
        /// Triangulate all primitives in sourceDetail, producing a new NativeDetail
        /// with only triangle primitives. All point-domain attributes are transferred.
        /// </summary>
        /// <param name="sourceDetail">Input mesh with N-gon primitives (read-only usage)</param>
        /// <param name="outputDetail">Must be a fresh empty NativeDetail. Will contain triangulated result.</param>
        /// <param name="mode">Triangulation algorithm to use for N-gon primitives.</param>
        /// <param name="policy">Interpolation policy for attribute transfer.</param>
        /// <param name="tessOptions">Triangulation options for Tess mode (winding rule, orientation, normal).</param>
        /// <returns>CoreResult.Success or error code</returns>
        public static CoreResult Triangulate(
            ref NativeDetail sourceDetail,
            ref NativeDetail outputDetail,
            TriangulationMode mode = TriangulationMode.EarClipping,
            InterpolationPolicy policy = default,
            TriangulationOptions tessOptions = default)
        {
            if (outputDetail.PointCount != 0 || outputDetail.VertexCount != 0 || outputDetail.PrimitiveCount != 0)
                return CoreResult.InvalidOperation;

            if (sourceDetail.PrimitiveCount == 0)
                return CoreResult.Success;

            var allPrimitives = new NativeList<int>(sourceDetail.PrimitiveCount, Allocator.TempJob);
            sourceDetail.GetAllValidPrimitives(allPrimitives);

            if (allPrimitives.Length == 0)
            {
                allPrimitives.Dispose();
                return CoreResult.Success;
            }

            var trianglePrims = new NativeList<int>(allPrimitives.Length, Allocator.TempJob);
            var ngonPrims = new NativeList<int>(allPrimitives.Length, Allocator.TempJob);

            for (int i = 0; i < allPrimitives.Length; i++)
            {
                int primIdx = allPrimitives[i];
                int vertCount = sourceDetail.GetPrimitiveVertexCount(primIdx);
                if (vertCount < 3)
                {
                    allPrimitives.Dispose();
                    trianglePrims.Dispose();
                    ngonPrims.Dispose();
                    return CoreResult.InvalidOperation;
                }

                if (vertCount == 3)
                    trianglePrims.Add(primIdx);
                else if (vertCount > 3)
                    ngonPrims.Add(primIdx);
            }

            CoreResult result;
            if (mode == TriangulationMode.Tess)
                result = TriangulateTess(ref sourceDetail, ref outputDetail, trianglePrims, ngonPrims, policy, tessOptions);
            else
                result = TriangulateFanOrEarClip(ref sourceDetail, ref outputDetail, mode, trianglePrims, ngonPrims, policy);

            allPrimitives.Dispose();
            trianglePrims.Dispose();
            ngonPrims.Dispose();

            return result;
        }

        /// <summary>
        /// Convenience overload: triangulates in-place by creating a new detail and swapping.
        /// </summary>
        public static CoreResult TriangulateInPlace(
            ref NativeDetail detail,
            TriangulationMode mode = TriangulationMode.EarClipping,
            Allocator outputAllocator = Allocator.Persistent,
            InterpolationPolicy policy = default,
            TriangulationOptions tessOptions = default)
        {
            var output = new NativeDetail(
                detail.PointCount, detail.VertexCount, detail.PrimitiveCount,
                outputAllocator);

            CoreResult result = Triangulate(ref detail, ref output, mode, policy, tessOptions);
            if (result != CoreResult.Success)
            {
                output.Dispose();
                return result;
            }

            detail.Dispose();
            detail = output;
            return CoreResult.Success;
        }

        // ──────────────────────────────────────────────────────────────
        //  Fan / EarClipping path — preserves corner semantics
        // ──────────────────────────────────────────────────────────────

        static CoreResult TriangulateFanOrEarClip(
            ref NativeDetail sourceDetail,
            ref NativeDetail outputDetail,
            TriangulationMode mode,
            NativeList<int> trianglePrims,
            NativeList<int> ngonPrims,
            InterpolationPolicy policy)
        {
            // ── 1. Collect unique source points and build point mapping ──
            var usedSourcePoints = new NativeHashSet<int>(sourceDetail.PointCount, Allocator.TempJob);

            for (int i = 0; i < trianglePrims.Length; i++)
            {
                NativeSlice<int> verts = sourceDetail.GetPrimitiveVertices(trianglePrims[i]);
                for (int v = 0; v < verts.Length; v++)
                    usedSourcePoints.Add(sourceDetail.GetVertexPoint(verts[v]));
            }

            for (int i = 0; i < ngonPrims.Length; i++)
            {
                NativeSlice<int> verts = sourceDetail.GetPrimitiveVertices(ngonPrims[i]);
                for (int v = 0; v < verts.Length; v++)
                    usedSourcePoints.Add(sourceDetail.GetVertexPoint(verts[v]));
            }

            var usedPointsArray = usedSourcePoints.ToNativeArray(Allocator.TempJob);
            usedPointsArray.Sort();

            var sourceToOutputPoint = new NativeHashMap<int, int>(
                math.max(1, usedPointsArray.Length), Allocator.TempJob);

            for (int i = 0; i < usedPointsArray.Length; i++)
            {
                int srcPt = usedPointsArray[i];
                float3 pos = sourceDetail.GetPointPosition(srcPt);
                int outPt = outputDetail.AddPoint(pos);
                sourceToOutputPoint[srcPt] = outPt;
            }

            int totalOutputPoints = usedPointsArray.Length;

            // ── 2. Build provenance (identity — Fan/EarClip don't create new points) ──
            int maxSourcePointIndex = 0;
            for (int i = 0; i < usedPointsArray.Length; i++)
            {
                if (usedPointsArray[i] > maxSourcePointIndex)
                    maxSourcePointIndex = usedPointsArray[i];
            }

            var provenance = new ProvenanceMap(totalOutputPoints, maxSourcePointIndex + 1, Allocator.TempJob);
            for (int i = 0; i < usedPointsArray.Length; i++)
                provenance.Records[i] = ProvenanceRecord.Identity(usedPointsArray[i]);

            // ── 3. Build vertex mapping: source vertex → output vertex ──
            // Preserves corner semantics — each source vertex gets its own output vertex
            var sourceToOutputVertex = new NativeHashMap<int, int>(
                math.max(1, sourceDetail.VertexCount), Allocator.TempJob);

            // Count total source vertices used, for mapping arrays
            int totalSourceVerticesUsed = 0;

            for (int i = 0; i < trianglePrims.Length; i++)
            {
                NativeSlice<int> srcVerts = sourceDetail.GetPrimitiveVertices(trianglePrims[i]);
                for (int v = 0; v < srcVerts.Length; v++)
                {
                    int srcVert = srcVerts[v];
                    if (!sourceToOutputVertex.ContainsKey(srcVert))
                    {
                        int srcPoint = sourceDetail.GetVertexPoint(srcVert);
                        int outPoint = sourceToOutputPoint[srcPoint];
                        int outVert = outputDetail.AddVertex(outPoint);
                        sourceToOutputVertex[srcVert] = outVert;
                        totalSourceVerticesUsed++;
                    }
                }
            }

            for (int i = 0; i < ngonPrims.Length; i++)
            {
                NativeSlice<int> srcVerts = sourceDetail.GetPrimitiveVertices(ngonPrims[i]);
                for (int v = 0; v < srcVerts.Length; v++)
                {
                    int srcVert = srcVerts[v];
                    if (!sourceToOutputVertex.ContainsKey(srcVert))
                    {
                        int srcPoint = sourceDetail.GetVertexPoint(srcVert);
                        int outPoint = sourceToOutputPoint[srcPoint];
                        int outVert = outputDetail.AddVertex(outPoint);
                        sourceToOutputVertex[srcVert] = outVert;
                        totalSourceVerticesUsed++;
                    }
                }
            }

            // Build vertex source mapping array for attribute transfer
            var sourceVertexForOutput = new NativeArray<int>(totalSourceVerticesUsed, Allocator.TempJob);
            {
                var kvs = sourceToOutputVertex.GetKeyValueArrays(Allocator.TempJob);
                for (int i = 0; i < kvs.Keys.Length; i++)
                    sourceVertexForOutput[kvs.Values[i]] = kvs.Keys[i];
                kvs.Dispose();
            }

            // ── 4. Estimate total output primitives for primitive mapping ──
            int totalOutputPrims = trianglePrims.Length;
            for (int i = 0; i < ngonPrims.Length; i++)
            {
                int vertCount = sourceDetail.GetPrimitiveVertexCount(ngonPrims[i]);
                totalOutputPrims += vertCount - 2;
            }

            var sourcePrimForOutput = new NativeList<int>(totalOutputPrims, Allocator.TempJob);

            // ── 5. Pass-through triangles ──
            var triVerts = new NativeArray<int>(3, Allocator.TempJob);
            for (int i = 0; i < trianglePrims.Length; i++)
            {
                NativeSlice<int> srcVerts = sourceDetail.GetPrimitiveVertices(trianglePrims[i]);
                for (int v = 0; v < 3; v++)
                    triVerts[v] = sourceToOutputVertex[srcVerts[v]];
                outputDetail.AddPrimitive(triVerts);
                sourcePrimForOutput.Add(trianglePrims[i]);
            }

            // ── 6. Triangulate N-gons ──
            CoreResult result = CoreResult.Success;

            var primVertices = new NativeList<int>(16, Allocator.TempJob);
            var positions3D = new NativeList<float3>(16, Allocator.TempJob);
            var projected2D = new NativeList<float2>(16, Allocator.TempJob);
            var polygonOrder = new NativeList<int>(16, Allocator.TempJob);

            for (int i = 0; i < ngonPrims.Length; i++)
            {
                int primIdx = ngonPrims[i];
                NativeSlice<int> srcVerts = sourceDetail.GetPrimitiveVertices(primIdx);
                int vertCount = srcVerts.Length;

                primVertices.Clear();
                for (int v = 0; v < vertCount; v++)
                    primVertices.Add(sourceToOutputVertex[srcVerts[v]]);

                int triCount = vertCount - 2;
                var triangleIndices = new NativeArray<int>(triCount * 3, Allocator.TempJob);
                bool success;

                if (mode == TriangulationMode.Fan)
                {
                    SangriaMeshGeometry.WriteFanTriangulation(primVertices, triangleIndices, 0);
                    success = true;
                }
                else
                {
                    positions3D.Clear();
                    for (int v = 0; v < vertCount; v++)
                    {
                        int srcPointIdx = sourceDetail.GetVertexPoint(srcVerts[v]);
                        positions3D.Add(sourceDetail.GetPointPosition(srcPointIdx));
                    }

                    success = SangriaMeshGeometry.TryBuildProjectedPolygon(positions3D, projected2D);
                    if (success)
                    {
                        int writeIdx = 0;
                        success = SangriaMeshGeometry.TryTriangulateEarClip(
                            primVertices, projected2D, polygonOrder, triangleIndices, ref writeIdx);
                    }

                    if (!success)
                    {
                        SangriaMeshGeometry.WriteFanTriangulation(primVertices, triangleIndices, 0);
                        success = true;
                    }
                }

                if (success)
                {
                    for (int t = 0; t < triCount; t++)
                    {
                        triVerts[0] = triangleIndices[t * 3 + 0];
                        triVerts[1] = triangleIndices[t * 3 + 1];
                        triVerts[2] = triangleIndices[t * 3 + 2];
                        outputDetail.AddPrimitive(triVerts);
                        sourcePrimForOutput.Add(primIdx);
                    }
                }

                triangleIndices.Dispose();
            }

            // ── 7. Transfer all domain attributes and resources ──
            CoreResult pointResult = AttributeTransferOp.TransferPointAttributes(
                ref sourceDetail, ref outputDetail, in provenance, in policy);
            if (pointResult != CoreResult.Success)
                result = pointResult;

            CoreResult vertexResult = AttributeTransferOp.TransferVertexAttributes(
                ref sourceDetail, ref outputDetail, in sourceVertexForOutput);
            if (vertexResult != CoreResult.Success)
                result = vertexResult;

            var sourcePrimArray = sourcePrimForOutput.AsArray();
            CoreResult primResult = AttributeTransferOp.TransferPrimitiveAttributes(
                ref sourceDetail, ref outputDetail, in sourcePrimArray);
            if (primResult != CoreResult.Success)
                result = primResult;

            outputDetail.CopyResourcesFrom(ref sourceDetail);

            // ── Cleanup ──
            primVertices.Dispose();
            positions3D.Dispose();
            projected2D.Dispose();
            polygonOrder.Dispose();
            triVerts.Dispose();
            sourcePrimForOutput.Dispose();
            sourceVertexForOutput.Dispose();
            sourceToOutputVertex.Dispose();
            provenance.Dispose();
            usedPointsArray.Dispose();
            sourceToOutputPoint.Dispose();
            usedSourcePoints.Dispose();

            return result;
        }

        // ──────────────────────────────────────────────────────────────
        //  Tess path — uses NativeTess sweep-line per N-gon
        //  Preserves corner semantics, deduplicates shared points,
        //  transfers vertex/primitive attributes and resources.
        // ──────────────────────────────────────────────────────────────

        static CoreResult TriangulateTess(
            ref NativeDetail sourceDetail,
            ref NativeDetail outputDetail,
            NativeList<int> trianglePrims,
            NativeList<int> ngonPrims,
            InterpolationPolicy policy,
            TriangulationOptions tessOptions)
        {
            // Build positions array indexed by source point index
            var allPoints = new NativeList<int>(sourceDetail.PointCount, Allocator.TempJob);
            sourceDetail.GetAllValidPoints(allPoints);

            int maxPointIndex = 0;
            for (int i = 0; i < allPoints.Length; i++)
            {
                if (allPoints[i] > maxPointIndex)
                    maxPointIndex = allPoints[i];
            }

            var positions = new NativeArray<float3>(maxPointIndex + 1, Allocator.TempJob);
            for (int i = 0; i < allPoints.Length; i++)
            {
                int ptIdx = allPoints[i];
                positions[ptIdx] = sourceDetail.GetPointPosition(ptIdx);
            }

            // Triangulate each N-gon via NativeTess
            var ngonDetails = new NativeDetail[ngonPrims.Length];
            var ngonProvenances = new ProvenanceMap[ngonPrims.Length];
            CoreResult result = CoreResult.Success;

            for (int i = 0; i < ngonPrims.Length; i++)
            {
                int primIdx = ngonPrims[i];
                NativeSlice<int> vertexIndices = sourceDetail.GetPrimitiveVertices(primIdx);
                int vertCount = vertexIndices.Length;

                var contourOffsets = new NativeArray<int>(2, Allocator.TempJob);
                contourOffsets[0] = 0;
                contourOffsets[1] = vertCount;

                var contourPointIndices = new NativeArray<int>(vertCount, Allocator.TempJob);
                for (int v = 0; v < vertCount; v++)
                {
                    int vertIdx = vertexIndices[v];
                    int pointIdx = sourceDetail.GetVertexPoint(vertIdx);
                    contourPointIndices[v] = pointIdx;
                }

                var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
                var ngonOutput = new NativeDetail(vertCount, Allocator.TempJob);
                CoreResult tessResult = Triangulation.TriangulateContours(
                    in contours, ref ngonOutput, out ProvenanceMap prov, in tessOptions);

                contourOffsets.Dispose();
                contourPointIndices.Dispose();

                if (tessResult != CoreResult.Success)
                {
                    ngonOutput.Dispose();
                    if (prov.IsCreated) prov.Dispose();
                    result = tessResult;
                    for (int j = 0; j < i; j++)
                    {
                        ngonDetails[j].Dispose();
                        if (ngonProvenances[j].IsCreated) ngonProvenances[j].Dispose();
                    }
                    goto Cleanup;
                }

                ngonDetails[i] = ngonOutput;
                ngonProvenances[i] = prov;
            }

            // ── Merge into output ──
            {
                // ── 1. Collect ALL unique source points (triangles + ngons) with dedup ──
                var sourceToOutputPoint = new NativeHashMap<int, int>(
                    math.max(1, sourceDetail.PointCount), Allocator.TempJob);

                for (int i = 0; i < trianglePrims.Length; i++)
                {
                    NativeSlice<int> verts = sourceDetail.GetPrimitiveVertices(trianglePrims[i]);
                    for (int v = 0; v < verts.Length; v++)
                    {
                        int srcPt = sourceDetail.GetVertexPoint(verts[v]);
                        if (!sourceToOutputPoint.ContainsKey(srcPt))
                        {
                            float3 pos = sourceDetail.GetPointPosition(srcPt);
                            int outPt = outputDetail.AddPoint(pos);
                            sourceToOutputPoint[srcPt] = outPt;
                        }
                    }
                }

                for (int i = 0; i < ngonPrims.Length; i++)
                {
                    NativeSlice<int> verts = sourceDetail.GetPrimitiveVertices(ngonPrims[i]);
                    for (int v = 0; v < verts.Length; v++)
                    {
                        int srcPt = sourceDetail.GetVertexPoint(verts[v]);
                        if (!sourceToOutputPoint.ContainsKey(srcPt))
                        {
                            float3 pos = sourceDetail.GetPointPosition(srcPt);
                            int outPt = outputDetail.AddPoint(pos);
                            sourceToOutputPoint[srcPt] = outPt;
                        }
                    }
                }

                // Add Tess-generated new points (interior Steiner points)
                // Map ngon local point → global output point, deduplicating shared boundary points
                var ngonLocalToGlobal = new NativeArray<int>[ngonPrims.Length];

                for (int i = 0; i < ngonPrims.Length; i++)
                {
                    var ngonOut = ngonDetails[i];
                    var prov = ngonProvenances[i];
                    var ngonAllPts = new NativeList<int>(ngonOut.PointCount, Allocator.TempJob);
                    ngonOut.GetAllValidPoints(ngonAllPts);

                    var localToGlobal = new NativeArray<int>(ngonOut.PointCount > 0 ? ngonAllPts[ngonAllPts.Length - 1] + 1 : 0, Allocator.TempJob);

                    for (int p = 0; p < ngonAllPts.Length; p++)
                    {
                        int localPtIdx = ngonAllPts[p];
                        var record = prov.Records[localPtIdx];

                        if (record.Kind == ProvenanceKind.Identity && record.Count == 1)
                        {
                            int srcPtIdx = record.Src0;
                            if (sourceToOutputPoint.TryGetValue(srcPtIdx, out int existingOutPt))
                            {
                                localToGlobal[localPtIdx] = existingOutPt;
                                continue;
                            }
                        }

                        float3 pos = ngonOut.GetPointPosition(localPtIdx);
                        int outPt = outputDetail.AddPoint(pos);
                        localToGlobal[localPtIdx] = outPt;
                    }

                    ngonLocalToGlobal[i] = localToGlobal;
                    ngonAllPts.Dispose();
                }

                int totalOutputPoints = outputDetail.PointCount;

                // ── 2. Build global provenance ──
                var globalProvenance = new ProvenanceMap(totalOutputPoints, maxPointIndex + 1, Allocator.TempJob);

                // Identity records for source points
                {
                    var kvs = sourceToOutputPoint.GetKeyValueArrays(Allocator.TempJob);
                    for (int i = 0; i < kvs.Keys.Length; i++)
                        globalProvenance.Records[kvs.Values[i]] = ProvenanceRecord.Identity(kvs.Keys[i]);
                    kvs.Dispose();
                }

                // Ngon provenance for new interior points
                for (int i = 0; i < ngonPrims.Length; i++)
                {
                    var prov = ngonProvenances[i];
                    var localToGlobal = ngonLocalToGlobal[i];
                    for (int p = 0; p < prov.OutputPointCount; p++)
                    {
                        if (p < localToGlobal.Length)
                        {
                            int globalIdx = localToGlobal[p];
                            var record = prov.Records[p];
                            if (record.Kind != ProvenanceKind.Identity || record.Count != 1)
                                globalProvenance.Records[globalIdx] = record;
                        }
                    }
                }

                // ── 3. Build vertex mapping with corner semantics ──
                var sourceToOutputVertex = new NativeHashMap<int, int>(
                    math.max(1, sourceDetail.VertexCount), Allocator.TempJob);

                int totalSourceVerticesUsed = 0;

                for (int i = 0; i < trianglePrims.Length; i++)
                {
                    NativeSlice<int> srcVerts = sourceDetail.GetPrimitiveVertices(trianglePrims[i]);
                    for (int v = 0; v < srcVerts.Length; v++)
                    {
                        int srcVert = srcVerts[v];
                        if (!sourceToOutputVertex.ContainsKey(srcVert))
                        {
                            int srcPoint = sourceDetail.GetVertexPoint(srcVert);
                            int outPoint = sourceToOutputPoint[srcPoint];
                            int outVert = outputDetail.AddVertex(outPoint);
                            sourceToOutputVertex[srcVert] = outVert;
                            totalSourceVerticesUsed++;
                        }
                    }
                }

                // For ngon vertices that map to source vertices, preserve them
                for (int i = 0; i < ngonPrims.Length; i++)
                {
                    NativeSlice<int> srcVerts = sourceDetail.GetPrimitiveVertices(ngonPrims[i]);
                    for (int v = 0; v < srcVerts.Length; v++)
                    {
                        int srcVert = srcVerts[v];
                        if (!sourceToOutputVertex.ContainsKey(srcVert))
                        {
                            int srcPoint = sourceDetail.GetVertexPoint(srcVert);
                            int outPoint = sourceToOutputPoint[srcPoint];
                            int outVert = outputDetail.AddVertex(outPoint);
                            sourceToOutputVertex[srcVert] = outVert;
                            totalSourceVerticesUsed++;
                        }
                    }
                }

                // For Tess-generated interior vertices, create new output vertices
                // (Tess creates 1:1 vertex-to-point for new interior points)
                var ngonVertexMaps = new NativeHashMap<int, int>[ngonPrims.Length];
                int totalNewVertices = 0;
                for (int i = 0; i < ngonPrims.Length; i++)
                {
                    var ngonOut = ngonDetails[i];
                    var localToGlobal = ngonLocalToGlobal[i];

                    var ngonAllVerts = new NativeList<int>(ngonOut.VertexCount, Allocator.TempJob);
                    ngonOut.GetAllValidVertices(ngonAllVerts);

                    var vertMap = new NativeHashMap<int, int>(math.max(1, ngonAllVerts.Length), Allocator.TempJob);

                    // Map original ngon source vertices
                    NativeSlice<int> srcVerts = sourceDetail.GetPrimitiveVertices(ngonPrims[i]);
                    for (int v = 0; v < srcVerts.Length; v++)
                    {
                        int srcVert = srcVerts[v];
                        int srcPoint = sourceDetail.GetVertexPoint(srcVert);

                        // Find matching ngon local vertex for this source point
                        for (int nv = 0; nv < ngonAllVerts.Length; nv++)
                        {
                            int ngonVertIdx = ngonAllVerts[nv];
                            int ngonPointIdx = ngonOut.GetVertexPoint(ngonVertIdx);
                            if (ngonPointIdx < localToGlobal.Length)
                            {
                                int globalPt = localToGlobal[ngonPointIdx];
                                if (globalPt == sourceToOutputPoint[srcPoint])
                                {
                                    if (!vertMap.ContainsKey(ngonVertIdx))
                                    {
                                        vertMap[ngonVertIdx] = sourceToOutputVertex[srcVert];
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Remaining ngon vertices are interior — create new output vertices
                    for (int nv = 0; nv < ngonAllVerts.Length; nv++)
                    {
                        int ngonVertIdx = ngonAllVerts[nv];
                        if (!vertMap.ContainsKey(ngonVertIdx))
                        {
                            int ngonPointIdx = ngonOut.GetVertexPoint(ngonVertIdx);
                            int globalPt = localToGlobal[ngonPointIdx];
                            int outVert = outputDetail.AddVertex(globalPt);
                            vertMap[ngonVertIdx] = outVert;
                            totalNewVertices++;
                        }
                    }

                    ngonVertexMaps[i] = vertMap;
                    ngonAllVerts.Dispose();
                }

                // Build vertex source mapping array for attribute transfer
                var sourceVertexForOutput = new NativeArray<int>(
                    totalSourceVerticesUsed + totalNewVertices, Allocator.TempJob);
                // Initialize to -1 (no source) for new interior vertices
                for (int i = 0; i < sourceVertexForOutput.Length; i++)
                    sourceVertexForOutput[i] = -1;
                {
                    var kvs = sourceToOutputVertex.GetKeyValueArrays(Allocator.TempJob);
                    for (int i = 0; i < kvs.Keys.Length; i++)
                        sourceVertexForOutput[kvs.Values[i]] = kvs.Keys[i];
                    kvs.Dispose();
                }

                // ── 4. Build primitive source mapping ──
                var sourcePrimForOutput = new NativeList<int>(
                    trianglePrims.Length + ngonPrims.Length * 2, Allocator.TempJob);

                // ── 5. Emit pass-through triangles ──
                var triVerts = new NativeArray<int>(3, Allocator.TempJob);
                for (int i = 0; i < trianglePrims.Length; i++)
                {
                    NativeSlice<int> srcVerts = sourceDetail.GetPrimitiveVertices(trianglePrims[i]);
                    for (int v = 0; v < 3; v++)
                        triVerts[v] = sourceToOutputVertex[srcVerts[v]];
                    outputDetail.AddPrimitive(triVerts);
                    sourcePrimForOutput.Add(trianglePrims[i]);
                }

                // ── 6. Emit N-gon triangulated primitives ──
                for (int i = 0; i < ngonPrims.Length; i++)
                {
                    var ngonOut = ngonDetails[i];
                    var vertMap = ngonVertexMaps[i];
                    var ngonAllPrims = new NativeList<int>(ngonOut.PrimitiveCount, Allocator.TempJob);
                    ngonOut.GetAllValidPrimitives(ngonAllPrims);

                    for (int p = 0; p < ngonAllPrims.Length; p++)
                    {
                        NativeSlice<int> ngonPrimVerts = ngonOut.GetPrimitiveVertices(ngonAllPrims[p]);
                        for (int v = 0; v < math.min(ngonPrimVerts.Length, 3); v++)
                            triVerts[v] = vertMap[ngonPrimVerts[v]];
                        outputDetail.AddPrimitive(triVerts);
                        sourcePrimForOutput.Add(ngonPrims[i]);
                    }
                    ngonAllPrims.Dispose();
                }

                // ── 7. Transfer all domain attributes and resources ──
                CoreResult pointResult = AttributeTransferOp.TransferPointAttributes(
                    ref sourceDetail, ref outputDetail, in globalProvenance, in policy);
                if (pointResult != CoreResult.Success)
                    result = pointResult;

                CoreResult vertexResult = AttributeTransferOp.TransferVertexAttributes(
                    ref sourceDetail, ref outputDetail, in sourceVertexForOutput);
                if (vertexResult != CoreResult.Success)
                    result = vertexResult;

                var sourcePrimArray = sourcePrimForOutput.AsArray();
                CoreResult primResult = AttributeTransferOp.TransferPrimitiveAttributes(
                    ref sourceDetail, ref outputDetail, in sourcePrimArray);
                if (primResult != CoreResult.Success)
                    result = primResult;

                outputDetail.CopyResourcesFrom(ref sourceDetail);

                // ── Cleanup merge phase ──
                triVerts.Dispose();
                sourceVertexForOutput.Dispose();
                sourcePrimForOutput.Dispose();
                for (int i = 0; i < ngonPrims.Length; i++)
                {
                    ngonVertexMaps[i].Dispose();
                    ngonLocalToGlobal[i].Dispose();
                }
                sourceToOutputVertex.Dispose();
                sourceToOutputPoint.Dispose();
                globalProvenance.Dispose();
            }

            // Dispose per-ngon intermediates
            for (int i = 0; i < ngonPrims.Length; i++)
            {
                ngonDetails[i].Dispose();
                if (ngonProvenances[i].IsCreated) ngonProvenances[i].Dispose();
            }

            Cleanup:
            positions.Dispose();
            allPoints.Dispose();

            return result;
        }
    }
}
