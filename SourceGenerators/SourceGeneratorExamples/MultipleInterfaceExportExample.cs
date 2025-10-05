using UTools.SourceGeneratorAttributes;

namespace ExampleNamespace.NameSpace
{
    public partial interface ITestInterface { }

    public partial interface ITestInterface2 { }

    public partial interface ITestInterface3 { }

    public partial class MultipleInterfaceExportExample
    {
        [PropertySubscription(GetterVisibility = Visibility.Public, SetterVisibility = Visibility.Private)]
        private bool m_TestVisibility;
        
        [PropertySubscription(Visibility.Private, Visibility.Private)]
        private bool m_TestVisibility1;
        
        [PropertySubscription(Visibility.Public, Visibility.Internal, typeof(ITestInterface))]
        private bool m_TestVisibility2;

        [PropertySubscription(OutputInterfaces = new[] { typeof(ITestInterface) })]
        private bool m_TestFieldDisposable;
        
        [PropertySubscription(typeof(ITestInterface), typeof(ITestInterface2))]
        private bool m_TestField2;
        
        [PropertySubscription(typeof(ITestInterface3))]
        private bool m_TestField3;
    }
}