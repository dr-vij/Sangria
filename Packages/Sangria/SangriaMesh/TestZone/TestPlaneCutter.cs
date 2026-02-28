using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using static UnityEngine.Mesh;
using UnityEngine.UI;
using TMPro;

namespace ViJMeshTools
{
    public class TestPlaneCutter : MonoBehaviour
    {
        //Initial Data
        [SerializeField] private MeshFilter mInitialMeshFilter = default;
        [SerializeField] private Transform mPlaneTransform = default;
        [SerializeField] private Transform mPlaneRoot = default;

        private Mesh mInitialMesh;
        private MeshDataArray mInitialMeshDataArray;

        //Returned Data
        [SerializeField] private MeshFilter mPositiveMeshFilter = default;
        [SerializeField] private MeshFilter mNegativeMeshFilter = default;

        private Mesh mPositiveMesh;
        private Mesh mNegativeMesh;

        [SerializeField] private Button mButton = default;
        [SerializeField] private TextMeshPro mTmp = default;

        private void OnDestroy()
        {
            Destroy(mInitialMesh);
            Destroy(mPositiveMesh);
            Destroy(mNegativeMesh);

            mInitialMeshDataArray.Dispose();
        }

        private void Start()
        {
            mPositiveMeshFilter.mesh = mPositiveMesh = new Mesh();
            mNegativeMeshFilter.mesh = mNegativeMesh = new Mesh();

            mInitialMesh = mInitialMeshFilter.mesh;
            MeshAnalizers.IsMeshSupportedWithCutter(mInitialMesh);

            mInitialMeshDataArray = AcquireReadOnlyMeshData(mInitialMesh);
            mButton.onClick.AddListener(DoCut);
        }

        private void DoCut()
        {
            MeshCutter.CutMeshWithTesselation(mInitialMeshDataArray[0], mPlaneTransform, mInitialMeshFilter.transform, mPositiveMesh, mNegativeMesh);

            MeshAnalizers.MeshVolumeJob volumeCalc = new MeshAnalizers.MeshVolumeJob();
            volumeCalc.InitializeJob(mInitialMeshDataArray[0]);
            volumeCalc.Execute();
            Debug.Log(volumeCalc.Result);
            volumeCalc.Dispose();
        }
    }
}