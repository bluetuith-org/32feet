using InTheHand.Net.Bluetooth;
using InTheHand.Net.Bluetooth.AttributeIds;
using InTheHand.Net.Bluetooth.Sdp;
using InTheHand.Net.Obex;
using InTheHand.Net.Sockets;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace InTheHand.Net
{
    public class ObexBluetoothSessionProperties
    {
        protected BluetoothAddress deviceAddress;

        protected bool sessionStarted = false;
        protected bool transferInProgress = false;

        public enum SessionStatus : int
        {
            CLIENT_SESSION_STARTED,
            LISTENER_SESSION_STARTED,
            CLIENT_TRANSFER_IN_PROGRESS,
            LISTENER_TRANSFER_IN_PROGRESS,
            CLIENT_TRANSFER_STOPPED,
            LISTENER_TRANSFER_STOPPED,
            CLIENT_SESSION_STOPPED,
            LISTENER_SESSION_STOPPED,
            CLIENT_SESSION_ERROR,
            LISTENER_SESSION_ERROR,
        }

        public record struct SessionProperty(
            SessionStatus Status,
            BluetoothAddress Address,
            TransferProperty Property,
            string Error
        );

        public record struct TransferProperty(
            string FileName,
            string MimeType,
            long FileSize,
            long BytesTransferred
        );

        public event EventHandler<SessionProperty> SessionEvent;

        protected void OnSessionEvent(SessionStatus status, TransferProperty transferProperty = default)
        {
            SessionEvent?.Invoke(this, new(status, deviceAddress, transferProperty, null));
        }

        protected void OnSessionEvent(SessionStatus status, string error)
        {
            SessionEvent?.Invoke(this, new(status, deviceAddress, default, error));
        }
    }

    public class ObexBluetoothClientSession : ObexBluetoothSessionProperties
    {
        private const int INVALID_CONNECTION_ID = -1;

        const ObexStatusCode ObexStatus_OK_Final = ObexStatusCode.OK | ObexStatusCode.Final;
        const ObexStatusCode ObexStatus_Continue_Final = ObexStatusCode.Continue | ObexStatusCode.Final;

        private ushort remoteMaxPacket = 0x400;
        private int connectionId = INVALID_CONNECTION_ID;

        private Stream networkStream = null;
        private WebHeaderCollection headers = new();

        public void StartSession(BluetoothAddress address)
        {
            try
            {
                Initialize(address);
            }
            catch (Exception e)
            {
                StopSession();

                OnSessionEvent(SessionStatus.CLIENT_SESSION_ERROR, e.Message);
                OnSessionEvent(SessionStatus.CLIENT_SESSION_STOPPED);

                throw;
            }
        }

        public void StopSession()
        {
            if (!sessionStarted)
                return;

            Disconnect();

            headers = new();

            networkStream?.Close();
            networkStream = null;

            sessionStarted = false;
            transferInProgress = false;

            OnSessionEvent(SessionStatus.CLIENT_SESSION_STOPPED);
        }

        public void PutFile(string filepath)
        {
            try
            {
                PutObexFile(filepath);
            }
            catch (Exception e)
            {
                OnSessionEvent(SessionStatus.CLIENT_SESSION_ERROR, e.Message);
            }
        }

        private void Initialize(BluetoothAddress address)
        {
            if (sessionStarted)
                return;

            var client = new BluetoothClient();
            client.Connect(address, BluetoothService.ObexObjectPush);
            if (!client.Connected)
                throw new Exception("Socket is not connected, closing session");

            networkStream = client.GetStream();
            if (networkStream == null)
                networkStream = new NetworkStream(client.Client, true);

            var status = Connect();
            if ((status & ObexStatus_OK_Final) == 0)
                throw new Exception("Obex connection was not done");

            deviceAddress = address;

            sessionStarted = true;
            OnSessionEvent(SessionStatus.CLIENT_SESSION_STARTED);
        }

        private ObexStatusCode PutObexFile(string filepath)
        {
            if (!sessionStarted)
                throw new Exception("Session has not started yet");

            if (transferInProgress)
                throw new Exception("Transfer is already in progress");

            var fileInfo = new FileInfo(filepath);
            var filename = fileInfo.Name;
            var fileStream = File.OpenRead(filepath);
            var fileMimeType = MimeTypes.GetMimeType(filename);
            int filenameLength = (filename.Length + 1) * 2;
            long filesize = fileInfo.Length;

            int packetLength = 3;
            ObexStatusCode status = 0;

            byte[] buffer = new byte[remoteMaxPacket];
            buffer[0] = (byte)ObexMethod.Put;

            if (connectionId != INVALID_CONNECTION_ID)
            {
                buffer[packetLength] = (byte)ObexHeader.ConnectionID;
                byte[] raw = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(connectionId));
                raw.CopyTo(buffer, packetLength + 1);
                packetLength += 5;
            }

            // Name
            buffer[packetLength] = (byte)ObexHeader.Name;
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)(filenameLength + 3))).CopyTo(buffer, packetLength + 1);
            System.Text.Encoding.BigEndianUnicode.GetBytes(filename).CopyTo(buffer, packetLength + 3);
            packetLength += (3 + filenameLength);

            // Size
            buffer[packetLength] = (byte)ObexHeader.Length;
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(Convert.ToInt32(filesize))).CopyTo(buffer, packetLength + 1);
            packetLength += 5;

            // Content type
            int contentTypeLength = (fileMimeType.Length + 1);// *2;
            buffer[packetLength] = (byte)ObexHeader.Type;
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)(contentTypeLength + 3))).CopyTo(buffer, packetLength + 1);
            System.Text.Encoding.ASCII.GetBytes(fileMimeType).CopyTo(buffer, packetLength + 3);
            packetLength += (3 + contentTypeLength);

            if (headers["AppParameters"] != null)
            {
                buffer[packetLength] = (byte)ObexHeader.AppParameters;
                byte[] headerValue = { 0xe, 1, 0xb1 };// System.Text.Encoding.ASCII.GetBytes(headers["AppParameters"]);
                BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)(headerValue.Length + 3))).CopyTo(buffer, packetLength + 1);
                headerValue.CopyTo(buffer, packetLength + 3);
                packetLength += 3 + headerValue.Length;
            }

            foreach (var headerName in headers.AllKeys)
            {
                if (headerName.StartsWith("User"))
                {
                    // add one of the user defined headers - initially support unicode string values only
                    if (Enum.TryParse<ObexHeader>(headerName, true, out ObexHeader userHeader))
                    {
                        buffer[packetLength] = (byte)userHeader;
                        string headerValue = headers[headerName];
                        int stringLength = (headerValue.Length + 1) * 2;
                        BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)(stringLength + 3))).CopyTo(buffer, packetLength + 1);
                        System.Text.Encoding.BigEndianUnicode.GetBytes(headerValue).CopyTo(buffer, packetLength + 3);
                        packetLength += (3 + stringLength);
                    }
                }
            }

            // write the final packet size
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)packetLength)).CopyTo(buffer, 1);

            try
            {
                // send packet with name header
                networkStream.Write(buffer, 0, packetLength);

                if (CheckResponse(ref status))
                {
                    transferInProgress = true;

                    var totalBytes = (int)filesize;
                    while (totalBytes > 0)
                    {
                        int packetLen = AddBody(buffer, 3, ref totalBytes, fileStream);

                        networkStream.Write(buffer, 0, packetLen);
                        if (!CheckResponse(ref status))
                            break;

                        OnSessionEvent(
                            SessionStatus.CLIENT_TRANSFER_IN_PROGRESS,
                            new TransferProperty(
                                FileName: filename,
                                MimeType: fileMimeType,
                                FileSize: filesize,
                                BytesTransferred: filesize - totalBytes
                            )
                        );
                    }
                }
            }
            finally
            {
                fileStream.Close();

                transferInProgress = false;
                OnSessionEvent(SessionStatus.CLIENT_TRANSFER_STOPPED);
            }

            return status;
        }

        private ObexStatusCode Connect()
        {
            // do obex negotiation
            byte[] connectPacket = new byte[remoteMaxPacket];
            short connectPacketLength = 7;

            new byte[7] { 0x80, 0x00, 0x07, 0x10, 0x00, 0x20, 0x00 }.CopyTo(connectPacket, 0);

            // target
            if (Guid.TryParse(headers["TARGET"], out var targetGuid))
            {
                var targetBytes = GuidHelper.HostToNetworkOrder(targetGuid).ToByteArray();
                connectPacket[connectPacketLength] = (byte)ObexHeader.Target;
                short targetHeaderLen = IPAddress.HostToNetworkOrder((short)(targetBytes.Length + 3));
                BitConverter.GetBytes(targetHeaderLen).CopyTo(connectPacket, connectPacketLength + 1);
                targetBytes.CopyTo(connectPacket, connectPacketLength + 3);

                connectPacketLength += 19;
            }
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(connectPacketLength)).CopyTo(connectPacket, 1);

            networkStream.Write(connectPacket, 0, connectPacketLength);

            byte[] receivePacket = new byte[3];
            StreamReadBlockMust(networkStream, receivePacket, 0, 3);
            if (receivePacket[0] == (byte)ObexStatus_OK_Final)
            {
                // get length
                short len = (short)(IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receivePacket, 1)) - 3);

                byte[] receivePacket2 = new byte[3 + len];
                Buffer.BlockCopy(receivePacket, 0, receivePacket2, 0, 3);
                StreamReadBlockMust(networkStream, receivePacket2, 3, len);
                ObexParser.ParseHeaders(receivePacket2, true, ref remoteMaxPacket, null, headers);
                if (headers["CONNECTIONID"] != null)
                {
                    connectionId = int.Parse(headers["CONNECTIONID"]);
                }
            }

            return (ObexStatusCode)receivePacket[0];
        }

        private int AddBody(byte[] buffer, int lenToBodyHeader, ref int totalBytes, Stream readBuffer)
        {
            int thisRequest = 0;
            int lenToBodyContent = lenToBodyHeader + 3;
            if (totalBytes <= (remoteMaxPacket - lenToBodyContent))
            {
                thisRequest = totalBytes;

                totalBytes = 0;
                buffer[0] = (byte)ObexMethod.PutFinal;
                buffer[lenToBodyHeader] = (byte)ObexHeader.EndOfBody;

            }
            else
            {
                thisRequest = remoteMaxPacket - lenToBodyContent;
                // decrement byte count
                totalBytes -= thisRequest;
                buffer[0] = (byte)ObexMethod.Put;
                buffer[lenToBodyHeader] = (byte)ObexHeader.Body;
            }

            int readBytes = readBuffer.Read(buffer, lenToBodyContent, thisRequest);

            // Before we use the unchecked arithmetic below, check that our
            // maths hasn't failed and we're writing too big length headers.
            ushort check = (ushort)readBytes;
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(unchecked((short)(readBytes + 3)))).CopyTo(buffer, lenToBodyHeader + 1);

            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(unchecked((short)(readBytes + lenToBodyContent)))).CopyTo(buffer, 1);
            int packetLen = readBytes + lenToBodyContent;

            return packetLen;
        }

        private void Disconnect()
        {
            if (networkStream != null)
            {
                ObexStatusCode status = 0;

                short disconnectPacketSize = 3;
                byte[] disconnectPacket = new byte[8];
                disconnectPacket[0] = (byte)ObexMethod.Disconnect;

                // add connectionid header
                if (connectionId != INVALID_CONNECTION_ID)
                {
                    disconnectPacket[3] = (byte)ObexHeader.ConnectionID;
                    BitConverter.GetBytes(IPAddress.HostToNetworkOrder(connectionId)).CopyTo(disconnectPacket, 4);
                    disconnectPacketSize += 5;
                }

                //set packet size
                BitConverter.GetBytes(IPAddress.HostToNetworkOrder(disconnectPacketSize)).CopyTo(disconnectPacket, 1);

                networkStream.Write(disconnectPacket, 0, disconnectPacketSize);

                try
                {
                    CheckResponse(ref status);
                }
                catch (EndOfStreamException)
                {
                    // If the server has closed the connection already we
                    // normally see EoF on reading for the (first byte of the)
                    // response to our Disconnect request.
                }
                catch (IOException)
                {
                    // Other exceptions are possible,
                    // e.g. on _writing_ the Disconnect request.
                }

                networkStream.Close();
            }
        }

        private bool CheckResponse(ref ObexStatusCode status)
        {
            byte[] receiveBuffer = new byte[3];
            StreamReadBlockMust(networkStream, receiveBuffer, 0, receiveBuffer.Length);

            status = (ObexStatusCode)receiveBuffer[0];
            switch (status)
            {
                case ObexStatusCode.OK:
                case ObexStatusCode.Continue:
                case ObexStatus_OK_Final:
                case ObexStatus_Continue_Final:
                    //get length
                    short len = (short)(IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receiveBuffer, 1)) - 3);
                    Debug.Assert(len >= 0, "not got len!");

                    if (len > 0)
                    {
                        bool validObexHeader = true;

                        byte[] receivePacket2 = new byte[len];
                        StreamReadBlockMust(networkStream, receivePacket2, 0, len);

                        switch ((ObexHeader)receivePacket2[0])
                        {
                            case ObexHeader.ConnectionID:
                            case ObexHeader.Name:
                                validObexHeader = true;
                                break;

                            default:
                                validObexHeader = false;
                                break;
                        }

                        if (!validObexHeader)
                        {
                            Debug.Fail("unused headers...");
                        }
                    }

                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// A wrapper for Stream.Read that blocks until the requested number of bytes
        /// have been read, and throw an exception if the stream is closed before that occurs.
        /// </summary>
        private static void StreamReadBlockMust(Stream stream, byte[] buffer, int offset, int size)
        {
            int numRead = StreamReadBlock(stream, buffer, offset, size);
            System.Diagnostics.Debug.Assert(numRead <= size);
            if (numRead < size)
            {
                throw new EndOfStreamException("Connection closed whilst reading an OBEX packet.");
            }
        }

        /// <summary>
        /// A wrapper for Stream.Read that blocks until the requested number of bytes
        /// have been read or the end of the Stream has been reached.
        /// Returns the number of bytes read.
        /// </summary>
        private static int StreamReadBlock(Stream stream, byte[] buffer, int offset, int size)
        {
            int numRead = 0;
            while (size - numRead > 0)
            {
                int curCount = stream.Read(buffer, offset + numRead, size - numRead);
                if (curCount == 0)
                { // EoF
                    break;
                }
                numRead += curCount;
            }
            System.Diagnostics.Debug.Assert(numRead <= size);
            return numRead;
        }
    }

    public class ObexBluetoothListenerSession : ObexBluetoothSessionProperties
    {
        byte[] buffer;

        private WebHeaderCollection headers = null;
        private ObexStreamWriter bodyStream = null;
        private EndPoint localEndPoint;
        private EndPoint remoteEndPoint;
        ushort remoteMaxPacket = 0;

        private BluetoothListener listener = null;
        private Socket socket = null;

        private bool listening = false;

        private static readonly string _temporaryFileName = "obex-tmpfile";
        private string _temporaryFile = null;
        private string _saveFolder = null;

        public string TemporaryFolder
        {
            get
            {
                return Path.GetDirectoryName(_temporaryFile);
            }
            set
            {
                //Check read/write accesses here
                _temporaryFile = Path.Join(value, _temporaryFileName);
            }
        }

        public string SavePath
        {
            get
            {
                return _saveFolder;
            }
            set
            {
                //Check read/write accesses here
                _saveFolder = value;
            }
        }

        public void StartSession()
        {
            try
            {
                if (sessionStarted)
                    throw new Exception("Session has already started");

                if (string.IsNullOrEmpty(_temporaryFileName) || string.IsNullOrEmpty(_saveFolder))
                    throw new ArgumentException("Paths for temporary and/or save directories have not been set");

                ServiceRecord record = CreateServiceRecord();
                listener = new BluetoothListener(BluetoothService.ObexObjectPush, record);
                listener.ServiceClass = ServiceClass.ObjectTransfer;
                listener.Start();
                listening = true;
            }
            catch (Exception e)
            {
                StopSession();
                OnSessionEvent(SessionStatus.LISTENER_SESSION_ERROR, e.Message);
                OnSessionEvent(SessionStatus.LISTENER_SESSION_STOPPED);

                throw;
            }

            sessionStarted = true;
            OnSessionEvent(SessionStatus.LISTENER_SESSION_STARTED);

            while (listening)
            {
                Initialize(true);

                try
                {
                    if (socket == null || !socket.Connected)
                    {
                        var bluetoothClient = listener.AcceptBluetoothClient();
                        socket = bluetoothClient.Client;
                        if (socket == null)
                        {
                            StopSession();
                            throw new Exception("Socket has not been initialized, closing session");
                        }

                        localEndPoint = socket.LocalEndPoint;
                        remoteEndPoint = socket.RemoteEndPoint;

                        deviceAddress = ((BluetoothEndPoint)remoteEndPoint).Address;
                    }

                    PutOperation();
                }
                catch (Exception e)
                {
                    OnSessionEvent(SessionStatus.LISTENER_SESSION_ERROR, e.Message);
                }
            }
        }

        public void StopSession()
        {
            if (!listening)
                return;

            File.Delete(_temporaryFile);

            listening = false;
            sessionStarted = false;

            bodyStream?.Dispose();
            socket?.Close();
            listener?.Stop();

            OnSessionEvent(SessionStatus.LISTENER_SESSION_STOPPED);
        }

        private void Initialize(bool renewBuffer)
        {
            if (renewBuffer)
                buffer = new byte[0x2000];

            headers = new();
            bodyStream?.Dispose();
            bodyStream = new(_temporaryFile);
            bodyStream.WriteEvent += PutEventHandler;
        }

        private void PutCompletedHandler(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new Exception($"Filename to create within {SavePath} was not set");
            }

            bodyStream?.Close();
            bodyStream = null;

            File.Move(_temporaryFile, Path.Join(_saveFolder, filename), true);
        }

        private void PutEventHandler(object sender, long bytesTransferred)
        {
            if (headers is null || headers.Count == 0 || bytesTransferred == 0)
                return;


            long size = 0;
            var length = headers["LENGTH"];

            try
            {
                size = long.Parse(length);
            } catch
            {
                size = -1;
            }
            
            OnSessionEvent(
                SessionStatus.LISTENER_TRANSFER_IN_PROGRESS,
                new TransferProperty(
                    FileName: headers["NAME"],
                    MimeType: headers["TYPE"],
                    FileSize: size,
                    BytesTransferred: bytesTransferred
                )
            );
        }

        private void PutOperation()
        {
            bool moreToReceive = true;
            bool putCompleted = false;

            try
            {
                while (moreToReceive)
                {
                    //receive the request and store the data for the request object
                    int received = 0;

                    while (received < 3)
                    {
                        int readLen = socket.Receive(buffer, received, 3 - received, SocketFlags.None);
                        if (readLen == 0)
                        {
                            moreToReceive = false;
                            if (received == 0)
                            {
                                break; // Waiting for first byte of packet -- OK to close then.
                            }
                            else
                            {
                                throw new EndOfStreamException("Connection lost.");
                            }
                        }
                        received += readLen;
                    }

                    if (received == 3)
                    {
                        ObexMethod method = (ObexMethod)buffer[0];
                        //get length (excluding the 3 byte header)
                        short len = (short)(IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, 1)) - 3);
                        if (len > 0)
                        {
                            int iPos = 0;

                            while (iPos < len)
                            {
                                int wanted = len - iPos;
                                Debug.Assert(wanted > 0, "NOT wanted > 0, is: " + wanted);
                                int receivedBytes = socket.Receive(buffer, iPos + 3, wanted, SocketFlags.None);
                                if (receivedBytes == 0)
                                {
                                    moreToReceive = false;
                                    throw new EndOfStreamException("Connection lost.");
                                }
                                iPos += receivedBytes;
                            }
                        }

                        byte[] responsePacket; // Don't init, then the compiler will check that it's set below.

                        //Debug.WriteLine(s.GetHashCode().ToString("X8") + ": Method: " + method, "ObexListener");
                        responsePacket = HandleAndMakeResponse(ref moreToReceive, ref putCompleted, method);

                        System.Diagnostics.Debug.Assert(responsePacket != null, "Must always respond to the peer.");
                        if (responsePacket != null)
                        {
                            socket.Send(responsePacket);
                        }
                    }
                    else
                    {
                        moreToReceive = false;
                    }
                }
            }
            finally
            {
                OnSessionEvent(SessionStatus.LISTENER_TRANSFER_STOPPED);

                socket?.Close();
                socket = null;
            }

            if (!putCompleted)
            {
                // Should not return the request.
                throw new ProtocolViolationException("No PutFinal received.");
            }
        }

        private byte[] HandleAndMakeResponse(ref bool moretoreceive, ref bool putCompleted, ObexMethod method)
        {
            byte[] responsePacket;

            switch (method)
            {
                case ObexMethod.Connect:
                    ObexParser.ParseHeaders(buffer, true, ref remoteMaxPacket, bodyStream, headers);
                    responsePacket = new byte[7] { 0xA0, 0x00, 0x07, 0x10, 0x00, 0x20, 0x00 };
                    break;
                case ObexMethod.Put:
                    if (putCompleted)
                    {
                        Initialize(false);
                        moretoreceive = true;
                        putCompleted = false;
                    }
                    ObexParser.ParseHeaders(buffer, false, ref remoteMaxPacket, bodyStream, headers);
                    responsePacket = new byte[3] { (byte)(ObexStatusCode.Continue | ObexStatusCode.Final), 0x00, 0x03 };
                    break;
                case ObexMethod.PutFinal:
                    ObexParser.ParseHeaders(buffer, false, ref remoteMaxPacket, bodyStream, headers);
                    responsePacket = new byte[3] { (byte)(ObexStatusCode.OK | ObexStatusCode.Final), 0x00, 0x03 };
                    // Shouldn't return an object if the sender didn't send it all.
                    putCompleted = true; // (Need to just assume that it does contains EndOfBody)
                    PutCompletedHandler(headers["NAME"]);
                    break;
                case ObexMethod.Disconnect:
                    ObexParser.ParseHeaders(buffer, false, ref remoteMaxPacket, bodyStream, headers);
                    responsePacket = new byte[3] { (byte)(ObexStatusCode.OK | ObexStatusCode.Final), 0x00, 0x03 };
                    moretoreceive = false;
                    break;
                default:
                    //Console.WriteLine(method.ToString() + " " + received.ToString());
                    responsePacket = new byte[3] { (byte)ObexStatusCode.NotImplemented, 0x00, 0x03 };
                    moretoreceive = false;
                    putCompleted = false;
                    break;
            }
            return responsePacket;
        }

        private static ServiceRecord CreateServiceRecord()
        {
            ServiceElement englishUtf8PrimaryLanguage = CreateEnglishUtf8PrimaryLanguageServiceElement();
            ServiceRecord record = new ServiceRecord(
                new ServiceAttribute(InTheHand.Net.Bluetooth.AttributeIds.UniversalAttributeId.ServiceClassIdList,
                    new ServiceElement(ElementType.ElementSequence,
                        new ServiceElement(ElementType.Uuid16, (UInt16)0x1105))),
                new ServiceAttribute(InTheHand.Net.Bluetooth.AttributeIds.UniversalAttributeId.ProtocolDescriptorList,
                    ServiceRecordHelper.CreateGoepProtocolDescriptorList()),
                new ServiceAttribute(InTheHand.Net.Bluetooth.AttributeIds.ObexAttributeId.SupportedFormatsList,
                    new ServiceElement(ElementType.ElementSequence,
                        new ServiceElement(ElementType.UInt8, (byte)0xFF)))
                );
            return record;
        }

        private static ServiceElement CreateEnglishUtf8PrimaryLanguageServiceElement()
        {
            ServiceElement englishUtf8PrimaryLanguage = LanguageBaseItem.CreateElementSequenceFromList(
                new LanguageBaseItem[] {
                    new LanguageBaseItem("en", LanguageBaseItem.Utf8EncodingId, LanguageBaseItem.PrimaryLanguageBaseAttributeId)
                });
            return englishUtf8PrimaryLanguage;
        }
    }

    internal class ObexStreamWriter : Stream
    {
        private readonly Stream inner;
        public EventHandler<long> WriteEvent;

        private long totalBytes;

        public ObexStreamWriter(string filepath)
        {
            inner = File.Create(filepath);
        }

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Close()
        {
            inner.Close();
        }

        public override void Flush()
        {
            inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);

            if (count > 0)
            {
                totalBytes += count;
                WriteEvent?.Invoke(this, totalBytes);
            } 
        }
    }
}
