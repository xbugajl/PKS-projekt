using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using PKSprojekt;
using Timer = System.Timers.Timer;
namespace PKSprojekt;
class GoBackN
{
    private const int MaxSequenceNumber = 8; // Modulo sekvencie
    private const int Timeout = 3000; // Timeout v milisekundách

    private static UdpClient udpClient;
    private static IPEndPoint remoteEndPoint;

    private int baseSeq = 0; // Začiatok okna
    private int nextSeq = 0; // Nasledujúce sekvenčné číslo
    private readonly int windowSize;

    private Timer timer;
    private readonly Dictionary<int, byte[]> sentFragments = new();

    public GoBackN(UdpClient client, IPEndPoint endPoint, int windowSize)
    {
        udpClient = client;
        remoteEndPoint = endPoint;
        this.windowSize = windowSize;

        timer = new Timer(Timeout);
        timer.Elapsed += OnTimeout;
    }

    public void SendPacket(byte[] data)
    {
        if ((nextSeq - baseSeq + MaxSequenceNumber) % MaxSequenceNumber >= windowSize)
        {
            Console.WriteLine("Okno je plné, čaká sa na potvrdenie.");
            return;
        }

        sentFragments[nextSeq] = data;
        SendFragment(nextSeq, data);

        if (baseSeq == nextSeq) timer.Start(); // Spustenie časovača pre prvý nepotvrdený paket

        nextSeq = (nextSeq + 1) % MaxSequenceNumber;
    }

    public void ReceiveAck(int ack)
    {
        if (IsInWindow(baseSeq, ack))
        {
            Console.WriteLine($"ACK prijaté pre paket: {ack}");
            baseSeq = (ack + 1) % MaxSequenceNumber;

            if (baseSeq == nextSeq)
                timer.Stop(); // Všetky pakety potvrdené
            else
                timer.Start(); // Reštart časovača
        }
    }

    private void OnTimeout(object sender, ElapsedEventArgs e)
    {
        Console.WriteLine("Timeout vypršal, opätovné odoslanie nepotvrdených fragmentov...");
        timer.Stop();

        for (int seq = baseSeq; seq != nextSeq; seq = (seq + 1) % MaxSequenceNumber)
        {
            Console.WriteLine($"Znovuodosielam paket: {seq}");
            SendFragment(seq, sentFragments[seq]);
        }

        timer.Start();
    }

    private void SendFragment(int seq, byte[] data)
    {
        byte[] header = P2PNode.CreateHeader(4, 0, 0, (ushort)seq, CRC.ComputeChecksum(data));
        byte[] packet = P2PNode.CreatePacket(header, data);

        udpClient.Send(packet, packet.Length, remoteEndPoint);
        Console.WriteLine($"Odoslaný fragment sekvencie: {seq}");
    }

    private bool IsInWindow(int baseSeq, int seq)
    {
        if (baseSeq <= seq)
            return seq < baseSeq + windowSize;
        return seq < (baseSeq + windowSize) % MaxSequenceNumber;
    }
    public static void SendAck(int seqNumber)
    {
        ushort crc = CRC.ComputeChecksum(new byte[0]);
        byte[] header = P2PNode.CreateHeader(6, (ushort)seqNumber, 0, 0, crc); // Typ 1 = ACK
        byte[] ackPacket = P2PNode.CreatePacket(header, new byte[0]); // Prázdna správa, iba hlavička

        udpClient.Send(ackPacket, ackPacket.Length, remoteEndPoint);
        Console.WriteLine($"ACK odoslané pre sekvenciu: {seqNumber}");
    }
}
