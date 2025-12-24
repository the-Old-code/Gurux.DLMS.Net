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
        private readonly IGXMedia _media;
        private readonly GXDLMSClient _client;
        private readonly GXDLMSReader _reader;

        public DlmsClient(Settings cfg)
        {
            // 1) Media (TCP или COM)
            _media = CreateMedia(cfg);

            // 2) DLMS client
            _client = new GXDLMSClient
            {
                UseLogicalNameReferencing = cfg.client.UseLogicalNameReferencing,
                InterfaceType = cfg.client.InterfaceType,
                ClientAddress = cfg.client.ClientAddress,
                ServerAddress = cfg.client.ServerAddress,
                Authentication = cfg.client.Authentication,
                Password = cfg.client.Password,
                ServiceClass = cfg.client.ServiceClass
                
            };
            _client.Settings.Broacast = true;
            // 3) Reader берёт на себя: SNRM/UA, AARQ/AARE, HLS, блоки, RR, disconnect.
            //    4-й параметр обычно путь к trace/log файлу (можно пустую строку).
            _reader = new GXDLMSReader(_client, _media, TraceLevel.Off, "");
        }

        public void Open()
        {
            // Можно и не вызывать явно, но оставляем ваш API как есть.
            if (!_media.IsOpen)
                _media.Open();
        }

        /// <summary>
        /// Открывает DLMS/COSEM сессию (SNRM + AARQ + HLS при необходимости).
        /// Если надо — можно сразу прочитать Association View (список объектов).
        /// </summary>
        public void InitializeConnection()
        {
            _reader.InitializeConnection();
        }

        public object Read(GXDLMSObject obj, int attributeIndex)
        {
            // GXDLMSReader сам обновляет значение объекта и возвращает уже разобранное значение.
            return _reader.Read(obj, attributeIndex);
        }

        public void Close()
        {
            try
            {
                // Корректный disconnect/release.
                _reader.Close();
            }
            catch
            {
                // Ошибки закрытия часто не критичны (счётчик мог уже разорвать связь).
            }
            finally
            {
                if (_media.IsOpen)
                    _media.Close();
            }
        }

        public void Dispose() => Close();

        private static IGXMedia CreateMedia(Settings cfg)
        {
            // У вас сейчас только TCP пример.
            // Если нужен Serial — сделайте ветку с GXSerial.
            return new GXNet
            {
                HostName = ((GXNet)(cfg.media)).HostName,
                Port = ((GXNet)(cfg.media)).Port
            };
        }
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
