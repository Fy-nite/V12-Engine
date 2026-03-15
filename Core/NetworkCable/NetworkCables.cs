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
        /// Create a Uri from a user-supplied sender string. If the string already contains a scheme
        /// (contains "://") it will be used as-is when valid. If no scheme is present, defaults to http://.
        /// If the input is null or invalid, falls back to networkcables://localhost.
        /// </summary>
        public static Uri CreateUriFromString(string? sender)
        {
            if (string.IsNullOrWhiteSpace(sender)) return new Uri("networkcables://localhost");

            // If the caller provided a scheme already, try to use it
            if (sender.Contains("://"))
            {
                try { return new Uri(sender); }
                catch { /* fall through to try http */ }
            }

            // No scheme provided; default to http to support http:// style names
            try { return new Uri("http://" + sender); }
            catch { return new Uri("networkcables://localhost"); }
        }

        /// <summary>
        /// Convenience: send raw bytes with a string sender (parsed to Uri).
        /// </summary>
        public static void Send(string sender, byte[] message, MessageType messageType = MessageType.Event)
        {
            var uri = CreateUriFromString(sender);
            var dto = new MessageDTO(uri, message) { MessageType = messageType };
            Default?.SendData(dto);
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
                Console.WriteLine($"Processing outgoing message from {message.Sender?.AbsoluteUri} of type {message.MessageType}");
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
                Console.WriteLine($"Processing incoming message from {message.Sender?.AbsoluteUri} of type {message.MessageType}");
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
