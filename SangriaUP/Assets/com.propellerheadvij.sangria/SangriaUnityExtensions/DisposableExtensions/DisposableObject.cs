using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sangria
{
    public class DisposableObject : IDisposableObject
    {
        public bool IsDisposed { get; private set; }

        public event Action Disposed;

        public void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
                OnDispose();
                Disposed?.Invoke();
                Disposed = null;
            }
        }

        protected virtual void OnDispose()
        {
        }
    }
}