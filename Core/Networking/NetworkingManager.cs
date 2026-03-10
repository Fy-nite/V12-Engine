using System;
using System.Collections.Generic;
using System.Text;
using V12.Interfaces;

namespace V12.Core.Networking
{
    /// <summary>
    /// the networking manager or something.
    /// i am gonna hate this one really much - charlie-san
    /// </summary>
    public class NetworkingManager : INetworkManager, IDisposable
    {
        private bool disposedValue;

        public MessageDTO ReceiveMessage()
        {
            throw new NotImplementedException();
        }

        public void SendMessage(MessageDTO message)
        {
            throw new NotImplementedException();
        }

        void INetworkManager.Register(INetworkManager manager)
        {
            throw new NotImplementedException();
        }

        void INetworkManager.Unregister(INetworkManager manager)
        {
            throw new NotImplementedException();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~NetworkingManager()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
