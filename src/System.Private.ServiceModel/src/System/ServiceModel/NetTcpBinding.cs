// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.ServiceModel.Channels;
using System.Xml;

namespace System.ServiceModel
{
    public class NetTcpBinding : Binding
    {
        // private BindingElements
        private TcpTransportBindingElement _transport;
        private BinaryMessageEncodingBindingElement _encoding;
        private long _maxBufferPoolSize;
        private NetTcpSecurity _security = new NetTcpSecurity();

        public NetTcpBinding() { Initialize(); }
        public NetTcpBinding(SecurityMode securityMode)
            : this()
        {
            _security.Mode = securityMode;
        }


        public NetTcpBinding(string configurationName)
            : this()
        {
            if (!String.IsNullOrEmpty(configurationName))
            {
                throw ExceptionHelper.PlatformNotSupported();
            }
        }

        private NetTcpBinding(TcpTransportBindingElement transport,
                      BinaryMessageEncodingBindingElement encoding,
                      NetTcpSecurity security)
            : this()
        {
            _security = security;
        }

        [DefaultValue(ConnectionOrientedTransportDefaults.TransferMode)]
        public TransferMode TransferMode
        {
            get { return _transport.TransferMode; }
            set { _transport.TransferMode = value; }
        }

        [DefaultValue(TransportDefaults.MaxBufferPoolSize)]
        public long MaxBufferPoolSize
        {
            get { return _maxBufferPoolSize; }
            set
            {
                _maxBufferPoolSize = value;
            }
        }

        [DefaultValue(TransportDefaults.MaxBufferSize)]
        public int MaxBufferSize
        {
            get { return _transport.MaxBufferSize; }
            set { _transport.MaxBufferSize = value; }
        }

        [DefaultValue(TransportDefaults.MaxReceivedMessageSize)]
        public long MaxReceivedMessageSize
        {
            get { return _transport.MaxReceivedMessageSize; }
            set { _transport.MaxReceivedMessageSize = value; }
        }

        public XmlDictionaryReaderQuotas ReaderQuotas
        {
            get { return _encoding.ReaderQuotas; }
            set
            {
                if (value == null)
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("value");
                value.CopyTo(_encoding.ReaderQuotas);
            }
        }

        public override string Scheme { get { return _transport.Scheme; } }

        public EnvelopeVersion EnvelopeVersion
        {
            get { return EnvelopeVersion.Soap12; }
        }

        public NetTcpSecurity Security
        {
            get { return _security; }
            set
            {
                if (value == null)
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("value");
                _security = value;
            }
        }

        private void Initialize()
        {
            _transport = new TcpTransportBindingElement();
            _encoding = new BinaryMessageEncodingBindingElement();

            // NetNative and CoreCLR initialize to what TransportBindingElement does in the desktop
            // This property is not available in shipped contracts
            _maxBufferPoolSize = TransportDefaults.MaxBufferPoolSize;
        }

        // check that properties of the HttpTransportBindingElement and 
        // MessageEncodingBindingElement not exposed as properties on BasicHttpBinding 
        // match default values of the binding elements
        private bool IsBindingElementsMatch(TcpTransportBindingElement transport, BinaryMessageEncodingBindingElement encoding)
        {
            if (!_transport.IsMatch(transport))
                return false;

            if (!_encoding.IsMatch(encoding))
                return false;

            return true;
        }

        // In the Win8 profile, some settings for the binding security are not supported.
        private void CheckSettings()
        {
            NetTcpSecurity security = this.Security;
            if (security == null)
            {
                return;
            }

            SecurityMode mode = security.Mode;
            if (mode == SecurityMode.None)
            {
                return;
            }
            else if (mode == SecurityMode.Message)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new NotSupportedException(SR.Format(SR.UnsupportedSecuritySetting, "Mode", mode)));
            }

