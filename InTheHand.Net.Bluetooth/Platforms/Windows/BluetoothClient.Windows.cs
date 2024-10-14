// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.Sockets.BluetoothClient (WinRT)
// 
// Copyright (c) 2018-2024 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

using InTheHand.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;

namespace InTheHand.Net.Bluetooth.Platforms.Windows
{
    internal sealed class WindowsBluetoothClient : IBluetoothClient
    {
        private StreamSocket _streamSocket;

        internal WindowsBluetoothClient() { }

        internal WindowsBluetoothClient(StreamSocket socket)
        {
            _streamSocket = socket;
        }

        public IEnumerable<BluetoothDeviceInfo> PairedDevices
        {
            get
            {
                global::Windows.Foundation.IAsyncOperation<DeviceInformationCollection> t = DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));

                _ = t.AsTask().ConfigureAwait(false);
                t.AsTask().Wait();
                DeviceInformationCollection devices = t.GetResults();

                foreach (DeviceInformation device in devices)
                {
                    Task<BluetoothDevice> td = BluetoothDevice.FromIdAsync(device.Id).AsTask();
                    td.Wait();
                    BluetoothDevice bluetoothDevice = td.Result;
                    yield return new BluetoothDeviceInfo(new WindowsBluetoothDeviceInfo(bluetoothDevice));
                }
            }
        }

        public BluetoothDeviceInfo DiscoverDeviceByAddress(string address, bool issueInquiryIfNotFound)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyCollection<BluetoothDeviceInfo> DiscoverDevices(int maxDevices)
        {
            List<BluetoothDeviceInfo> results = [];

            DeviceInformationCollection devices = Threading.Tasks.AsyncHelpers.RunSync(async () =>
            {
                return await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(false));
            });

            foreach (DeviceInformation device in devices)
            {
                BluetoothDevice bluetoothDevice = Threading.Tasks.AsyncHelpers.RunSync(async () =>
                {
                    return await BluetoothDevice.FromIdAsync(device.Id);
                });
                results.Add(new BluetoothDeviceInfo(new WindowsBluetoothDeviceInfo(bluetoothDevice)));
            }
            return results.AsReadOnly();
        }

#if NET6_0_OR_GREATER
        public async Task<BluetoothDeviceInfo> DiscoverDeviceByAddressAsync(string address, bool issueInquiryIfNotFound, CancellationToken token)
        {
            ulong parsedAddress = BluetoothAddress.Parse(address);
            BluetoothDevice device = await BluetoothDevice.FromBluetoothAddressAsync(parsedAddress);
            if (device == null)
            {
                if (issueInquiryIfNotFound)
                {
                    await foreach (BluetoothDeviceInfo deviceDiscovered in DiscoverDevicesAsync(token))
                    {
                        if (device.BluetoothAddress == parsedAddress)
                        {
                            return deviceDiscovered;
                        }
                    }
                }
                else
                {
                    throw new ArgumentException($"Device with address {address} is not found");
                }
            }

            return new BluetoothDeviceInfo(new WindowsBluetoothDeviceInfo(device));
        }

        public async IAsyncEnumerable<BluetoothDeviceInfo> DiscoverDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(false)).AsTask(cancellationToken);

            foreach (DeviceInformation device in devices)
            {
                yield return new BluetoothDeviceInfo(new WindowsBluetoothDeviceInfo(await BluetoothDevice.FromIdAsync(device.Id)));
            }
        }
#endif

        public async Task ConnectAsync(BluetoothAddress address, Guid service)
        {
            BluetoothDevice device = await BluetoothDevice.FromBluetoothAddressAsync(address);
            RfcommDeviceServicesResult rfcommServices = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(service), BluetoothCacheMode.Uncached);

            if (rfcommServices.Error == BluetoothError.Success)
            {
                RfcommDeviceService rfCommService = rfcommServices.Services[0];
                _streamSocket = new StreamSocket();
                await _streamSocket.ConnectAsync(rfCommService.ConnectionHostName, rfCommService.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            }
        }

        public void Connect(BluetoothAddress address, Guid service)
        {
            Task t = Task.Run(async () =>
            {
                await ConnectAsync(address, service);
            });

            t.Wait();
        }

        /// <summary>
        /// Connects the client to a remote Bluetooth host using the specified endpoint.
        /// </summary>
        /// <param name="remoteEP">The <see cref="BluetoothEndPoint"/> to which you intend to connect.</param>
        public void Connect(BluetoothEndPoint remoteEP)
        {
            if (remoteEP == null)
            {
                throw new ArgumentNullException(nameof(remoteEP));
            }

            Connect(remoteEP.Address, remoteEP.Service);
        }

        public void Close()
        {
            if (_streamSocket != null)
            {
                _streamSocket.Dispose();
                _streamSocket = null;
            }
        }

        public bool Authenticate { get; set; } = false;

        Socket IBluetoothClient.Client => null;

        public bool Connected => _streamSocket != null;

        bool IBluetoothClient.Encrypt { get => false; set => throw new PlatformNotSupportedException(); }

        TimeSpan IBluetoothClient.InquiryLength { get => TimeSpan.Zero; set => throw new PlatformNotSupportedException(); }

        public string RemoteMachineName => Connected ? _streamSocket.Information.RemoteHostName.DisplayName : string.Empty;

        public NetworkStream GetStream()
        {
            return Connected ? new WinRTNetworkStream(_streamSocket, true) : (NetworkStream)null;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}