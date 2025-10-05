using UTools.SourceGeneratorAttributes;

namespace SourceGeneratorExamples
{
    public partial class PropertySubscriptionExample2
    {
        [PropertySubscription(Visibility.Public, Visibility.Private)] protected bool m_TestVariable;
    }
}