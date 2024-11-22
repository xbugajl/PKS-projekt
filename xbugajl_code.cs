﻿
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text;
using System.Threading;
namespace PKSprojekt;

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
    private static int fragmentOffset;
    private static bool fragmentTypeSelected;
    private static int fragmentSize;
    private static int currentAck;
    private static List<byte[]> receivedFragments = new List<byte[]>();
    private static GoBackN gbnSender;

    static void Main(string[] args)
    {
        //nastavenie lokalneho endpointu (IP a port)
        Console.Write("Zadaj cislo portu ktory chces pouzit:");
        int localPort;
        localPort = int.Parse(Console.ReadLine() ?? throw new InvalidOperationException());
        udpClient = new UdpClient(localPort);
        localEndPoint = new IPEndPoint(IPAddress.Any, localPort);

        // Spustenie vlákna na prijímanie správ
        Thread receiveThread = new Thread(ReceiveMessages);
        receiveThread.Start();
        string remoteIp = "";
        int remotePort = 0;
        
        Console.Write("Zadaj IP adresu cieľa (napr. 127.0.0.1): ");
        remoteIp = Console.ReadLine() ?? throw new InvalidOperationException();
        Console.Write("Zadaj port cieľa (napr. 11001): ");
        remotePort = int.Parse(Console.ReadLine() ?? throw new InvalidOperationException());
        
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp ?? throw new InvalidOperationException()), remotePort);
        AckFunction(remoteEndPoint, ack);
        gbnSender = new GoBackN(udpClient, remoteEndPoint, 4); // Veľkosť okna = 4
        while (isRunning)
        {
            Console.WriteLine("\nMenu:"); 
            Console.WriteLine("1. Nastaviť fragmentovanie");
            Console.WriteLine("2. Poslať textovú správu");
            Console.WriteLine("3. Poslať súbor");
            Console.WriteLine("4. Ukončiť");
            Console.Write("Vyber možnosť: "); 
            string choice = Console.ReadLine() ?? throw new InvalidOperationException();
            
            switch (choice) 
            { 
                case "1": 
                    SetFragmentSize(); 
                    break;
                
                case "2": 
                    if (fragmentTypeSelected) 
                    { 
                        SendTextMessage(remoteEndPoint);
                    }
                    else Console.WriteLine("Nevybrali ste si typ odosielania sprav! Pouzite moznost 1!");
                    break;
                case "3":
                if (fragmentTypeSelected)
                {
                    SendFile(remoteEndPoint);
                }
                else Console.WriteLine("Nevybrali ste si typ odosielania sprav! Pouzite moznost 1!"); 
                break; 
                case "4": 
                    isRunning = false; 
                    break;
                default:
                    Console.WriteLine("Neplatná možnosť."); 
                    break;
                }
        }
    }

    private static void SendTextMessage(IPEndPoint remoteEndPoint)
    {
        Console.Write("Zadaj správu na odoslanie: ");
        String message = Console.ReadLine() ?? throw new InvalidOperationException();
        SendMessage(message, remoteEndPoint, ack);
    }
    private static void SetFragmentSize()
    {
        fragmentTypeSelected = true;
        Console.Write("Zadaj veľkosť fragmentu (min. 64, max. 1500): ");
        int newSize = int.Parse(Console.ReadLine() ?? throw new InvalidOperationException());
        if (newSize >= 64 && newSize <= 1500)
        {
            fragmentSize = newSize;
            Console.WriteLine($"Veľkosť fragmentu nastavená na {fragmentSize} bajtov.");
        }
        else
        {
            Console.WriteLine("Neplatná veľkosť fragmentu.");
        }
    }
    private static void SendFile(IPEndPoint remoteEndPoint)
    {
        Console.Write("Zadaj cestu k súboru: ");
        string filePath = Console.ReadLine() ?? throw new InvalidOperationException();
        string fileName = Path.GetFileName(filePath) ?? throw new InvalidOperationException();
        Console.WriteLine(fileName);
        byte[] messageBytes = Encoding.ASCII.GetBytes(fileName);
        ushort crc = CRC.ComputeChecksum(messageBytes);
        byte[] header = CreateHeader(5, (byte)ack, 0, 0, crc);
        byte[] messageToSend = CreatePacket(header, messageBytes);
        udpClient.Send(messageToSend, messageToSend.Length, remoteEndPoint);
        if (!File.Exists(filePath))
        {
            Console.WriteLine("Súbor neexistuje.");
            return;
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        

        Console.WriteLine($"Odosielanie súboru: {fileName}, veľkosť: {fileBytes.Length} bajtov.");
        SendWithFragmentation(fileBytes, remoteEndPoint, fileName);
    }
    private static void KillSession()
    {
        isRunning = false;
    }
    private static void SendWithFragmentation(byte[] data, IPEndPoint remoteEndPoint, string dataType)
    {
        if (data.Length <= fragmentSize)
        {
            // Odoslanie bez fragmentácie
            ushort crc = CRC.ComputeChecksum(data);
            Console.WriteLine($"CRC-16 pre správu: 0x{crc:X4}");
            byte[] header = CreateHeader(3, (ushort)ack, 0, 0, crc);
            byte[] packet = CreatePacket(header, data);
            udpClient.Send(packet, packet.Length, remoteEndPoint);
            Console.WriteLine($"{dataType} odoslaný bez fragmentácie, veľkosť: {data.Length} bajtov.");
        }
        else
        {
            int fragments = (int)Math.Ceiling((double)data.Length / fragmentSize);
            Console.WriteLine($"{dataType} rozdelený na {fragments} fragmentov.");

            for (int i = 0; i < fragments; i++)
            {
                int offset = i * fragmentSize;
                int size = Math.Min(fragmentSize, data.Length - offset);
                byte[] fragment = new byte[size];
                Array.Copy(data, offset, fragment, 0, size);

                int isLastFragment = (i == fragments - 1) ? 2 : 1;// Príznak pre posledný fragment
                ushort crc = CRC.ComputeChecksum(fragment);
                byte[] header = CreateHeader(3, (ushort)ack, (ushort)isLastFragment, (ushort)i, crc);
                byte[] packet = CreatePacket(header, fragment);

                try
                {
                    gbnSender.SendPacket(packet);
                    Console.WriteLine($"Odoslaný fragment {i + 1}/{fragments}, veľkosť: {size} bajtov, posledný: {isLastFragment == 2}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Chyba pri odosielaní fragmentu: " + ex.Message);
                }
            }

            Console.WriteLine($"Všetky fragmenty {dataType} boli úspešne odoslané.");
        }
    }
    private static void SendMessage(int type, IPEndPoint remoteEndPoint, int acknowledge)
    {
        string ackMessage = "";
        if (type == 0) ackMessage = "ackmsg";
        else if (type == 1) ackMessage = "ackrsp";
        else if (type == 2) ackMessage = "ackrdy";
        byte[] messageBytes = Encoding.ASCII.GetBytes(ackMessage);
        ushort crc = CRC.ComputeChecksum(messageBytes);
        byte[] header = CreateHeader((byte)type, (byte)acknowledge, 2, 0, crc);
        
        byte[] messageToSend = CreatePacket(header, messageBytes);

        udpClient.Send(messageToSend, messageToSend.Length, remoteEndPoint);
        //Console.WriteLine("Odoslaná správa typu " + type +" s ACK " + acknowledge + " do " + remoteEndPoint);
    }
    private static void SendMessage(string message, IPEndPoint remoteEndPoint, int acknowledge)
    {
        byte[] messageBytes = Encoding.ASCII.GetBytes(message);
        if (messageBytes.Length <= fragmentSize)
        {
            ushort crc = CRC.ComputeChecksum(messageBytes);
            byte[] header = CreateHeader(4, (byte)acknowledge, 0, 0, crc);
            byte[] messageToSend = CreatePacket(header, messageBytes);
            udpClient.Send(messageToSend, messageToSend.Length, remoteEndPoint);
        }
        else
        {
            int fragments = (int)Math.Ceiling((double)messageBytes.Length / fragmentSize);
            for (int i = 0; i < fragments; i++)
            {
                int offset = i * fragmentSize;
                int size = Math.Min(fragmentSize, messageBytes.Length - offset);
                byte[] fragment = new byte[size];
                Array.Copy(messageBytes, offset, fragment, 0, size);

                int isLastFragment = (i == fragments - 1) ? 2 : 1; // Príznak pre posledný fragment
                ushort crc = CRC.ComputeChecksum(fragment);
                byte[] header = CreateHeader(4, (ushort)ack, (ushort)isLastFragment, (ushort)i, crc);
                byte[] packet = CreatePacket(header, fragment);
                try
                {
                    udpClient.Send(packet, packet.Length, remoteEndPoint);
                    Console.WriteLine($"Odoslaný fragment {i + 1}/{fragments}, veľkosť: {size} bajtov, posledný: {isLastFragment == 2}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Chyba pri odosielaní fragmentu: " + ex.Message);
                }
            }
        }
       
        //Console.WriteLine("Odoslaná správa: " + message + " do " + remoteEndPoint);
    }

    private static void AckFunction(IPEndPoint remoteEndPoint, int acknowledge)
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
    }
    public static byte[] CreateHeader(ushort type, ushort acknow, ushort fragmentflag, ushort fragmentoffset, ushort crc)
    {
        // Vytvorenie poľa bajtov pre hlavičku (4 časti po 2 bajty)
        byte[] header = new byte[8];

        // Typ (type) -> uložené ako prvé 2 bajty
        Buffer.BlockCopy(BitConverter.GetBytes(type), 0, header, 0, 2);

        // ACK -> uložené ako ďalšie 2 bajty
        Buffer.BlockCopy(BitConverter.GetBytes(acknow), 0, header, 2, 2);

        // Fragment Flag -> ďalšie 2 bajty
        Buffer.BlockCopy(BitConverter.GetBytes(fragmentflag), 0, header, 4, 2);

        // Fragment Offset -> posledné 2 bajty
        Buffer.BlockCopy(BitConverter.GetBytes(fragmentoffset), 0, header, 6, 2);
        
        // Checksum CRC 16
        Buffer.BlockCopy(BitConverter.GetBytes(crc), 0, header, 8, 2);

        // Výpis pre kontrolu
        //Console.WriteLine("Vytvorená hlavička (bajty): " + BitConverter.ToString(header));

        return header;
    }

    static ushort[] UnpackHeader(byte[] header)
    {
        ushort[] returnArray = new ushort[5];

        // Extrahovanie Type (prvé 2 bajty)
        returnArray[0] = BitConverter.ToUInt16(header, 0);

        // Extrahovanie ACK (2. dvojica bajtov)
        returnArray[1] = BitConverter.ToUInt16(header, 2);

        // Extrahovanie Fragment Flag (3. dvojica bajtov)
        returnArray[2] = BitConverter.ToUInt16(header, 4);

        // Extrahovanie Fragment Offset (4. dvojica bajtov)
        returnArray[3] = BitConverter.ToUInt16(header, 6);
        
        //Extrahovanie Checksum
        returnArray[4] = BitConverter.ToUInt16(header, 8);
        //Console.WriteLine("Rozbalená hlavička - Typ: " + returnArray[0]+ ", ACK: "+returnArray[1]);

        return returnArray;
    }

   public static byte[] CreatePacket(byte[] header, byte[] data)
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
        if (CRC.VerifyChecksum(data, headerArray[4]))
        {
            return data;
        }

        throw new AuthenticationException("Chyba integrity spravy!");
    }

    private static void AssembleAndSaveFile( byte[] data, string fileName)
    {
        try
        {
            // Získanie cesty k adresáru projektu (kde sú zdrojové kódy)
            string sourceCodePath = Directory.GetCurrentDirectory();

            // Nastavenie cesty k adresáru, kde sa má ukladať súbor
            string assembledFilesPath = Path.Combine(sourceCodePath, "AssembledFiles");

            // Kontrola a vytvorenie adresára, ak neexistuje
            if (!Directory.Exists(assembledFilesPath))
            {
                Directory.CreateDirectory(assembledFilesPath);
                Console.WriteLine("Adresár bol úspešne vytvorený: {0}", assembledFilesPath);
            }

            // Nastavenie cesty pre výsledný súbor
            string fullFilePath = Path.Combine(assembledFilesPath, fileName);

            // Uloženie dát do výsledného súboru
            File.WriteAllBytes(fullFilePath, data);

            Console.WriteLine("Súbor bol úspešne uložený na ceste: {0}", fullFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Proces zlyhal: {0}", ex.Message);
        }
    }



    private static void ReceiveMessages()
    {
        string filename = ""; 
        while (isRunning)
        {
            try
            {
                byte[] receivedBytes = udpClient.Receive(ref localEndPoint);
                hasRecieved = true;
                byte[] data = UnpackPacket(receivedBytes);
                switch (typeOfData)
                {
                    case 0://ackmsg
                        string message = Encoding.ASCII.GetString(data);
                        Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                        SendMessage(1, localEndPoint, ack + 1);
                        break;
                    case 1://ackrsp
                        // Skontrolujeme, či prijatý ACK sedí s očakávanou hodnotou
                        if (currentAck == ack + 1)
                        {
                            message = Encoding.ASCII.GetString(data);
                            Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                            //Console.WriteLine("Úspešne sme dokončili handshake!");

                            // Nastavenie príznaku, že handshake bol úspešný, ale neukončujeme program
                            hadHandshake = true;

                            // Pokračujeme v komunikácii po handshaku
                            //Console.WriteLine("Handshake bol úspešný. Pokračujeme v komunikácii.");
                            SendMessage(2, localEndPoint, currentAck + 1);
                        }else if (currentAck == ack + 2)
                        {
                            message = Encoding.ASCII.GetString(data);
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
                        break;
                    case 2://ackrdy
                        message = Encoding.ASCII.GetString(data);
                        Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                        hadHandshake = true;
                        break;
                    case 3://posielanie suborov
                        if (fragmentFlag == 0) // Nefragmentovaná správa
                        {
                            AssembleAndSaveFile(data,filename);
                        }else // Fragmentovaná správa
                        {
                            receivedFragments.Add(data); // Pridáme prvý prijatý fragment
                            Console.WriteLine($"Prijatý fragment {fragmentOffset + 1}, veľkosť: {data.Length} bajtov.");

                            // Čakanie na ďalšie fragmenty
                            while (fragmentFlag != 2) // 2 označuje posledný fragment
                            {
                                byte[] nextFragment = udpClient.Receive(ref localEndPoint);
                                byte[] fragmentData = UnpackPacket(nextFragment);

                                if (typeOfData != 3) continue; // Ignorujeme iné typy správ

                                receivedFragments.Add(fragmentData);
                                Console.WriteLine($"Prijatý fragment {fragmentOffset + 1}, veľkosť: {fragmentData.Length} bajtov.");
                            }
                            
                            byte[] allData = receivedFragments.SelectMany(fragment => fragment).ToArray();
                            AssembleAndSaveFile(allData,filename);
                            receivedFragments.Clear();
                        }
                        break;
                    case 4://posielanie sprav
                        if (fragmentFlag == 0) // Nefragmentovaná správa
                        {
                            message = Encoding.ASCII.GetString(data);
                            Console.WriteLine("\nPrijatá správa: " + message + " od " + localEndPoint);
                        }
                        else // Fragmentovaná správa
                        {
                            receivedFragments.Add(data); // Pridáme prvý prijatý fragment
                            Console.WriteLine($"Prijatý fragment {fragmentOffset + 1}, veľkosť: {data.Length} bajtov.");

                            // Čakanie na ďalšie fragmenty
                            while (fragmentFlag != 2) // 2 označuje posledný fragment
                            {
                                byte[] nextFragment = udpClient.Receive(ref localEndPoint);
                                byte[] fragmentData = UnpackPacket(nextFragment);

                                if (typeOfData != 4) continue; // Ignorujeme iné typy správ

                                receivedFragments.Add(fragmentData);
                                Console.WriteLine($"Prijatý fragment {fragmentOffset + 1}, veľkosť: {fragmentData.Length} bajtov.");
                            }

                            // Posledný fragment
                            Console.WriteLine("Prijatý posledný fragment. Skladanie správy...");
                            byte[] completeMessageData = receivedFragments.SelectMany(fragment => fragment).ToArray();
                            string completeMessage = Encoding.ASCII.GetString(completeMessageData);

                            Console.WriteLine("\nPrijatá správa: " + completeMessage + " od " + localEndPoint);

                            // Vyčistenie zoznamu fragmentov
                            receivedFragments.Clear();
                        }
                        break;
                    case 5://zistovanie nazvu a typu suboru
                        filename = Encoding.ASCII.GetString(data);
                        Console.WriteLine($"Ideme prijimat subor {filename}!");
                        break;
                    case 6://kontrola pre gbn
                        gbnSender.ReceiveAck(currentAck);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Chyba pri prijímaní správ: " + ex.Message);
            }
        }
        udpClient.Close();
    }
}

