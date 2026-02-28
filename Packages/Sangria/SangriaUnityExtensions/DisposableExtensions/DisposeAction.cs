using System;
using System.Threading;

namespace Sangria
{
    public class DisposeAction : DisposableObject
    {
        private Action m_ActionOnDispose;

        public DisposeAction(Action actionOnDispose) => m_ActionOnDispose = actionOnDispose;

        protected override void OnDispose() => Interlocked.Exchange(ref m_ActionOnDispose, null)?.Invoke();
    }
}