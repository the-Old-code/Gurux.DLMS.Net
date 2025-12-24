using Gurux.Net;
using System.Collections.Concurrent;

namespace Gurux.DLMS.Simulator.Net
{
    class TestClientsManager
    {
        public async Task<List<string>> BeginTesting(Settings settings)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var sem = new SemaphoreSlim(settings.serverCount);
            var tasks = new List<Task>();

            var testResult = new ConcurrentBag<string>();

            int startPort = 55555;
            Console.WriteLine("==========================BEGIN TESTING========================");
            for (int i = 0; i < settings.serverCount; i++)
            {
                int idx = i;                    
                int port = startPort + idx;

                tasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync(cts.Token);
                    try
                    {
                        var one = settings.Clone();

                        var media = (GXNet)one.media;
                        media.Port = port;

                        using var client = new DlmsClient(one);
                        client.Open();
                        client.InitializeConnection();

                        //Console.WriteLine($"{((GXNet)settings.media).HostName}:{port} первичная инициализация пройдена");

                        var reg = new Gurux.DLMS.Objects.GXDLMSRegister("1.0.1.8.0.255");
                        var scalerUnit = client.Read(reg, 3);

                        //Console.WriteLine($"{((GXNet)settings.media).HostName}:{port} Чтение мастштаба текущей энергии пройдено");

                        object rawValue = client.Read(reg, 2);
                        //Console.WriteLine($"{((GXNet)settings.media).HostName}:{port} Чтение текущей энергии пройдено");

                        var clock = new Gurux.DLMS.Objects.GXDLMSClock("0.0.1.0.1.255");
                        var time = client.Read(clock, 2);

                        //Console.WriteLine($"{((GXNet)settings.media).HostName}:{port} Чтение часов пройдено");

                        var ok = $"[{idx:000}] {media.HostName}:{media.Port} OK";

                        //Console.WriteLine($"{((GXNet)settings.media).HostName}:{port} чтение обисов пройдено");

                        testResult.Add(ok);

                        client.Close();
                    }
                    catch (Exception ex)
                    {
                        var err = $"[{idx:000}] {((GXNet)settings.media).HostName}:{port} {((GXNet)settings.media).Protocol} ERROR: {ex.Message}";
                        
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