            // Message.ClientCredentialType = Certificate, IssuedToken or Windows are not supported.
            if (mode == SecurityMode.TransportWithMessageCredential)
            {
                MessageSecurityOverTcp message = security.Message;
                if (message != null)
                {
                    MessageCredentialType mct = message.ClientCredentialType;
                    if ((mct == MessageCredentialType.Certificate) || (mct == MessageCredentialType.IssuedToken) || (mct == MessageCredentialType.Windows))
                    {
                        throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new NotSupportedException(SR.Format(SR.UnsupportedSecuritySetting, "Message.ClientCredentialType", mct)));
                    }
                }
            }

            // Transport.ClientCredentialType = Certificate is not supported.
            Contract.Assert((mode == SecurityMode.Transport) || (mode == SecurityMode.TransportWithMessageCredential), "Unexpected SecurityMode value: " + mode);
            TcpTransportSecurity transport = security.Transport;
            if ((transport != null) && (transport.ClientCredentialType == TcpClientCredentialType.Certificate))
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new NotSupportedException(SR.Format(SR.UnsupportedSecuritySetting, "Transport.ClientCredentialType", transport.ClientCredentialType)));
            }
        }

        public override BindingElementCollection CreateBindingElements()
        {
            this.CheckSettings();

            // return collection of BindingElements
            BindingElementCollection bindingElements = new BindingElementCollection();
            // order of BindingElements is important
            // add security (*optional)
            SecurityBindingElement wsSecurity = CreateMessageSecurity();
            if (wsSecurity != null)
                bindingElements.Add(wsSecurity);
            // add encoding
            bindingElements.Add(_encoding);
            // add transport security
            BindingElement transportSecurity = CreateTransportSecurity();
            if (transportSecurity != null)
            {
                bindingElements.Add(transportSecurity);
            }

            // add transport (tcp)
            bindingElements.Add(_transport);

            return bindingElements.Clone();
        }

        internal static bool TryCreate(BindingElementCollection elements, out Binding binding)
        {
            binding = null;
            if (elements.Count > 6)
                return false;

            // collect all binding elements
            TcpTransportBindingElement transport = null;
            BinaryMessageEncodingBindingElement encoding = null;

            SecurityBindingElement wsSecurity = null;
            BindingElement transportSecurity = null;

            foreach (BindingElement element in elements)
            {
                if (element is SecurityBindingElement)
                    wsSecurity = element as SecurityBindingElement;
                else if (element is TransportBindingElement)
                    transport = element as TcpTransportBindingElement;
                else if (element is MessageEncodingBindingElement)
                    encoding = element as BinaryMessageEncodingBindingElement;
                else
                {
                    if (transportSecurity != null)
                        return false;
                    transportSecurity = element;
                }
            }

            if (transport == null)
                return false;
            if (encoding == null)
                return false;

            TcpTransportSecurity tcpTransportSecurity = new TcpTransportSecurity();
            UnifiedSecurityMode mode = GetModeFromTransportSecurity(transportSecurity);

            NetTcpSecurity security;
            if (!TryCreateSecurity(wsSecurity, mode, false /*session != null*/, transportSecurity, tcpTransportSecurity, out security))
                return false;

            if (!SetTransportSecurity(transportSecurity, security.Mode, tcpTransportSecurity))
                return false;

            NetTcpBinding netTcpBinding = new NetTcpBinding(transport, encoding, security);
            if (!netTcpBinding.IsBindingElementsMatch(transport, encoding))
                return false;

            binding = netTcpBinding;
            return true;
        }

        private BindingElement CreateTransportSecurity()
        {
            return _security.CreateTransportSecurity();
        }

        private static UnifiedSecurityMode GetModeFromTransportSecurity(BindingElement transport)
        {
            return NetTcpSecurity.GetModeFromTransportSecurity(transport);
        }

        private static bool SetTransportSecurity(BindingElement transport, SecurityMode mode, TcpTransportSecurity transportSecurity)
        {
            return NetTcpSecurity.SetTransportSecurity(transport, mode, transportSecurity);
        }

        private SecurityBindingElement CreateMessageSecurity()
        {
            if (_security.Mode == SecurityMode.Message || _security.Mode == SecurityMode.TransportWithMessageCredential)
            {
                throw ExceptionHelper.PlatformNotSupported("NetTcpBinding.CreateMessageSecurity is not supported.");
            }
            else
            {
                return null;
            }
        }

        private static bool TryCreateSecurity(SecurityBindingElement sbe, UnifiedSecurityMode mode, bool isReliableSession, BindingElement transportSecurity, TcpTransportSecurity tcpTransportSecurity, out NetTcpSecurity security)
        {
            if (sbe != null)
                mode &= UnifiedSecurityMode.Message | UnifiedSecurityMode.TransportWithMessageCredential;
            else
                mode &= ~(UnifiedSecurityMode.Message | UnifiedSecurityMode.TransportWithMessageCredential);

            SecurityMode securityMode = SecurityModeHelper.ToSecurityMode(mode);
            Contract.Assert(SecurityModeHelper.IsDefined(securityMode), string.Format("Invalid SecurityMode value: {0}.", securityMode.ToString()));

            if (NetTcpSecurity.TryCreate(sbe, securityMode, isReliableSession, transportSecurity, tcpTransportSecurity, out security))
                return true;

            return false;
        }
    }
}