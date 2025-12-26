using Gurux.Net;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Gurux.DLMS.Simulator.Net
{
    class TestClientsManager
    {
        public async Task<List<string>> BeginTesting(Settings settings)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var sem = new SemaphoreSlim(settings.serverCount);//хотелось посмотреть, как ведет себя маршрутизтор, если опрашивать его одновременно кусками, а не все сразу
            var tasks = new List<Task>();

            var testResult = new ConcurrentBag<string>();

            int startPort = 55555;//захардкожено, надо перенести в launchSettings.json
            Console.WriteLine("==========================BEGIN TESTING========================");
            for (int i = 0; i < settings.serverCount; i++)
            {
                int idx = i;                    
                int port = startPort + idx;

                tasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync(cts.Token);
                    var one = settings.Clone();

                    var media = (GXNet)one.media;
                    media.Port = port;

                    using var client = new DlmsClient(one);
                    try
                    {
                        
                        client.Open();
                        client.InitializeConnection();

                        var reg = new Gurux.DLMS.Objects.GXDLMSRegister("1.0.1.8.0.255");
                        var scalerUnit = client.Read(reg, 3);

                        object rawValue = client.Read(reg, 2);

                        var clock = new Gurux.DLMS.Objects.GXDLMSClock("0.0.1.0.1.255");
                        var time = client.Read(clock, 2);

                        var ok = $"[{idx:000}] {media.HostName}:{media.Port} OK";

                        testResult.Add(ok);

                        client.Close();
                    }
                    catch (SocketException ex) 
                    {
                        //Гуруксовский TCP клиент GXNet выкидывает SocketException при попытке подключиться client.Open();, хотя в Wireshark он уже подключился и через минуту разрывает соединение. Почему в C# он пишет, что не может открыть TCP соединение, а в Wireshak он фактически открыл его и через минуту закрыл
                        Console.WriteLine($"SocketErrorCode={ex.SocketErrorCode}, NativeErrorCode={ex.NativeErrorCode}, Message={ex.Message}");
                        var err = $"[{idx:000}] {media.HostName}:{port} {media.Protocol} ERROR: {ex.Message} {ex.StackTrace}";
                        testResult.Add(err);
                    }
                    catch (Exception ex)
                    {
                        var err = $"[{idx:000}] {((GXNet)client._media).HostName}:{port} {((GXNet)client._media).Protocol} ERROR: {ex.Message} {ex.StackTrace}";

                        testResult.Add(err);
                    }
                    finally
                    {
                        sem.Release();
                    }
                }, cts.Token));
            }

            await Task.WhenAll(tasks);

            return testResult.ToList();
        }

    }
}