using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PKSprojekt;


class KeepAlive
{
    private readonly UdpClient udpClient;
    private readonly IPEndPoint remoteEndPoint;
    private readonly object lockObject = new object(); // Zámok na synchronizáciu
    private bool isRunning = true;
    private DateTime lastImpulseTime;
    private readonly int timeoutSeconds = 3; // Maximálny čas bez impulzu
    private readonly int checkIntervalMs = 3000; // Interval kontroly (0.5 sekundy)
    private int ack;
    private int failedPingCount = 0; // Počet neúspešných PING pokusov
    private readonly int maxFailedPingCount = 3; // Maximálny počet neúspešných pokusov
    public KeepAlive(UdpClient udpClient, IPEndPoint remoteEndPoint,int ack)
    {
        this.udpClient = udpClient;
        this.remoteEndPoint = remoteEndPoint;
        this.lastImpulseTime = DateTime.Now;
        this.ack = ack; // Nastavíme aktuálny čas
    }

    // Spustenie sledovania aktivity
    public void Start()
    {
        Thread keepAliveThread = new Thread(CheckActivity);
        keepAliveThread.Start();
    }

    // Zastavenie sledovania aktivity
    public void Stop()
    {
        lock (lockObject)
        {
            isRunning = false;
        }
    }

    // Príjem impulzu
    public void ReceiveImpulse()
    {
        lock (lockObject)
        {
            lastImpulseTime = DateTime.Now;
        }
    }

    // Sledovanie aktivity
    private void CheckActivity()
    {
        while (true)
        {
            // Pozastavenie na určitý interval
            Thread.Sleep(checkIntervalMs);

            lock (lockObject)
            {
                if (!isRunning) break; // Skončíme slučku, ak je program ukončený

                if ((DateTime.Now - lastImpulseTime).TotalSeconds > timeoutSeconds)
                {
                    Console.WriteLine("Timeout! Žiadny impulz za posledné 3 sekundy. Odosielam PING.");
                    SendPing();

                    failedPingCount++;
                    if (failedPingCount >= maxFailedPingCount)
                    {
                        Console.WriteLine("Druhý uzol neodpovedá. Program sa ukončí.");
                         // Ukončenie programu
                        P2PNode.KillSession();
                        Environment.Exit(0);
                    }
                }
            }
        }

        Console.WriteLine("KeepAlive vlákno ukončené.");
    }

    public void resetFailedPings()
    {
        failedPingCount = 0;
    }
    // Odoslanie PING správy
    private void SendPing()
    {
        try
        {
            byte[] pingMessage = P2PNode.CreateHeader(10, (byte)(ack+1), 0, 0, 0);
            udpClient.Send(pingMessage, pingMessage.Length, remoteEndPoint);
        }
        catch (Exception ex)
        {
            P2PNode.LogException(ex);
        }
    }
    // Odoslanie PONG správy
    public void SendPong()
    {
        try
        {
            byte[] pongMessage = P2PNode.CreateHeader(11, (byte)(ack-1), 0, 0, 0);
            udpClient.Send(pongMessage, pongMessage.Length, remoteEndPoint);
        }
        catch (Exception ex)
        {
            P2PNode.LogException(ex);
        }
    }
    
}
