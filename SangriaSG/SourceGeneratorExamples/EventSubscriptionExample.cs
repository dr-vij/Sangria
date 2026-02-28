using Sangria.SourceGeneratorAttributes;

namespace ExampleNamespace.NameSpace
{
    public partial interface IEventInterface
    {
    }

    public partial class EventSubscriptionExample
    {
        [PropertySubscription(typeof(IEventInterface))]
        private bool m_TestFieldEvent;
    }
}