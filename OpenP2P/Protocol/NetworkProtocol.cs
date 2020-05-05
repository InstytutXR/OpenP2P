﻿
using System;
using System.Net;
using System.Collections.Generic;

namespace OpenP2P
{
    /// <summary>
    /// Network Protocol for header defining the type of message
    /// 
    /// Single Byte Format:
    ///     0 0 0 0 0000
    ///     Bit 8    => ProtocolType Flag
    ///     Bit 7    => Big Endian Flag
    ///     Bit 6    => Reliable Flag
    ///     Bit 5    => SendType Flag
    ///     Bits 4-1 => Channel Type
    /// </summary>
    public class NetworkProtocol : NetworkProtocolBase
    {
        //const uint S
        const uint ProtocolTypeFlag = (1 << 7); //bit 8
        const uint StreamFlag = (1 << 6); //bit 7
        const uint ReliableFlag = (1 << 5);  //bit 6
        const uint SendTypeFlag = (1 << 4); //bit 5
        
        public event EventHandler<NetworkMessage> OnWriteHeader = null;
        public event EventHandler<NetworkMessage> OnReadHeader = null;
        public event EventHandler<NetworkPacket> OnErrorConnectToServer;
        public event EventHandler<NetworkPacket> OnErrorReliableFailed;
        public event EventHandler<NetworkPacket> OnErrorSTUNFailed;
        Random random = new Random();

        public bool isClient = false;
        public bool isServer = false;


        public NetworkProtocol(bool _isServer)
        {
            Setup(0, _isServer);
        }

        public NetworkProtocol(int localPort, bool _isServer)
        {
            Setup(localPort, _isServer);
        }


        public void Setup(int localPort, bool _isServer)
        {
            string localIP = "127.0.0.1";
           
            channel = new NetworkChannel();
            socket = new NetworkSocket(localIP, localPort);
            socket.SetChannel(channel);

            Console.WriteLine("Binding Socket to: " + localIP + ":" + localPort);
            Console.WriteLine("Binded to: " + socket.socket4.LocalEndPoint.ToString());
            
            isClient = !isServer;
            isServer = _isServer;

            AttachSocketListener(socket);
            AttachNetworkIdentity();
        }
        

        public void AttachNetworkIdentity()
        {
            AttachNetworkIdentity(new NetworkIdentity());
        }

        public void AttachNetworkIdentity(NetworkIdentity ni)
        {
            ident = ni;
            ident.AttachToProtocol(this);

            if (isServer)
            {
                ident.RegisterServer(socket.sendSocket.LocalEndPoint);
            }
        }


        public virtual MessageServer ConnectToServer(string userName)
        {
            return (MessageServer)ident.ConnectToServer(userName);
        }


        public NetworkPacket SendReliableMessage(EndPoint ep, NetworkMessage message)
        {
            IPEndPoint ip = GetIPv6(ep);
            NetworkPacket packet = socket.Prepare(ep);

            message.header.destination = ep;
            message.header.channelType = channel.GetChannelType(message);
            message.header.isReliable = true;
            message.header.sendType = SendType.Message;
            message.header.id = ident.local.id;

            if (message.header.retryCount == 0)
                message.header.sequence = ident.local.NextSequence(message);
            
            Send(packet, message);

            return packet;
        }

        public List<NetworkPacket> SendStream(EndPoint ep, NetworkMessage message)
        {
            IPEndPoint ip = GetIPv6(ep);
            NetworkMessageStream stream = (NetworkMessageStream)message;
            List<NetworkPacket> packets = new List<NetworkPacket>();

            stream.header.channelType = channel.GetChannelType(stream);
            stream.header.isReliable = true;
            stream.header.isStream = true;
            stream.header.sendType = SendType.Message;
            stream.header.sequence = ident.local.NextSequence(stream);
            stream.header.id = ident.local.id;

            while (stream.segmentLen > 0 && stream.startPos < stream.byteData.Length )
            {
                NetworkPacket packet = socket.Prepare(ep);
                packet.messages.Add(stream);
                
                WriteHeader(packet, stream);
                WriteRequest(packet, stream);

                socket.Send(packet);
                Console.WriteLine("Sent " + (stream.segmentLen) + " bytes");
            }
            
            return packets;
        }

        public NetworkPacket SendSTUN(EndPoint ep, NetworkMessage message, long delay)
        {
            IPEndPoint ip = GetIPv6(ep);
            NetworkPacket packet = socket.Prepare(ep);
            packet.retryDelay = delay;

            message.header.channelType = channel.GetChannelType(message);
            message.header.isReliable = true;
            message.header.isSTUN = true;
            message.header.sendType = SendType.Message;
            message.header.id = ident.local.id;

            Send(packet, message);

            return packet;
        }

