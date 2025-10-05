using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;

namespace PropellerheadMesh
{
    public class SphereGeneratorTest : MonoBehaviour
    {
        [SerializeField] private float m_Radius = 1f;
        [SerializeField] private int m_Subdives = 4;

        [SerializeField] private float m_NoiseScale = 1f;
        [SerializeField] private float m_WiggleAmplitude = 0.3f;
        [SerializeField] private float3 m_TimeMultiplier = new(0.8f, 0.6f, 0.4f);
        [SerializeField] private float3 m_NoiseOffset = new(0.3f, 0.9f, 0.7f);

        [Header("Normals Settings")] [SerializeField]
        private bool m_RecalculateNormals = true;

        [SerializeField] private float m_SmoothAngle = 60f;

        private NativeDetail m_NativeDetail;
        private NativePositionWiggleOperator m_WiggleOperator;

        private void Start()
        {
            CreateSphere();
        }

        private void CreateSphere()
        {
            var capacity = 64;

            m_NativeDetail = new NativeDetail(capacity, Allocator.Persistent);
            NativeSphereGenerator.GenerateSphere(ref m_NativeDetail, m_Radius, m_Subdives);
            // NativeCubeGenerator.GenerateCube(ref m_NativeDetail, new float3(m_Radius, m_Radius, m_Radius), true);
            m_NativeDetail.AddVertexAttribute<float3>(AttributeID.Normal);
            m_NativeDetail.AddPrimitiveAttribute<float3>(AttributeID.Normal);

            m_WiggleOperator = new NativePositionWiggleOperator();
            m_WiggleOperator.Initialize(ref m_NativeDetail);
        }

        private void Update()
        {
            m_WiggleOperator.NoiseScale = m_NoiseScale;
            m_WiggleOperator.WiggleAmplitude = m_WiggleAmplitude;
            m_WiggleOperator.TimeMultiplier = m_TimeMultiplier;
            m_WiggleOperator.NoiseOffset = m_NoiseOffset;

            var wiggleHandle = m_WiggleOperator.Apply(ref m_NativeDetail, Time.time);

            Unity.Jobs.JobHandle normalsHandle = wiggleHandle;
            if (m_RecalculateNormals)
            {
                var smoothAngleRad = NativeNormalsOperators.DegreesToRadians(m_SmoothAngle);
                normalsHandle =
                    NativeNormalsOperators.CalculateNormals(ref m_NativeDetail, smoothAngleRad, wiggleHandle);
            }

            normalsHandle.Complete();
            var mesh = GetComponent<MeshFilter>().mesh;
            if (mesh == null)
            {
                mesh = new Mesh();
                GetComponent<MeshFilter>().mesh = mesh;
            }

            m_NativeDetail.FillUnityMesh(mesh);
        }

        private void OnDrawGizmos()
        {
            m_NativeDetail.DrawPointGizmos(0.05f, Color.aliceBlue);
            m_NativeDetail.DrawVertexNormalsGizmos(0.3f, Color.cyan);
            m_NativeDetail.DrawPointNumbers(Color.black, 0.0333f);
            m_NativeDetail.DrawPrimitiveLines(Color.darkOrange);
            
        }

        private void OnDestroy()
        {
            m_NativeDetail.Dispose();
            m_WiggleOperator?.Dispose();
        }
    }
}