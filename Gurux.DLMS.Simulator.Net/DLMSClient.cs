using Gurux.Common;
using Gurux.DLMS.Enums;
using Gurux.DLMS.Objects;
using Gurux.DLMS.Reader;
using Gurux.DLMS.Secure;
using Gurux.Net;
using System.Diagnostics;

namespace Gurux.DLMS.Simulator.Net
{
    public sealed class DlmsClient : IDisposable
    {
        private readonly Settings _cfg;
        private readonly IGXMedia _media;
        private readonly GXDLMSSecureClient _client;
        GXDLMSReader _reader;

        public DlmsClient(Settings cfg)
        {
            _cfg = cfg;

            // 1) Media (TCP или COM)
            _media = CreateMedia(cfg);

            // 2) DLMS client
            _client = new GXDLMSSecureClient
            {
                UseLogicalNameReferencing = cfg.client.UseLogicalNameReferencing,
                InterfaceType = cfg.client.InterfaceType,
                ClientAddress = cfg.client.ClientAddress,
                ServerAddress = cfg.client.ServerAddress,
                Authentication = cfg.client.Authentication,
                Password = cfg.client.Password

            };

            _reader = new GXDLMSReader(_client, _media, TraceLevel.Off, "");
        }

        public void Open()
        {
            _media.Open();
        }

        public void InitializeConnection()
        {
            var reply = new GXReplyData();
            // SNRM/UA (для HDLC). Для Wrapper обычно SNRMRequest вернёт null.
            byte[] snrm = _client.SNRMRequest();
            if (snrm != null)
            {
                ReadDLMSPacket(snrm, reply);
                _client.ParseUAResponse(reply.Data);
            }
            

            // AARQ/AARE (всегда нужно для нормального соединения). :contentReference[oaicite:5]{index=5}
            reply.Clear();
            foreach (var aarq in _client.AARQRequest())
            {
                reply.Clear();
                ReadDLMSPacket(aarq, reply);
            }
            _client.ParseAAREResponse(reply.Data);

            // HLS: если требуется — делаем Application Association (challenge). :contentReference[oaicite:6]{index=6}
            if (_client.IsAuthenticationRequired)
            {
                reply.Clear();
                if (_client.IsAuthenticationRequired)
                {
                    foreach (byte[] it in _client.GetApplicationAssociationRequest())
                    {
                        reply.Clear();
                        ReadDataBlock(it, reply);   // или ReadDLMSPacket(it, reply) если блоков не ждёшь
                    }
                    //_client.ParseApplicationAssociationResponse(reply.Data);
                }
            }
        }

        public object Read(GXDLMSObject obj, int attributeIndex)
        {
            var reply = new GXReplyData();

            // Read(...) вернёт набор сообщений (byte[][]).
            var requests = _client.Read(obj, attributeIndex);

            foreach (var req in requests)
            {
                reply.Clear();
                ReadDataBlock(req, reply);   // ReadDataBlock умеет докачивать через ReceiverReady
            }

            return _client.UpdateValue(obj, attributeIndex, reply.Value);
        }

        public void Close()
        {
            try
            {
                var reply = new GXReplyData();
                ReadDLMSPacket(_client.DisconnectRequest(), reply);
            }
            catch
            {
                // игнорируем ошибки закрытия
            }
            finally
            {
                _media.Close();
            }
        }

        public void Dispose()
        {
            Close();
        }

        // --- НИЖЕ: низкоуровневые функции обмена ---

        private void ReadDLMSPacket(byte[] data, GXReplyData reply)
        {
            if (data == null)
                return;

            object eop = (byte)0x7E; // HDLC frame end
                                     // В WRAPPER по TCP терминатор не используется.
            if (_client.InterfaceType == InterfaceType.WRAPPER && _media is GXNet)
                eop = null;

            int tries = 0;
            bool ok = false;

            var p = new ReceiveParameters<byte[]>
            {
                AllData = true,
                Eop = eop,
                Count = 5,
                WaitTime = 10000
            };

            lock (_media.Synchronous)
            {
                while (!ok && tries != 3)
                {
                    Trace("<- " + GXCommon.ToHex(data, true));
                    _media.Send(data, null);
                    ok = _media.Receive(p);

                    if (!ok)
                    {
                        if (p.Eop == null)
                            p.Count = 1;

                        if (++tries != 3)
                            continue;

                        throw new IOException("Failed to receive reply from the device in given time.");
                    }
                }

                // Собираем полный COSEM-пакет
                while (!_client.GetData(p.Reply, reply))
                {
                    if (p.Eop == null)
                        p.Count = 1;

                    if (!_media.Receive(p))
                        throw new IOException("Failed to receive reply while assembling COSEM packet.");
                }
            }

            Trace("-> " + GXCommon.ToHex(p.Reply, true));

            if (reply.Error != 0)
                throw new GXDLMSException(reply.Error);
        }

        private void ReadDataBlock(byte[] request, GXReplyData reply)
        {
            // 1) отправили запрос -> получили ответ
            ReadDLMSPacket(request, reply);

            // 2) если ответ "в блоках" — дочитываем через ReceiverReady. :contentReference[oaicite:8]{index=8}
            while (reply.MoreData != RequestTypes.None)
            {
                byte[] rr = _client.ReceiverReady(reply.MoreData);
                reply.Clear();
                ReadDLMSPacket(rr, reply);
            }
        }

        private void Trace(string s)
        {
            if (_cfg.trace == System.Diagnostics.TraceLevel.Verbose)
                Console.WriteLine(s);
        }

        private static IGXMedia CreateMedia(Settings cfg)
        {
            return new GXNet
            {
                HostName = ((GXNet)(cfg.media)).HostName,
                Port = ((GXNet)(cfg.media)).Port
            };
        }

        private static InterfaceType ParseInterfaceType(string s) =>
            s.Equals("Wrapper", StringComparison.OrdinalIgnoreCase) ? InterfaceType.WRAPPER :
            InterfaceType.HDLC;

        private static Authentication ParseAuthentication(string s) =>
            Enum.TryParse<Authentication>(s, true, out var a) ? a : Authentication.None;

        private static System.IO.Ports.Parity ParseParity(string s) =>
            Enum.TryParse<System.IO.Ports.Parity>(s, true, out var p) ? p : System.IO.Ports.Parity.None;

        private static System.IO.Ports.StopBits ParseStopBits(string s) =>
            Enum.TryParse<System.IO.Ports.StopBits>(s, true, out var sb) ? sb : System.IO.Ports.StopBits.One;
    }

    public static class DlmsParsers
    {
        // Register attribute 3 = ScalerUnit = structure{ scaler, unit }
        public static (int scalerPow10, Unit? unit) TryParseScalerUnit(object value)
        {
            // Gurux 9+ часто отдаёт GXStructure/GXArray вместо object[]. :contentReference[oaicite:9]{index=9}
            if (value is GXStructure st && st.Count >= 2)
            {
                int scaler = Convert.ToInt32(st[0]); // обычно sbyte, но приводим аккуратно
                Unit unit = (Unit)Convert.ToInt32(st[1]);
                return (scaler, unit);
            }

            // старый формат (если кто-то включал Client.Version=8)
            if (value is object[] arr && arr.Length >= 2)
            {
                int scaler = Convert.ToInt32(arr[0]);
                Unit unit = (Unit)Convert.ToInt32(arr[1]);
                return (scaler, unit);
            }

            return (0, null);
        }

        public static DateTime? TryParseClockTime(object value)
        {
            if (value is GXDateTime gx)
                return gx.Value.LocalDateTime;

            if (value is DateTime dt)
                return dt;

            return null;
        }
    }
}
