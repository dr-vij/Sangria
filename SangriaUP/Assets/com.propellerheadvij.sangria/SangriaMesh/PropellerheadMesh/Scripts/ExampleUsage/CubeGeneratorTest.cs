using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;

namespace PropellerheadMesh
{
    public class CubeGeneratorTest : MonoBehaviour
    {
        [SerializeField] private float3 m_Size = 1f;
        [SerializeField] private float m_NoiseScale = 1f;
        [SerializeField] private float m_WiggleAmplitude = 0.3f;
        [SerializeField] private float3 m_TimeMultiplier = new (0.8f, 0.6f, 0.4f);
        [SerializeField] private float3 m_NoiseOffset = new (0.3f, 0.9f, 0.7f);

        private EntityManager m_Manager;
        private NativeDetail m_NativeDetail;
        private NativePositionWiggleOperator m_WiggleOperator;

        private void Start()
        {
            m_Manager = World.DefaultGameObjectInjectionWorld.EntityManager;
            CreateCube();
        }

        private void CreateCube()
        {
            var capacity = 64;

            m_NativeDetail = new NativeDetail(capacity, Allocator.Persistent);
            NativeCubeGenerator.GenerateCube(ref m_NativeDetail, m_Size, true);
            m_WiggleOperator = new NativePositionWiggleOperator();
            m_WiggleOperator.Initialize(ref m_NativeDetail);
        }

        private void Update()
        {
            m_WiggleOperator.NoiseScale = m_NoiseScale;
            m_WiggleOperator.WiggleAmplitude = m_WiggleAmplitude;
            m_WiggleOperator.TimeMultiplier = m_TimeMultiplier;
            m_WiggleOperator.NoiseOffset = m_NoiseOffset;
            
            var handle = m_WiggleOperator.Apply(ref m_NativeDetail, Time.time);
            handle.Complete();

            var mesh = GetComponent<MeshFilter>().mesh;
            if (mesh == null)
            {
                mesh = new Mesh();
                GetComponent<MeshFilter>().mesh = mesh;
            }

            m_NativeDetail.FillUnityMesh(mesh);
        }

        private void OnDestroy()
        {
            m_NativeDetail.Dispose();
            m_WiggleOperator?.Dispose();
        }
    }
}