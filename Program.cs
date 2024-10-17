using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

class P2PNode
{
    private static UdpClient udpClient;
    private static IPEndPoint localEndPoint;
    private static bool isRunning = true;
    private static bool hadHandshake = false;
    private static bool hasRecieved = false;
    private static int acknowledge = 5;
    private static int typeOfData = -1; //0 = ack req, 1 = ack resp, 2 = txt, 3 = file
    static void Main(string[] args)
    {
        //nastavenie lokalneho endpointu (IP a port)
        Console.Write("Zadaj cislo portu ktory chces pouzit:");
        int localPort;
        localPort = int.Parse(Console.ReadLine());
        udpClient = new UdpClient(localPort);
        localEndPoint = new IPEndPoint(IPAddress.Any, localPort);

        // Spustenie vlákna na prijímanie správ
        Thread receiveThread = new Thread(ReceiveMessages);
        receiveThread.Start();
        string remoteIp = "";
        int remotePort = 0;
        
        Console.Write("Zadaj IP adresu cieľa (napr. 127.0.0.1): ");
        remoteIp = Console.ReadLine();
        Console.Write("Zadaj port cieľa (napr. 11001): ");
        remotePort = int.Parse(Console.ReadLine());
        
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
        string message = "";
        isRunning = ackFunction(remoteEndPoint);
        while (isRunning)
        {
            // Zadaj správu na odoslanie
            Console.Write("Zadaj správu na odoslanie: ");
            message = Console.ReadLine();
            if (message.Equals("exit"))
            {
                SendMessage(message,remoteEndPoint);
                break;
            }
            SendMessage(message, remoteEndPoint);
        }
        
    }

    
    private static void KillSession(Thread receiveThread){
        receiveThread.Abort();
        udpClient.Close();
    }

    private static void SendMessage(int type, IPEndPoint remoteEndPoint)
    {
        if (type == 0)
        {
            ushort header = CreateHeader(0, (byte)acknowledge, 2, 0);
            string ackMessage = "ackmsg";
            byte[] messageBytes = Encoding.ASCII.GetBytes(ackMessage);
            byte[] messageToSend = CreatePacket(header, messageBytes);
            udpClient.Send(messageToSend, messageToSend.Length, remoteEndPoint);
        }else if (type == 1)
        {
            //summat for ack response
        }
        
    }
    private static void SendMessage(string message, IPEndPoint remoteEndPoint)
    {
        byte[] messageBytes = Encoding.ASCII.GetBytes(message);
        ushort header = CreateHeader(2, (byte)acknowledge, 2, 0);
        byte[] messageToSend = CreatePacket(header, messageBytes);
        udpClient.Send(messageToSend, messageToSend.Length, remoteEndPoint);
        Console.WriteLine("Odoslaná správa: " + message + " do " + remoteEndPoint);
    }

    private static bool ackFunction(IPEndPoint remoteEndPoint)
    {
        if (!hasRecieved)
        {
            hasRecieved = true;
            SendMessage(0, remoteEndPoint);
            return true;
        }
        return false;
    }
    static ushort CreateHeader(byte type, byte ack, byte fragmentFlag, byte fragmentOffset)
    {
        ushort header = 0;
        header |= (ushort)(type << 14);
        header |= (ushort)(ack << 12);
        header |= (ushort)(fragmentFlag << 8);
        header |= (ushort)fragmentOffset;
        return header;
    }
    static int[] UnpackHeader(ushort header)
    {
        int[] returnArray = new int[4];
        returnArray[0] = (byte)((header >> 14) & 0x03); // Posunieme o 14 bitov doprava a zoberieme 2 najvyššie bity
        returnArray[1] = (byte)((header >> 12) & 0x03); // Posunieme o 12 bitov a zoberieme ďalšie 2 bity
        returnArray[2] = (byte)((header >> 8) & 0x0F); // Posunieme o 8 bitov a zoberieme ďalšie 4 bity
        returnArray[3] = (byte)(header & 0xFF); // Zoberieme posledných 8 bitov
        // Výpis rozbalených hodnôt
       return returnArray;
    }
    static byte[] CreatePacket(ushort header, byte[] data)
    {
        byte[] headerBytes = BitConverter.GetBytes(header); // Konverzia hlavičky na pole bajtov
        byte[] packet = new byte[headerBytes.Length + data.Length]; // Vytvorenie balíka

        // Skopírovanie hlavičky na začiatok balíka
        Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);
        // Skopírovanie dát za hlavičku
        Buffer.BlockCopy(data, 0, packet, headerBytes.Length, data.Length);

        return packet;
    }
    static byte[] UnpackPacket(byte[] packet)
    {
        // Extrakcia hlavičky (prvé 2 bajty)
        ushort header = BitConverter.ToUInt16(packet, 0);
        byte[] data = new byte[packet.Length - 2]; // Zvyšok sú dáta
        Buffer.BlockCopy(packet, 2, data, 0, data.Length);

        // Rozbalenie hlavičky
        int[] headerArray = UnpackHeader(header);
        Console.WriteLine(headerArray[0] + ", " + headerArray[1] + ", " + headerArray[2] + ", " + headerArray[3]);
        if (headerArray[0] == 2)
        {
            typeOfData = headerArray[0];
        }
        // Dekódovanie dát
        return data;
    }
    private static void ReceiveMessages()
    {
        while (isRunning)
        {
            try
            {
                byte[] receivedBytes = udpClient.Receive(ref localEndPoint);
                hasRecieved = true;
                byte[] data = UnpackPacket(receivedBytes);
                if (typeOfData == 2)
                {
                    string message = Encoding.ASCII.GetString(data);
                    Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                }else if (typeOfData == 0)
                {
                    SendMessage(1, localEndPoint);
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine("Chyba pri prijímaní správ: " + ex.Message);
            }
        }
    }
}
