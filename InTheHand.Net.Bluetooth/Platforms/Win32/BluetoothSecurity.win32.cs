﻿// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.Bluetooth.BluetoothSecurity (Win32)
// 
// Copyright (c) 2003-2023 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

using InTheHand.Net.Bluetooth.Win32;
using InTheHand.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace InTheHand.Net.Bluetooth
{
    internal sealed class Win32BluetoothSecurity : IBluetoothSecurity
    {
        private PairRequestCallBackFunc _usercallback = null;
        private static readonly List<Win32BluetoothAuthentication> _authenticationHandlers = new List<Win32BluetoothAuthentication>();

        public bool PairRequest(BluetoothAddress device, string pin, bool? requireMitmProtection, PairRequestCallBackFunc usercallback = null)
        {
            if (pin != null)
            {
                if (pin.Length > BLUETOOTH_PIN_INFO.BTH_MAX_PIN_SIZE)
                    throw new ArgumentOutOfRangeException(nameof(pin));
            }

            _usercallback = usercallback;

            BLUETOOTH_DEVICE_INFO info = new BLUETOOTH_DEVICE_INFO();
            info.dwSize = Marshal.SizeOf(info);
            info.Address = device;

            RemoveRedundantAuthHandler(device);

            NativeMethods.BluetoothGetDeviceInfo(IntPtr.Zero, ref info);
            // don't wait on this process if already paired
            if (info.fAuthenticated)
                return true;

            var authHandler = new Win32BluetoothAuthentication(device, pin, _usercallback);

            // Handle response without prompt
            _authenticationHandlers.Add(authHandler);

            BluetoothAuthenticationRequirements mitmProtection = BluetoothAuthenticationRequirements.MITMProtectionRequiredBonding;
            if (requireMitmProtection == false)
            {
                mitmProtection = BluetoothAuthenticationRequirements.MITMProtectionNotRequiredBonding;
            }

            bool success = NativeMethods.BluetoothAuthenticateDeviceEx(IntPtr.Zero, IntPtr.Zero, ref info, null, mitmProtection) == 0;

            if (!success)
            {
                _authenticationHandlers.Remove(authHandler);
                return false;
            }

            authHandler.WaitOne();
            BluetoothDeviceInfo deviceInfo = new BluetoothDeviceInfo(new Win32BluetoothDeviceInfo(info));
            deviceInfo.Refresh();

            // On Windows 7 these services are not automatically activated
            if (deviceInfo.ClassOfDevice.Device == DeviceClass.AudioVideoHeadset || deviceInfo.ClassOfDevice.Device == DeviceClass.AudioVideoHandsFree)
            {
                deviceInfo.SetServiceState(BluetoothService.Headset, true);
                deviceInfo.SetServiceState(BluetoothService.Handsfree, true);
            }

            return success;
        }

        internal static void RemoveRedundantAuthHandler(ulong address)
        {
            Win32BluetoothAuthentication redundantAuth = null;

            foreach (Win32BluetoothAuthentication authHandler in _authenticationHandlers)
            {
                if (authHandler.Address == address)
                {
                    redundantAuth = authHandler;
                    break;
                }
            }

            if (redundantAuth != null)
            {
                _authenticationHandlers.Remove(redundantAuth);
                redundantAuth.Dispose();
            }
        }

        public bool RemoveDevice(BluetoothAddress device)
        {
            ulong addr = device;
            RemoveRedundantAuthHandler(addr);
            return NativeMethods.BluetoothRemoveDevice(ref addr) == 0;
        }
    }
}
