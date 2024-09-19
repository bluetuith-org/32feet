﻿// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.Sockets.Win32NetworkStream (Win32)
// 
// Copyright (c) 2003-2023 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

using System;
using System.IO;
using System.Net.Sockets;

namespace InTheHand.Net.Sockets
{
    internal sealed class Win32NetworkStream : NonSocketNetworkStream
    {
        private readonly Win32Socket _socket;
        private readonly bool _ownsSocket;

        public Win32NetworkStream(Win32Socket socket, bool ownsSocket)
        {
            if (socket is null)
                throw new ArgumentNullException(nameof(socket));

            _socket = socket;
            _ownsSocket = ownsSocket;
        }

        public override void Close()
        {
            if (_ownsSocket)
            {
                _socket.Close();
            }

            base.Close();
        }

        public override bool DataAvailable => _socket.Available > 0;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override long Length => _socket.Available;

        public override bool CanWrite => true;

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _socket.Receive(buffer, offset, count, SocketFlags.None);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _socket.Send(buffer, offset, count, SocketFlags.None);
        }
    }
}
