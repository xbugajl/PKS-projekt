
using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
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
    private static bool showAllFlag = false;
    private static readonly object handshakeLock = new object();
    private static bool hasRecieved = false;
    private static int typeOfData = -1; 
    private static int ack = 5;
    private static int fragmentFlag = -1;
    private static int fragmentOffset;
    private static bool fragmentTypeSelected;
    private static int fragmentSize;
    private static int currentAck;
    private static bool snW = false;
    private static ushort integrityCRC;
    private static KeepAlive keepAlive;
    private static CancellationTokenSource cts = new CancellationTokenSource();
    private static string assembledFilesPath;
    Task monitoringTask = keepAlive.StartAsync(cts.Token);
    private static readonly object snWLock = new object();
    private static List<byte[]> receivedFragments = new List<byte[]>();
    //private static GoBackN gbnSender;

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
        keepAlive = new KeepAlive(udpClient, remoteEndPoint, ack);
        
        if (hadHandshake)
        {
            Task.Run(async () => await keepAlive.StartAsync(cts.Token));
            while (isRunning)
            {
                Console.WriteLine("\nMenu:"); 
                Console.WriteLine("1. Nastaviť fragmentovanie");
                Console.WriteLine("2. Poslať textovú správu");
                Console.WriteLine("3. Poslať súbor");
                Console.WriteLine("4. Nastavenie spoôsobu zobrazenia");
                Console.WriteLine("5. Nastavenie miesta pre ulozenie suboru");
                Console.WriteLine("6. Ukončiť");
                Console.Write("Vyber možnosť: "); 
                string choice = Console.ReadLine() ?? throw new InvalidOperationException();
                string sourceCodePath = Directory.GetCurrentDirectory();
                assembledFilesPath = Path.Combine(sourceCodePath, "AssembledFiles");
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
                        SetDisplay();
                        break;
                    case "5": 
                        SetDirectory();
                        break;
                    case "6":
                        KillSession();
                        break;
                    default:
                        Console.WriteLine("Neplatná možnosť."); 
                        break;
                }
            }
        }
    }

    private static void SetDisplay()
    {
        Console.WriteLine("\nZvoľte ci chcete zobrazovat vsetky spravy alebo len dolezite");
        Console.WriteLine("1. Zobrazit vsetko");
        Console.WriteLine("2. Zobrazit len dolezite");
        string choice = Console.ReadLine() ?? throw new InvalidOperationException();
        switch (choice)
        {
            case "1":
                showAllFlag = true;
                break;
            case "2":
                showAllFlag = false;
                break;
            default:
                Console.WriteLine("Neplatna volba");
                break;
        }
    }

    private static void SetDirectory()
    {
        Console.Write("Zadaj cestu k adresáru, kde chceš uložiť súbor (alebo stlač ENTER pre predvolenú cestu): ");
        string userPath = Console.ReadLine();

        // Ak používateľ nezadal cestu, použije sa predvolená cesta
        if (!string.IsNullOrWhiteSpace(userPath))
        {
            assembledFilesPath = userPath;
            if (!Directory.Exists(assembledFilesPath))
            {
                Directory.CreateDirectory(assembledFilesPath);
                Console.WriteLine("Adresár bol úspešne vytvorený: {0}", assembledFilesPath);
            }
        }
        else
        {
            Console.WriteLine($"Zadany enter pouzita predvolena cesta {assembledFilesPath}");
        }
        
    }
    private static void SetHandshake(bool value)
    {
        lock (handshakeLock)
        {
            hadHandshake = value;
        }
    }
    private static bool GetHandshake()
    {
        lock (handshakeLock)
        {
            return hadHandshake;
        }
    }

    private static bool ResendUntilAck(byte[] packet, IPEndPoint remoteEndPoint)
    {
        snW = false; // Reset Stop-and-Wait príznaku pre nový prenos
        int retryCount = 0;
        const int maxRetries = 5;
        const int timeout = 100; // 0.1 sekundy

        while (retryCount < maxRetries)
        {
            try
            {
                // Odošleme paket
                udpClient.Send(packet, packet.Length, remoteEndPoint);
                Console.WriteLine($"Odoslaný pokus {retryCount + 1}, čaká sa na ACK...");

                // Čakanie na ACK so stanoveným timeoutom
                int elapsedTime = 0;
                while (!snW && elapsedTime < timeout)
                {
                    Thread.Sleep(10); // Kontrolujeme ACK každých 10 ms
                    elapsedTime += 10;
                }

                // Ak prišlo ACK, ukončíme cyklus
                lock (snWLock)
                {
                    if (snW)
                    {
                        Console.WriteLine("ACK prijaté, ukončujem odosielanie.");
                        return true;
                    }
                }

                retryCount++;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        // Ak po maximálnom počte pokusov nie je ACK, vrátime false
        Console.WriteLine("ACK neprišlo po maximálnom počte pokusov. Zlyhanie prenosu.");
        return false;
    }


    private static void SendTextMessage(IPEndPoint remoteEndPoint)
    {
        Console.Write("Zadaj správu na odoslanie: ");
        string message = Console.ReadLine() ?? throw new InvalidOperationException();
        SendMessage(message, remoteEndPoint, ack);
    }
    private static void SetFragmentSize()
    {
        fragmentTypeSelected = true;
        Console.Write("Zadaj veľkosť fragmentu (max. 1464): ");//toto koli tomu aby sme sa vyhli IP fragmentacii
        int newSize = int.Parse(Console.ReadLine() ?? throw new InvalidOperationException());
        if (newSize <= 1464)
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
    public static void LogException(Exception ex)
    {
        string logPath = "error.log";
        File.AppendAllText(logPath, $"{DateTime.Now}: {ex.Message}\n{ex.StackTrace}\n");
    }
    public static void KillSession()
    {
        Console.WriteLine("Ukončujem program...");
        if (cts != null)
        {
            cts.Cancel();
        }

        // Nastavte `isRunning` na false, aby hlavná slučka aj vlákna skončili
        isRunning = false;
        // Zavrite UdpClient
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient.Dispose();
        }

        // Čakajte na ukončenie vlákna (ak chcete mať istotu, že všetky vlákna skončili)
        Thread.Sleep(500); // Dajte čas na ukončenie
        Console.WriteLine("Program bol korektne ukončený.");
    }
    private static void SendWithFragmentation(byte[] data, IPEndPoint remoteEndPoint, string dataType)
    {
        if (data.Length <= fragmentSize)
        {
            // Odoslanie bez fragmentácie
            ushort crc = CRC.ComputeChecksum(data);
            if(showAllFlag) Console.WriteLine($"CRC-16 pre správu: 0x{crc:X4}");
            byte[] header = CreateHeader(3, (ushort)ack, 0, 0, crc);
            byte[] packet = CreatePacket(header, data);
            try
            {
                if(showAllFlag) Console.WriteLine("Pokus o prve odoslanie fragmentu");
                //udpClient.Send(packet, packet.Length, remoteEndPoint);
                if(showAllFlag) Console.WriteLine("Čaká sa na ACK...");
            
            }
            catch (Exception ex)
            {
               LogException(ex);
            }
            if (ResendUntilAck(packet, remoteEndPoint))
            {
                Console.WriteLine($"{dataType} odoslaný bez fragmentácie, veľkosť: {data.Length} bajtov.");
            }
            
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
                if(showAllFlag) Console.WriteLine($"CRC: {crc}");
                byte[] header = CreateHeader(3, (ushort)ack, (ushort)isLastFragment, (ushort)i, crc);
                byte[] packet = CreatePacket(header, fragment);
                if (ResendUntilAck(packet, remoteEndPoint))
                {
                   Console.WriteLine($"Odoslaný fragment {i + 1}/{fragments}, veľkosť: {size} bajtov, posledný: {isLastFragment == 2}");
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
        byte[] header = CreateHeader((byte)type, (byte)acknowledge, 0, 0, crc);
        
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
            byte[] packet = CreatePacket(header, messageBytes);
            if (ResendUntilAck(packet, remoteEndPoint))
            {
                Console.WriteLine($"Odoslaná správa {message}, veľkosť: {message.Length}");
            } 
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
                if (ResendUntilAck(packet, remoteEndPoint))
                {
                    Console.WriteLine($"Odoslaný fragment {i + 1}/{fragments}, veľkosť: {size} bajtov, posledný: {isLastFragment == 2}");
                } 
            }
        }
    }

    private static void AckFunction(IPEndPoint remoteEndPoint, int acknowledge)
    {
        int retryCount = 0;
        const int maxRetries = 10;
        const int waitTimeMs = 3000;
        SendMessage(0, remoteEndPoint, acknowledge);
        while (retryCount < maxRetries)
        {
            if (GetHandshake()) break;

            Console.WriteLine($"Čakám na handshake odpoveď (pokús číslo {retryCount + 1})...");
            Thread.Sleep(waitTimeMs);
            retryCount++;
        }

        if (!GetHandshake())
        {
            Console.WriteLine("Handshake zlyhal po maximálnom počte pokusov.");
            KillSession();
        }
        else
        {
            Console.WriteLine("Handshake úspešne dokončený.");
        }
    }
    public static byte[] CreateHeader(ushort type, ushort acknow, ushort fragmentflag, ushort fragmentoffset, ushort crc)
    {
        // Vytvorenie poľa bajtov pre hlavičku (4 časti po 2 bajty)
        byte[] header = new byte[10];

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
        //Console.WriteLine($"Odosielaný paket: {BitConverter.ToString(header)}");
        return header;
    }

    private static ushort[] UnpackHeader(byte[] header)
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
        //Console.WriteLine($"Prijaty paket: {BitConverter.ToString(header)}");
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
        //Console.WriteLine($"Odosielaný paket: {BitConverter.ToString(packet)}");
        return packet;
    }

    private static byte[] UnpackPacket(byte[] packet)
    {
        // Rozbal hlavičku (prvých 8 bajtov)
        byte[] header = new byte[10];
        Buffer.BlockCopy(packet, 0, header, 0, 10);
        //Console.WriteLine($"Prijaty paket: {BitConverter.ToString(header)}");
        // Rozbalené dáta (zvyšok po hlavičke)
        byte[] data = new byte[packet.Length - 10];
        Buffer.BlockCopy(packet, 10, data, 0, data.Length);

        // Rozbalenie hlavičky do jednotlivých častí
        ushort[] headerArray = UnpackHeader(header);
        typeOfData = headerArray[0];
        currentAck = headerArray[1];
        fragmentFlag = headerArray[2];
        fragmentOffset = headerArray[3];
        integrityCRC = headerArray[4];
        
        return SimulateCRCCorruption(data, 0.01);
    }
    
    private static void sendACK(bool success)
    {
        byte[] header;
        if (success)
        {
            header = CreateHeader(6, (byte)(ack + 1), 0, 0, 0); // Správne prijatý fragment
            Console.WriteLine("Odosielam ACK: " + (ack + 1));
        }
        else
        {
            header = CreateHeader(6, (byte)(ack - 1), 0, 0, 0); // Nesprávny fragment
            Console.WriteLine("Odosielam NACK: " + (ack - 1));
        }

        byte[] messageToSend = CreatePacket(header, new byte[0]);
        udpClient.Send(messageToSend, messageToSend.Length, localEndPoint);
    }
    private static byte[] SimulateCRCCorruption(byte[] data, double corruptionProbability)
    {
        Random random = new Random();

        // Skontroluje, či má dôjsť k poškodeniu
        if (random.NextDouble() < corruptionProbability)
        {
            // Vyberie náhodný bajt na poškodenie
            int corruptIndex = random.Next(data.Length);
            data[corruptIndex] ^= 0xFF; // Poškodenie bajtu (inverzia)
            Console.WriteLine($"Simulácia: Poškodený bajt na indexe {corruptIndex}.");
        }

        return data;
    }

    private static void ReceiveMessages()
    {
        string filename = ""; 
        while (isRunning)
        {
            try
            {
                byte[] receivedBytes = udpClient.Receive(ref localEndPoint);
                if (keepAlive != null)
                {
                    keepAlive.ReceiveImpulse();
                }
                byte[] data = UnpackPacket(receivedBytes);
                if (keepAlive != null)
                {
                    keepAlive.ReceiveImpulse();
                }
                switch (typeOfData)
                {
                    case 0://ackmsg
                        string message = Encoding.ASCII.GetString(data);
                        if(showAllFlag) Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                        SendMessage(1, localEndPoint, ack + 1);
                        break;
                    case 1://ackrsp
                        // Skontrolujeme, či prijatý ACK sedí s očakávanou hodnotou
                        if (currentAck == ack + 1)
                        {
                            message = Encoding.ASCII.GetString(data);
                            if(showAllFlag) Console.WriteLine("\nPrijatá správa: "+ message +" od "+ localEndPoint);
                            //Console.WriteLine("Úspešne sme dokončili handshake!");

                            // Nastavenie príznaku, že handshake bol úspešný, ale neukončujeme program
                            hadHandshake = true;

                            // Pokračujeme v komunikácii po handshaku
                            //Console.WriteLine("Handshake bol úspešný. Pokračujeme v komunikácii.");
                            SendMessage(2, localEndPoint, currentAck + 1);
                        }
                        else
                        {
                            Console.WriteLine("Nesprávny ACK prijatý: očakávalo sa " + (ack + 1) + ", prijaté: " + currentAck);
                            hadHandshake = false;
                        }
                        break;
                    case 2://ackrdy
                        message = Encoding.ASCII.GetString(data);
                        if(showAllFlag) Console.WriteLine("\nPrijatá správa ACKRDY: " + message + " od " + localEndPoint);

                        if (!GetHandshake())
                        {
                            SetHandshake(true);
                            Console.WriteLine("Handshake nastavený na úspešný.");
                        }
                        else
                        {
                            Console.WriteLine("Handshake už bol úspešne nastavený.");
                        }
                        break;
                    case 3: // Posielanie súborov
                        if (fragmentFlag == 0) // Nefragmentovaná správa
                        {
                            if (CRC.VerifyChecksum(data, integrityCRC))
                            {
                                sendACK(true);
                                try
                                {
                                    string fullFilePath = Path.Combine(assembledFilesPath, filename);
                                    // Uloženie dát do výsledného súboru
                                    File.WriteAllBytes(fullFilePath, data);
                                    Console.WriteLine("Súbor bol úspešne uložený na ceste: {0}", fullFilePath);
                                }
                                catch (Exception ex)
                                {
                                    LogException(ex);
                                }
                            }
                            else
                            {
                                sendACK(false);
                            }
                        }
                        else // Fragmentovaná správa
                        {
                            // Čakanie na ďalšie fragmenty
                            while (fragmentFlag != 2) // 2 označuje posledný fragment
                            {
                                byte[] nextFragment = udpClient.Receive(ref localEndPoint);
                                byte[] fragmentData = UnpackPacket(nextFragment);

                                //if (typeOfData != 3) continue; // Ignorujeme iné typy správ
                                
                                if (CRC.VerifyChecksum(fragmentData, integrityCRC))
                                {
                                    sendACK(true);

                                    // Kontrola, či fragment už neexistuje
                                    if (receivedFragments.All(f => BitConverter.ToUInt16(f, 0) != fragmentOffset))
                                    {
                                        receivedFragments.Add(fragmentData);
                                        Console.WriteLine($"Prijatý fragment {fragmentOffset + 1}, veľkosť: {fragmentData.Length} bajtov.");
                                    }
                                    else
                                    {
                                        if(showAllFlag) Console.WriteLine($"Ignorovaný duplikát fragmentu {fragmentOffset + 1}.");
                                    }
                                }
                                else
                                {
                                    sendACK(false);
                                }
                            }

                            byte[] allData = receivedFragments.SelectMany(fragment => fragment).ToArray();
                            try
                            {
                                string fullFilePath = Path.Combine(assembledFilesPath, filename);
                                // Uloženie dát do výsledného súboru
                                File.WriteAllBytes(fullFilePath, allData);
                                Console.WriteLine("Súbor bol úspešne uložený na ceste: {0}", fullFilePath);
                            }
                            catch (Exception ex)
                            {
                                LogException(ex);
                            }
                            receivedFragments.Clear();
                        }
                        break;
                    case 4: // Posielanie správ
                        if (fragmentFlag == 0) // Nefragmentovaná správa
                        {
                            
                            if (CRC.VerifyChecksum(data, integrityCRC))
                            {
                                message = Encoding.ASCII.GetString(data);
                                Console.WriteLine("\nPrijatá správa: " + message + " od " + localEndPoint);
                                sendACK(true);
                            }
                            else
                            {
                                sendACK(false);
                            }
                        }
                        else // Fragmentovaná správa
                        {
                            // Pridanie prvého fragmentu po kontrole duplikátov
                            
                            // Čakanie na ďalšie fragmenty
                            while (true) // 2 označuje posledný fragment
                            {
                                byte[] nextFragment = udpClient.Receive(ref localEndPoint);
                                keepAlive.ReceiveImpulse();
                                byte[] fragmentData = UnpackPacket(nextFragment);
                                
                                if (typeOfData != 4) continue; // Ignorujeme iné typy správ
                                Console.WriteLine(integrityCRC);
                                if (CRC.VerifyChecksum(data, integrityCRC))
                                {
                                    sendACK(true);
                                    if (!receivedFragments.Any(f => BitConverter.ToUInt16(f, 0) == fragmentOffset))
                                    {
                                        receivedFragments.Add(fragmentData);
                                        Console.WriteLine($"Prijatý fragment {fragmentOffset + 1}, veľkosť: {fragmentData.Length} bajtov.");
                                    }
                                    else
                                    {
                                        if(showAllFlag) Console.WriteLine($"Ignorovaný duplikát fragmentu {fragmentOffset + 1}.");
                                    }

                                    if (fragmentFlag == 2) break;
                                }
                                else
                                {
                                    sendACK(false);
                                }

                                integrityCRC = 0;
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
                        if(showAllFlag) Console.WriteLine($"Ideme prijimat subor {filename}!");
                        break;
                    case 6: // ACK správa pre Stop-and-Wait
                        lock (snWLock)
                        {
                            if (currentAck == ack + 1)
                            {
                                Console.WriteLine("ACK prijaté.");
                                snW = true;
                            }
                            else
                            {
                                Console.WriteLine($"Nesprávne ACK: očakávalo sa {ack + 1}, prijaté {currentAck}.");
                                snW = false;
                            }
                        }
                        break;
                    case 10:
                        keepAlive.SendPong();
                        if(showAllFlag) Console.WriteLine("Prijaty ping odosielam pong");
                        break;
                    case 11:
                        if(showAllFlag) Console.WriteLine("Prijaty pong ");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }
    }
}

