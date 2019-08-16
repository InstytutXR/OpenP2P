﻿
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenP2P
{
    public class NetworkClient
    {
        public NetworkProtocol protocol = null;

        public IPEndPoint serverHost = null;
        public NetworkPeer server = null;

        public int receiveCnt = 0;
        Stopwatch timer;
        Dictionary<uint, Stopwatch> recieveTimer = new Dictionary<uint, Stopwatch>();
        public NetworkClient(string remoteHost, int remotePort, int localPort)
        {
            protocol = new NetworkProtocol(localPort, false);
            protocol.AttachResponseListener(ChannelType.ConnectToServer, OnResponseConnectToServer);
            protocol.AttachErrorListener(NetworkErrorType.ErrorReliableFailed, OnErrorReliableFailed);
            
            IPEndPoint serverHost = protocol.GetEndPoint(remoteHost, remotePort);
            server = new NetworkPeer(protocol);
            server.AddEndpoint(serverHost);
        }
        
        public void OnErrorReliableFailed(object sender, NetworkPacket packet)
        {
            Console.WriteLine("[ERROR] " + packet.lastErrorType.ToString() + ": " + packet.lastErrorMessage);
        }

        public void ConnectToServer(string userName)
        {
            MsgConnectToServer message = protocol.ConnectToServer(userName);
            message.msgNumber = 10;
            message.msgShort = 20;
            message.msgBool = true;

            protocol.SendReliableMessage(server.GetEndpoint(), message);

            Stopwatch sw = new Stopwatch();
            /*for(int i=0; i<packet.messages.Count; i++)
            {
                recieveTimer.Add(packet.messages[i].header.ackkey, sw);
                sw.Start();
            }*/
            
        }

        public void SendHeartbeat()
        {
            MsgHeartbeat msg = protocol.CreateMessage<MsgHeartbeat>();
            msg.timestamp = NetworkTime.Milliseconds();
            protocol.SendMessage(serverHost, msg);
        }
        
        public void OnResponseConnectToServer(object sender, NetworkMessage message)
        {
            NetworkPacket packet = (NetworkPacket)sender;
            //recieveTimer[message.header.ackkey].Stop();
            //long end = recieveTimer[message.header.ackkey].ElapsedMilliseconds;
            //Console.WriteLine("Ping took: " + end + " milliseconds");
            PerformanceTest();
            //MsgConnectToServer connectMsg = (MsgConnectToServer)message;
        }

        public void PerformanceTest()
        {
            if (receiveCnt == 0)
                timer = Stopwatch.StartNew();

            //Interlocked.Increment(ref receiveCnt);
            receiveCnt++;

            if (receiveCnt == Program.MAXSEND)
            {
                timer.Stop();
                Console.WriteLine("CLIENT Finished in " + ((float)timer.ElapsedMilliseconds / 1000f) + " seconds");
            }
        }
    }
}
