using System;


namespace Sangria
{
    public interface IDisposedNotifier
    {
        public event Action Disposed;
    }
}