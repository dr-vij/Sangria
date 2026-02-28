using System;

namespace Sangria
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