        public NetworkPacket SendMessage(EndPoint ep, NetworkMessage message)
        {
            IPEndPoint ip = GetIPv6(ep);
            NetworkPacket packet = socket.Prepare(ep);

            message.header.channelType = channel.GetChannelType(message);
            message.header.isReliable = false;
            message.header.sendType = SendType.Message;
            message.header.sequence = ident.local.NextSequence(message);
            message.header.id = ident.local.id;

            Send(packet, message);

            return packet;
        }


        public NetworkPacket SendResponse(NetworkMessage requestMessage, NetworkMessage response)
        {
            NetworkPacket packet = socket.Prepare(requestMessage.header.source);

            if(requestMessage.header.source.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                packet.networkIPType = NetworkSocket.NetworkIPType.IPv4;
            else
                packet.networkIPType = NetworkSocket.NetworkIPType.IPv6;

            response.header.channelType = requestMessage.header.channelType;
            response.header.isReliable = requestMessage.header.isReliable;
            response.header.sendType = SendType.Response;
            response.header.sequence = requestMessage.header.sequence;
            response.header.id = requestMessage.header.id;
            response.header.ackkey = requestMessage.header.ackkey;

            Send(packet, response);

            return packet;
        }
       

        public void Send(NetworkPacket packet, NetworkMessage message)
        {
            packet.messages.Add(message);// = message;
            WriteHeader(packet, message);
            switch(message.header.sendType)
            {
                case SendType.Message: WriteRequest(packet, message); break;
                case SendType.Response: WriteResponse(packet, message); break;
            }
            
            socket.Send(packet);
        }
        

        public override void OnReceive(object sender, NetworkPacket packet)
        {
            NetworkMessage message = ReadHeader(packet);
            
            packet.messages.Add(message);
            message.header.source = packet.remoteEndPoint;
            
            if( message.header.isStream )
            {
                
                HandleReceiveStream(message, packet);
            }
            else
            {
                HandleReceiveMessage(message, packet);
            }


            
        }

        public void HandleReceiveStream(NetworkMessage message, NetworkPacket packet) 
        {
            NetworkMessageStream stream = (NetworkMessageStream)message;
            uint streamID = ((uint)stream.header.id << 8) | (uint)stream.header.sequence;
            
            NetworkMessageStream response = (NetworkMessageStream)channel.CreateMessage(stream.header.channelType);

            if (message.header.sendType == SendType.Response )
            {
                if( message.header.isReliable)
                {
                    lock (socket.thread.ACKNOWLEDGED)
                    {
                        if (!socket.thread.ACKNOWLEDGED.ContainsKey(message.header.ackkey))
                            socket.thread.ACKNOWLEDGED.Add(message.header.ackkey, packet);
                    }
                }

                message.ReadResponse(packet);
                NetworkChannelEvent channelEvent = GetChannelEvent(message.header.channelType);
                channelEvent.InvokeEvent(packet, message);
            }
            else if( message.header.sendType == SendType.Message )
            {
                //send acknowledgement

                NetworkMessageStream first = stream;
                if(cachedStreams.ContainsKey(streamID))
                {
                    first = cachedStreams[streamID];
                }
                else
                {
                    cachedStreams.Add(streamID, first);
                }

                stream.ReadRequest(packet);

                first.SetBuffer(stream.byteData, stream.startPos);

                if(stream.startPos > 0
                    && first.byteData.Length == (stream.startPos + stream.byteData.Length))
                {
                    NetworkChannelEvent channelEvent = GetChannelEvent(first.header.channelType);
                    channelEvent.InvokeEvent(packet, first);

                    channel.FreeMessage(first);
                }

                if (first != stream)
                {
                    channel.FreeMessage(stream);
                }
            }
        }

        public void HandleReceiveMessage(NetworkMessage message, NetworkPacket packet)
        {
            switch (message.header.sendType)
            {
                case SendType.Message: message.ReadRequest(packet); break;
                case SendType.Response: message.ReadResponse(packet); break;
            }

            if ((message.header.sendType == SendType.Response)
                && message.header.isReliable)
            {
                lock (socket.thread.ACKNOWLEDGED)
                {
                    if (!socket.thread.ACKNOWLEDGED.ContainsKey(message.header.ackkey))
                        socket.thread.ACKNOWLEDGED.Add(message.header.ackkey, packet);
                }
            }

            NetworkChannelEvent channelEvent = GetChannelEvent(message.header.channelType);
            channelEvent.InvokeEvent(packet, message);

            channel.FreeMessage(message);
        }
        


        public override void OnSend(object sender, NetworkPacket packet)
        {
        }


        public override void OnError(object sender, NetworkPacket packet)
        {
            NetworkErrorType errorType = (NetworkErrorType)sender;
            switch (errorType)
            {
                case NetworkErrorType.ErrorConnectToServer:
                    if (OnErrorConnectToServer != null)
                        OnErrorConnectToServer.Invoke(this, packet);
                    break;
                case NetworkErrorType.ErrorReliableFailed:
                    if( OnErrorReliableFailed != null )
                        OnErrorReliableFailed.Invoke(this, packet);
                    break;
                case NetworkErrorType.ErrorNoResponseSTUN:
                    if (OnErrorSTUNFailed != null)
                        OnErrorSTUNFailed.Invoke(this, packet);
                    break;
            }
        }


        public override void AttachErrorListener(NetworkErrorType errorType, EventHandler<NetworkPacket> func)
        {
            switch (errorType)
            {
                case NetworkErrorType.ErrorConnectToServer:
                    OnErrorConnectToServer += func;
                    break;
                case NetworkErrorType.ErrorReliableFailed:
                    OnErrorReliableFailed += func;
                    break;
                case NetworkErrorType.ErrorNoResponseSTUN:
                    OnErrorSTUNFailed += func;
                    break;
            }
        }

        // 0000 0000
        // bits 1-4 => Channel Type (up to 16 channels)
        // bits 5 => Send Type
        // bits 6 => Reliable Flag
        // bits 7 => Endian Flag
        // bits 8 => ProtocolType Flag
        public override void WriteHeader(NetworkPacket packet, NetworkMessage message)
        {
            if (message.header.isSTUN)
                return;

            uint msgBits = (uint)message.header.channelType;
            if (msgBits < 0 || msgBits >= (uint)ChannelType.LAST)
                msgBits = 0;

            //add sendType to bit 5 
            if( message.header.sendType == SendType.Response )
                msgBits |= SendTypeFlag;

            //add reliable to bit 6
            if( message.header.isReliable )
                msgBits |= ReliableFlag;
           
            //add little endian to bit 8
            if ( message.header.isStream )
                msgBits |= StreamFlag;

            msgBits |= ProtocolTypeFlag;

            packet.Write((byte)msgBits);
            packet.Write(message.header.sequence);

            OnWriteHeader.Invoke(packet, message);

            if (message.header.isReliable)
            {
                if (message.header.sendType == SendType.Message && message.header.retryCount == 0)
                {
                    message.header.ackkey = GenerateAckKey(packet, message);
                }
            }
        }


        public override NetworkMessage ReadHeader(NetworkPacket packet)
        {
            uint bits = packet.ReadByte();

            bool isSTUN = (bits & ProtocolTypeFlag) == 0;
            if( isSTUN )
            {
                packet.bytePos = 0;
                NetworkMessage msg = (NetworkMessage)channel.CreateMessage(ChannelType.STUN);
                msg.header.isSTUN = true;
                msg.header.isReliable = true;
                msg.header.sendType = SendType.Response;
                return msg;
            }

            bool isStream = (bits & StreamFlag) > 0;
            bool isReliable = (bits & ReliableFlag) > 0;
            SendType sendType = (SendType)((bits & SendTypeFlag) > 0 ? 1 : 0);
           
            //remove flag bits to reveal channel type
            bits = bits & ~(StreamFlag | SendTypeFlag | ReliableFlag | ProtocolTypeFlag);

            if (bits < 0 || bits >= (uint)ChannelType.LAST)
                return (NetworkMessage)channel.CreateMessage(ChannelType.Invalid);

            NetworkMessage message = (NetworkMessage)channel.CreateMessage(bits);
            message.header.isReliable = isReliable;
            message.header.isStream = isStream;
            message.header.sendType = sendType;
            message.header.channelType = (ChannelType)bits;
            message.header.sequence = packet.ReadUShort();

            OnReadHeader.Invoke(packet, message);
            
            if (message.header.isReliable)
            {
                message.header.ackkey = GenerateAckKey(packet, message);
            }
            
            return message;
        }

        public override NetworkMessage[] ReadHeaders(NetworkPacket packet)
        {
            uint bits = packet.ReadByte();
            //remove flag bits to reveal channel type
            bits = bits & ~(StreamFlag | SendTypeFlag | ReliableFlag | ProtocolTypeFlag);

            if (bits < 0 || bits >= (uint)ChannelType.LAST)
            {
                NetworkMessage[] msgFailList = new NetworkMessage[1];
                msgFailList[0] = (NetworkMessage)channel.CreateMessage(ChannelType.Invalid);
                return msgFailList;
            }

            uint msgCount = packet.ReadByte();
            NetworkMessage[] msg = new NetworkMessage[msgCount];
            for (int i=0; i<msgCount; i++)
            {
                msg[i] = ReadHeader(packet);
            }
            return msg;
        }

        public override void WriteRequest(NetworkPacket packet, NetworkMessage message)
        {
            message.WriteRequest(packet);
        }

        public override void WriteResponse(NetworkPacket packet, NetworkMessage message)
        {
            message.WriteResponse(packet);
        }


        public uint GenerateAckKey(NetworkPacket packet, NetworkMessage message)
        {
            uint sequence = message.header.sequence;
            uint id = message.header.id;

            uint key = sequence | (id << 16);
            return key;
        }
    }
}
