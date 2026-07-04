using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using V12.Core.Networking;

namespace V12.Core.NetworkCable
{
    public class NetworkCables
    {
        /// <summary>
        /// Global default instance used by extension helpers. Can be replaced if a different instance is desired.
        /// </summary>
        public static NetworkCables Default { get; set; } = new NetworkCables();

        /// <summary>
        /// Convenience static sender that uses the Default instance.
        /// </summary>
        /// <param name="message"></param>
        public static void Send(MessageDTO message) => Default?.SendData(message);

        /// <summary>
        /// Thread-safe outgoing message queue.
        /// </summary>
        public ConcurrentQueue<MessageDTO> SendQueue { get; } = new ConcurrentQueue<MessageDTO>();

        /// <summary>
        /// Thread-safe incoming message queue.
        /// </summary>
        public ConcurrentQueue<MessageDTO> ReceiveQueue { get; } = new ConcurrentQueue<MessageDTO>();

        /// <summary>
        /// Raised when a message is received and processed.
        /// </summary>
        public event Action<MessageDTO>? OnMessageReceived;

        /// <summary>
        /// Raised when a message is about to be sent. Allows transport layer to intercept.
        /// </summary>
        public event Action<MessageDTO>? OnMessageSending;

        public NetworkCables()
        {
        }

        /// <summary>
        /// Enqueue a message to be sent.
        /// </summary>
        public void SendData(MessageDTO message)
        {
            if (message == null) return;
            SendQueue.Enqueue(message);
        }

        /// <summary>
        /// Enqueue a received message for processing.
        /// </summary>
        public void EnqueueIncoming(MessageDTO message)
        {
            if (message == null) return;
            ReceiveQueue.Enqueue(message);
        }

        /// <summary>
        /// Try to dequeue an outgoing message. Used by transport layer.
        /// </summary>
        public bool TryDequeueOutgoing(out MessageDTO message)
        {
            return SendQueue.TryDequeue(out message);
        }

        /// <summary>
        /// Process all pending outgoing messages by raising OnMessageSending event.
        /// </summary>
        public void ProcessOutgoing()
        {
            while (SendQueue.TryDequeue(out var message))
            {
                
                // Console.WriteLine($"Processing outgoing message from {message.Sender?.AbsoluteUri} of type {message.MessageType}");
                OnMessageSending?.Invoke(message);
            }
        }

        /// <summary>
        /// Process all pending incoming messages by raising OnMessageReceived event.
        /// </summary>
        public void ProcessIncoming()
        {
            while (ReceiveQueue.TryDequeue(out var message))
            {
                // Console.WriteLine($"Processing incoming message from {message.Sender?.AbsoluteUri} of type {message.MessageType}");
                OnMessageReceived?.Invoke(message);
            }
        }

        /// <summary>
        /// Process both incoming and outgoing queues.
        /// </summary>
        public void Update()
        {
            ProcessIncoming();
            ProcessOutgoing();
        }
    }
}
