﻿
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

namespace OpenP2P
{
    public class NetworkMessagePool
    {
        NetworkChannel channel = null;
        List<Queue<NetworkMessage>> available = new List<Queue<NetworkMessage>>();
        //Queue<NetworkMessage> available = new Queue<NetworkMessage>();
        //ConcurrentBag<NetworkPacket> available = new ConcurrentBag<NetworkPacket>();
        int initialPoolCount = 0;
        public int messageCount = 0;

        public NetworkMessagePool(NetworkChannel _channel, int initPoolCount)
        {
            channel = _channel;
            initialPoolCount = initPoolCount;
            Queue<NetworkMessage> queue = null;

            for(int i=0; i<(int)ChannelType.LAST; i++)
            {
                queue = new Queue<NetworkMessage>(initPoolCount);
                available.Add(queue);

                for (int j = 0; j < initPoolCount; j++)
                {
                    New((ChannelType)i, queue);
                }
            }
        }

        /**
         * Add another NetworkBuffer to the Pool
         */
        public void New(ChannelType channelType, Queue<NetworkMessage> queue)
        {
            messageCount++;
            NetworkMessage message = channel.InstantiateMessage(channelType);
            queue.Enqueue(message);
        }

        public T Reserve<T>() where T : INetworkMessage
        {
            T msg = (T)Reserve(channel.messageToChannelType[typeof(T)]);
            return msg;
        }

        /**
         * Reserve a NetworkBuffer from this pool.
         */
        public INetworkMessage Reserve(ChannelType channelType)
        {
            NetworkMessage message = null;
            Queue<NetworkMessage> queue = available[(int)channelType];
            
            int availableCount = 0;

            lock(queue)
            {
                availableCount = queue.Count;

                if (availableCount == 0)
                    New(channelType, queue);

                message = queue.Dequeue();
            }

            return message;
        }

        /**
         * Free a reserved NetworkBuffer from this pool by NetworkBuffer object.
         */
        public void Free(NetworkMessage message)
        {
            Queue<NetworkMessage> queue = available[(int)message.header.channelType];
            lock (queue)
            {
                queue.Enqueue(message);
            }
        }

        public void Dispose()
        {
            NetworkMessage message = null;
            Queue<NetworkMessage> queue;

            for(int i=0; i<(int)ChannelType.LAST; i++)
            {
                queue = available[i];
                while (queue.Count > 0)
                {
                    message = queue.Dequeue();
                }
            }
        }
    }
}


