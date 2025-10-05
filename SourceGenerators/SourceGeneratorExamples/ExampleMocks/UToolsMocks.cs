using System;

namespace UTools
{
    public class DisposeAction : IDisposable
    {
        public DisposeAction(System.Action action)
        {
        }

        public void Dispose()
        {
        }
    }
}