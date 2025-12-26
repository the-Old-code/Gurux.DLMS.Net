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
        public readonly IGXMedia _media;
        private readonly GXDLMSClient _client;
        private readonly GXDLMSReader _reader;

        public DlmsClient(Settings cfg)
        {
            _media = CreateMedia(cfg);

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
            _reader = new GXDLMSReader(_client, _media, TraceLevel.Off, "");
        }

        public void Open()
        {
            if (!_media.IsOpen) _media.Open();
        }

        public void InitializeConnection()
        {
            _reader.InitializeConnection();
        }

        public object Read(GXDLMSObject obj, int attributeIndex)
        {
            return _reader.Read(obj, attributeIndex);
        }

        public void Close()
        {
            try
            {
                _reader.Close();
            }
            catch
            {
                // Ошибки закрытия
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
            return new GXNet
            {
                Protocol = NetworkType.Tcp,
                HostName = ((GXNet)(cfg.media)).HostName,
                Port = ((GXNet)(cfg.media)).Port
            };
        }
    }
}
