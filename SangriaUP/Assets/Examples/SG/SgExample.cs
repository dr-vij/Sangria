using Sangria.SourceGeneratorAttributes;
using UnityEngine;

public partial class SgExample : MonoBehaviour
{
    [PropertySubscription] private bool m_IsTrue;

    public void Update()
    {
        IsTrue = true;
    }
}
