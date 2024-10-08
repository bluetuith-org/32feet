// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.Bluetooth.BluetoothProtocolDescriptorType
// 
// Copyright (c) 2003-2020 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

namespace InTheHand.Net.Bluetooth.Sdp
{
    /// <summary>
    /// Configures what type of element will be added by the <see cref="T:InTheHand.Net.Bluetooth.ServiceRecordBuilder"/>
    /// for the <see cref="F:InTheHand.Net.Bluetooth.AttributeIds.UniversalAttributeId.ProtocolDescriptorList"/> 
    /// attribute.
    /// </summary>
    /// -
    /// <remarks><para>Used with the <see cref="P:InTheHand.Net.Bluetooth.ServiceRecordBuilder.ProtocolType"/>
    /// property.
    /// </para>
    /// </remarks>
    public enum BluetoothProtocolDescriptorType
    {
        /// <summary>
        /// No PDL attribute will be added.
        /// </summary>
        None,

        /// <summary>
        /// A standard L2CAP element will be added.
        /// </summary>
        L2Cap,

        /// <summary>
        /// A standard RFCOMM element will be added.
        /// </summary>
        Rfcomm,

        /// <summary>
        /// A standard GOEP (OBEX) element will be added.
        /// </summary>
        GeneralObex
    }
}
