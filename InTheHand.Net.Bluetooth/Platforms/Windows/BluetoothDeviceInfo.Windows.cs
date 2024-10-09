// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.Sockets.BluetoothDeviceInfo (WinRT)
// 
// Copyright (c) 2003-2024 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

using InTheHand.Net.Bluetooth;
using InTheHand.Net.Bluetooth.AttributeIds;
using InTheHand.Net.Bluetooth.Sdp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;

namespace InTheHand.Net.Sockets
{
    internal sealed class WindowsBluetoothDeviceInfo : IBluetoothDeviceInfo
    {
        internal BluetoothDevice NativeDevice;

        internal WindowsBluetoothDeviceInfo(BluetoothAddress address)
        {
            Task t = Task.Run(async () =>
            {
                NativeDevice = await BluetoothDevice.FromBluetoothAddressAsync(address.ToUInt64());
            });

            t.Wait();
        }

        internal WindowsBluetoothDeviceInfo(BluetoothDevice device)
        {
            NativeDevice = device;
        }

        public static implicit operator BluetoothDevice(WindowsBluetoothDeviceInfo device)
        {
            return device.NativeDevice;
        }

        public static implicit operator WindowsBluetoothDeviceInfo(BluetoothDevice device)
        {
            return new WindowsBluetoothDeviceInfo(device);
        }

        public BluetoothAddress DeviceAddress { get => NativeDevice.BluetoothAddress; }

        public string DeviceName { get => NativeDevice.Name; }

        public ClassOfDevice ClassOfDevice { get => (ClassOfDevice)NativeDevice.ClassOfDevice.RawValue; }

        public async Task<IEnumerable<Guid>> GetL2CapServicesAsync(bool cached)
        {
            List<Guid> services = new List<Guid>();

            foreach (var sdpRecord in NativeDevice.SdpRecords)
            {
                Guid protocolUUID = Guid.Empty;

                ServiceRecord attributes = ServiceRecord.CreateServiceRecordFromBytes(sdpRecord.ToArray());

                var attribute = attributes.GetAttributeById(UniversalAttributeId.ProtocolDescriptorList);
                if (attribute != null)
                {
                    try
                    {
                        var vals = attribute.Value.
                        GetValueAsElementList().
                        FirstOrDefault().
                        GetValueAsElementList();

                        // values in a list from most to least specific so read the first entry
                        var mostSpecific = vals.FirstOrDefault();
                        // short ids are automatically converted to a long Guid
                        protocolUUID = mostSpecific.GetValueAsUuid();
                    }
                    catch { protocolUUID = Guid.Empty; }
                }

                if (protocolUUID != BluetoothProtocol.L2CapProtocol)
                    continue;

                attribute = attributes.GetAttributeById(UniversalAttributeId.ServiceClassIdList);
                if (attribute != null)
                {
                    try
                    {
                        var vals = attribute.Value.GetValueAsElementList();
                        // values in a list from most to least specific so read the first entry
                        var mostSpecific = vals.FirstOrDefault();
                        // short ids are automatically converted to a long Guid
                        var guid = mostSpecific.GetValueAsUuid();

                        services.Add(guid);
                    }
                    catch { }
                }
            }

            return services;
        }

        public async Task<IEnumerable<Guid>> GetRfcommServicesAsync(bool cached)
        {
            List<Guid> services = new List<Guid>();

            var servicesResult = await NativeDevice.GetRfcommServicesAsync(cached ? BluetoothCacheMode.Cached : BluetoothCacheMode.Uncached);

            if (servicesResult != null && servicesResult.Error == BluetoothError.Success)
            {
                foreach (var service in servicesResult.Services)
                {
                    services.Add(service.ServiceId.Uuid);
                }
            }

            return services;
        }

        void IBluetoothDeviceInfo.Refresh() { }

        public bool Connected { get => NativeDevice.ConnectionStatus == BluetoothConnectionStatus.Connected; }

        public bool Authenticated { get => NativeDevice.DeviceInformation.Pairing.IsPaired; }
    }
}