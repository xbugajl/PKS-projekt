using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PKSprojekt;


class KeepAlive
{
    private readonly UdpClient udpClient;
    private readonly IPEndPoint remoteEndPoint;
    private readonly object lockObject = new object(); // Synchronizácia zdieľaných dát
    private DateTime lastImpulseTime;
    private readonly int timeoutSeconds = 3; // Maximálny čas bez impulzu
    private readonly int checkIntervalMs = 3000; // Interval kontroly (3 sekundy)
    private int ack;
    private int failedPingCount = 0; // Počet neúspešných PING pokusov
    private readonly int maxFailedPingCount = 3; // Maximálny počet neúspešných pokusov

    public KeepAlive(UdpClient udpClient, IPEndPoint remoteEndPoint, int ack)
    {
        this.udpClient = udpClient;
        this.remoteEndPoint = remoteEndPoint;
        this.lastImpulseTime = DateTime.Now;
        this.ack = ack; // Inicializujeme hodinu potvrdenia
    }

    // Asynchrónne spustenie sledovania s podporou CancellationToken
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkIntervalMs, cancellationToken);

                lock (lockObject)
                {
                    if ((DateTime.Now - lastImpulseTime).TotalSeconds > timeoutSeconds)
                    {
                        Console.WriteLine("Timeout! Žiadny impulz za posledné 3 sekundy. Odosielam PING.");
                        SendPing();

                        failedPingCount++;
                        if (failedPingCount >= maxFailedPingCount)
                        {
                            Console.WriteLine("Druhý uzol neodpovedá. Ukončujem pripojenie.");
                            P2PNode.KillSession();
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ukončenie vlákna po zrušení CancellationToken
                Console.WriteLine("Monitoring bol ukončený.");
                break;
            }
            catch (Exception ex)
            {
                // Logovanie neočakávanej chyby
                P2PNode.LogException(ex);
            }
        }
    }

    // Prijatie impulzu
    public void ReceiveImpulse()
    {
        lock (lockObject)
        {
            failedPingCount = 0;
            lastImpulseTime = DateTime.Now;
        }
    }

    // Odoslanie PING správy
    private void SendPing()
    {
        try
        {
            byte[] pingMessage = P2PNode.CreateHeader(10, (byte)(ack + 1), 0, 0, 0);
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
            byte[] pongMessage = P2PNode.CreateHeader(11, (byte)(ack - 1), 0, 0, 0);
            udpClient.Send(pongMessage, pongMessage.Length, remoteEndPoint);
        }
        catch (Exception ex)
        {
            P2PNode.LogException(ex);
        }
    }
}