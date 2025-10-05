using UTools.SourceGeneratorAttributes;

namespace ExampleNamespace.NameSpace
{
    public class MyTestAttr : System.Attribute { }

    [MyTestAttr]
    public partial class CombinedSubscriptionsExample
    {

#if !TEST
        [PropertySubscription] private bool m_TwoSubscriptionsField;
        void Test0()
        {
        }
#else
        [PropertySubscription] private bool m_TwoSubscriptionsField2;

        void Test1()
        {
            
        }
        
        void Test2()
        {
            
        }
#endif
    }
}