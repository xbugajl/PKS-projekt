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
    private static int typeOfData = -1; 
    private static int ack = 5;
    private static int fragmentFlag = -1;
    private static int fragmentOffset = 0;
    private static int currentAck = 0;
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
        ackFunction(remoteEndPoint, ack);
        while (isRunning)
        {
            // Zadaj správu na odoslanie
            Console.Write("Zadaj správu na odoslanie: ");
            message = Console.ReadLine();
            if (message.Equals("exit"))
            {
                SendMessage(message,remoteEndPoint,ack);
                break;
            }
            SendMessage(message, remoteEndPoint, ack);
        }
        
    }
    private static void KillSession(Thread receiveThread){
        receiveThread.Abort();
        udpClient.Close();
    }

    private static void SendMessage(int type, IPEndPoint remoteEndPoint, int acknowledge)
    {
        string ackMessage = "";
        if (type == 0) ackMessage = "ackmsg";
        else if (type == 1) ackMessage = "ackrsp";
        else if (type == 2) ackMessage = "ackrdy";

        byte[] header = CreateHeader((byte)type, (byte)acknowledge, 2, 0);
        byte[] messageBytes = Encoding.ASCII.GetBytes(ackMessage);
        byte[] messageToSend = CreatePacket(header, messageBytes);

        udpClient.Send(messageToSend, messageToSend.Length, remoteEndPoint);
        //Console.WriteLine("Odoslaná správa typu " + type +" s ACK " + acknowledge + " do " + remoteEndPoint);
    }
    private static void SendMessage(string message, IPEndPoint remoteEndPoint, int acknowledge)
    {
        byte[] messageBytes = Encoding.ASCII.GetBytes(message);
        byte[] header = CreateHeader(3, (byte)acknowledge, 2, 0);
        byte[] messageToSend = CreatePacket(header, messageBytes);
        udpClient.Send(messageToSend, messageToSend.Length, remoteEndPoint);
        //Console.WriteLine("Odoslaná správa: " + message + " do " + remoteEndPoint);
    }

    private static bool ackFunction(IPEndPoint remoteEndPoint, int acknowledge)
    {
        if (!hasRecieved)
        {
            hasRecieved = true;
            SendMessage(0, remoteEndPoint, acknowledge);
        
            // Čakanie na odpoveď
            int retryCount = 0;
            while (!hadHandshake && retryCount < 5)
            {
                Thread.Sleep(1000); // Počkáme 1 sekundu
                retryCount++;
            }
            if (hadHandshake)
            {
                //Console.WriteLine("Handshake bol úspešný, pokračujeme v komunikácii.");
            }
        }
        return hadHandshake; 
    }
    static byte[] CreateHeader(ushort type, ushort ack, ushort fragmentFlag, ushort fragmentOffset)
    {
        // Vytvorenie poľa bajtov pre hlavičku (4 časti po 2 bajty)
        byte[] header = new byte[8];

        // Typ (type) -> uložené ako prvé 2 bajty
        Buffer.BlockCopy(BitConverter.GetBytes(type), 0, header, 0, 2);

        // ACK -> uložené ako ďalšie 2 bajty
        Buffer.BlockCopy(BitConverter.GetBytes(ack), 0, header, 2, 2);

        // Fragment Flag -> ďalšie 2 bajty
        Buffer.BlockCopy(BitConverter.GetBytes(fragmentFlag), 0, header, 4, 2);

        // Fragment Offset -> posledné 2 bajty
        Buffer.BlockCopy(BitConverter.GetBytes(fragmentOffset), 0, header, 6, 2);

        // Výpis pre kontrolu
        //Console.WriteLine("Vytvorená hlavička (bajty): " + BitConverter.ToString(header));

        return header;
    }

    static ushort[] UnpackHeader(byte[] header)
    {
        ushort[] returnArray = new ushort[4];

        // Extrahovanie Type (prvé 2 bajty)
        returnArray[0] = BitConverter.ToUInt16(header, 0);

        // Extrahovanie ACK (2. dvojica bajtov)
        returnArray[1] = BitConverter.ToUInt16(header, 2);

        // Extrahovanie Fragment Flag (3. dvojica bajtov)
        returnArray[2] = BitConverter.ToUInt16(header, 4);

        // Extrahovanie Fragment Offset (4. dvojica bajtov)
        returnArray[3] = BitConverter.ToUInt16(header, 6);
        //Console.WriteLine("Rozbalená hlavička - Typ: " + returnArray[0]+ ", ACK: "+returnArray[1]);

        return returnArray;
    }

    static byte[] CreatePacket(byte[] header, byte[] data)
    {
        // Vytvorenie balíka, kde hlavička má 8 bajtov a potom nasledujú dáta
        byte[] packet = new byte[header.Length + data.Length];

        // Skopíruj hlavičku na začiatok balíka
        Buffer.BlockCopy(header, 0, packet, 0, header.Length);

        // Skopíruj dáta za hlavičku
        Buffer.BlockCopy(data, 0, packet, header.Length, data.Length);
        
        //Console.WriteLine("Vytvorený balík (bajty): " + BitConverter.ToString(packet));

        return packet;
    }

    static byte[] UnpackPacket(byte[] packet)
    {
        // Rozbal hlavičku (prvých 8 bajtov)
        byte[] header = new byte[8];
        Buffer.BlockCopy(packet, 0, header, 0, 8);

        // Rozbalené dáta (zvyšok po hlavičke)
        byte[] data = new byte[packet.Length - 8];
        Buffer.BlockCopy(packet, 8, data, 0, data.Length);

        // Rozbalenie hlavičky do jednotlivých častí
        ushort[] headerArray = UnpackHeader(header);
        typeOfData = headerArray[0];
        currentAck = headerArray[1];
        fragmentFlag = headerArray[2];
        fragmentOffset = headerArray[3];

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
                Console.Write(typeOfData);
                if (typeOfData == 2)
                {
                    string message = Encoding.ASCII.GetString(data);
                    Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                }
                else if (typeOfData == 0)
                {
                    string message = Encoding.ASCII.GetString(data);
                    Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                    SendMessage(1, localEndPoint, ack + 1);
                }
                else if (typeOfData == 1)
                {
                    // Skontrolujeme, či prijatý ACK sedí s očakávanou hodnotou
                    if (currentAck == ack + 1)
                    {
                        string message = Encoding.ASCII.GetString(data);
                        Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                        //Console.WriteLine("Úspešne sme dokončili handshake!");

                        // Nastavenie príznaku, že handshake bol úspešný, ale neukončujeme program
                        hadHandshake = true;

                        // Pokračujeme v komunikácii po handshaku
                        //Console.WriteLine("Handshake bol úspešný. Pokračujeme v komunikácii.");
                        SendMessage(2, localEndPoint, currentAck + 1);
                    }else if (currentAck == ack + 2)
                    {
                        string message = Encoding.ASCII.GetString(data);
                        Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                        //Console.WriteLine("Úspešne sme dokončili handshake!");

                        hadHandshake = true;

                        //Console.WriteLine("Handshake bol úspešný. Pokračujeme v komunikácii.");
                    }
                    else
                    {
                        Console.WriteLine("Nesprávny ACK prijatý: očakávalo sa " + (ack + 1) + ", prijaté: " + currentAck);
                        hadHandshake = false;
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Chyba pri prijímaní správ: " + ex.Message);
            }
        }
    }
}
