﻿// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.BluetoothEndPoint (Sockets)
// 
// Copyright (c) 2003-2023 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace InTheHand.Net
{
    /// <summary>
    /// Represents a network endpoint as a Bluetooth address and a Service Class Id and/or a port number.
    /// </summary>
    public sealed class BluetoothEndPoint : EndPoint
    {
        internal const byte AddressFamilyBluetooth = 32;
        internal const byte AddressFamilyBlueZ = 31;

        private readonly ulong _bluetoothAddress;
        private readonly Guid _serviceId;
        private readonly int _port;

        /// <summary>
        /// Initializes a new instance of the BluetoothEndPoint class with the specified address and service.
        /// </summary>
        /// <param name="address">The Bluetooth address of the device.</param>
        /// <param name="service">The Bluetooth service to use.</param>
        /// <param name="port">Optional port number.</param>
        public BluetoothEndPoint(BluetoothAddress address, Guid service, int port = 0) : this(address.ToUInt64(), service, port) { }

        internal BluetoothEndPoint(ulong bluetoothAddress, Guid serviceId, int port = 0)
        {
            _bluetoothAddress = bluetoothAddress;
            _serviceId = serviceId;
            _port = port;
        }

        internal BluetoothEndPoint(byte[] sockaddr_bt)
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                if (sockaddr_bt[0] != AddressFamilyBlueZ)
                    throw new ArgumentException("Invalid AddressFamily", nameof(sockaddr_bt));

                _bluetoothAddress = BitConverter.ToUInt64(sockaddr_bt, 2) & 0xFFFFFFFFFFFF;
                Debug.WriteLine($"Address: {_bluetoothAddress:X12}");
                _port = BitConverter.ToInt16(sockaddr_bt, 8);
                Debug.WriteLine($"Port: {_port:X4}");
            }
            else
            {
                if (sockaddr_bt[0] != AddressFamilyBluetooth)
                    throw new ArgumentException(nameof(sockaddr_bt));

                _bluetoothAddress = BitConverter.ToUInt64(sockaddr_bt, 2);

                byte[] servicebytes = new byte[16];

                for (int ibyte = 0; ibyte < 16; ibyte++)
                {
                    servicebytes[ibyte] = sockaddr_bt[10 + ibyte];
                }

                _serviceId = new Guid(servicebytes);
                _port = BitConverter.ToInt32(sockaddr_bt, 26);
            }
        }

        /// <summary>
        /// Gets the address family of the Bluetooth address.
        /// </summary>
        public override AddressFamily AddressFamily
        {
            get
            {
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    return (AddressFamily)AddressFamilyBlueZ;
                }
                else
                {
                    return (AddressFamily)AddressFamilyBluetooth;
                }
            }
        }

        /// <summary>
        /// Gets the Bluetooth address of the endpoint.
        /// </summary>
        public BluetoothAddress Address
        {
            get
            {
                return _bluetoothAddress;
            }
        }

        /// <summary>
        /// Gets the Bluetooth service to use for the connection.
        /// </summary>
        public Guid Service
        {
            get
            {
                return _serviceId;
            }
        }

        /// <summary>
        /// Gets the port number (or -1 for any).
        /// </summary>
        public int Port
        {
            get
            {
                return _port;
            }
        }

        /// <summary>
        /// Creates an endpoint from a socket address.
        /// </summary>
        /// <param name="socketAddress"></param>
        /// <returns></returns>
        public override EndPoint Create(SocketAddress socketAddress)
        {
            if (socketAddress == null)
            {
                throw new ArgumentNullException(nameof(socketAddress));
            }

            if (socketAddress.Family == AddressFamily)
            {
                int ibyte;

                var socketAddressBytes = socketAddress.ToByteArray();

                ulong address = BitConverter.ToUInt64(socketAddressBytes, 2);

                byte[] servicebytes = new byte[16];
                int port;

                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    port = BitConverter.ToInt32(socketAddressBytes, 8);
                }
                else
                {
                    for (ibyte = 0; ibyte < 16; ibyte++)
                    {
                        servicebytes[ibyte] = socketAddress[10 + ibyte];
                    }

                    port = BitConverter.ToInt32(socketAddressBytes, 26);
                }

                return new BluetoothEndPoint(address, new Guid(servicebytes), port);
            }

            return base.Create(socketAddress);
        }

        /// <summary>
        /// Serializes endpoint information into a <see cref="SocketAddress"/> instance.
        /// </summary>
        /// <returns></returns>
        public override SocketAddress Serialize()
        {
            SocketAddress btsa;

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                // sockaddr_rc
                // .NET Core on Linux doesn't allow you to create a SocketAddress with AddressFamilyBlueZ so create an IP4 address then change the raw address family value.
                btsa = new SocketAddress(AddressFamily.InterNetwork, 10);
                btsa[0] = AddressFamilyBlueZ;

                Console.WriteLine($"Address Family: {btsa.Family}"); // Will display "Unknown" but as long as the raw value is 31 this will work okay.
                Console.WriteLine($"Address Byte: {btsa[0]}");
                Console.WriteLine($"Size: {btsa.Size}");
            }
            else
            {
                btsa = new SocketAddress(AddressFamily, 30);
            }

            // copy device id
            byte[] deviceidbytes = BitConverter.GetBytes(_bluetoothAddress);

            for (int idbyte = 0; idbyte < 6; idbyte++)
            {
                btsa[idbyte + 2] = deviceidbytes[idbyte];
            }

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                if (_port == -1)
                {
                    btsa[8] = 0;
                }
                else
                {
                    btsa[8] = (byte)_port;
                }
            }
            else
            {
                // copy service clsid
                if (_serviceId != Guid.Empty)
                {
                    byte[] servicebytes = _serviceId.ToByteArray();

                    for (int servicebyte = 0; servicebyte < 16; servicebyte++)
                    {
                        btsa[servicebyte + 10] = servicebytes[servicebyte];
                    }
                }

                var portBytes = BitConverter.GetBytes(_port);
                for (int i = 0; i < portBytes.Length; i++)
                {
                    btsa[26 + i] = portBytes[i];
                }
            }

            DebugWriteSocketAddress(btsa);

            return btsa;
        }

        /// <summary>
        /// Returns the string representation of the BluetoothEndPoint.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"{_bluetoothAddress:X6}:{_serviceId:D} ({_port})";
        }

        [Conditional("DEBUG")]
        private static void DebugWriteSocketAddress(SocketAddress sa)
        {
            Debug.WriteLine("SocketAddress:");
            Debug.Indent();
            for (int i = 0; i < sa.Size; i++)
            {
                Debug.Write(sa[i].ToString("X2"));
            }

            Debug.WriteLine(string.Empty);
            Debug.Unindent();
        }
    }
}
