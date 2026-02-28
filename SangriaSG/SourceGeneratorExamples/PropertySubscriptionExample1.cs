using System;
using Sangria.SourceGeneratorAttributes;

namespace SourceGeneratorExamples
{
    /// <summary>
    /// All we make here is just private field
    /// all subscriptions/properties are generated.
    /// </summary>
    public partial class PropertySubscriptionExample1
    {
        [PropertySubscription] private bool m_TestField;
    }
}

namespace SourceGeneratorExamples
{
    public class ExampleClass
    {
        private IDisposable m_MySubscription;
        private PropertySubscriptionExample1 m_Test;

        public void SubscribeExample()
        {
            m_Test = new PropertySubscriptionExample1();
            m_MySubscription?.Dispose();
            // We get IDisposable after subscription. It removes handler when disposed
            m_MySubscription = m_Test.SubscribeToTestField(OnTestFieldChanged);
        }

        /// <summary>
        /// We did not create TestField property, it is created with Source Generator
        /// </summary>
        public void ChangeExample()
        {
            m_Test.TestField = true;
        }

        private void OnTestFieldChanged(object sender, bool newValue)
        {
            // Each time we change TestField we get this callback.
        }
    }
}