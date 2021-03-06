﻿using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using GatewayHelper.Exceptions;
using GatewayHelper.Native;
using GatewayHelper.Native.Constants;
using GatewayHelper.Native.Structures;

namespace GatewayHelper
{
    public static class GatewayUtility
    {
        /// <summary>
        /// Retrieves the IPv4 routing table
        /// </summary>
        /// <param name="forwardTable">The IPv4 routing table</param>
        /// <exception cref="OutOfMemoryException">Could not allocate a buffer that can store the routing table</exception>
        /// <exception cref="EmptyRouteTableException">There are no routes in the routing table</exception>
        /// <exception cref="NotSupportedException">There is no IP stack installed</exception>
        /// <exception cref="Win32Exception">Unexpected error</exception>
        private static void GetForwardTable(out IpForwardRow[] forwardTable)
        {
            IntPtr buffer = IntPtr.Zero;
            IntPtr bufferSize = IntPtr.Zero;

            try
            {
                var status = NativeLibrary.IPHelper.GetIpForwardTable(buffer, ref bufferSize, false);
                if (status == Error.InsufficientBuffer)
                {
                    buffer = Marshal.AllocHGlobal(bufferSize);
                    status = NativeLibrary.IPHelper.GetIpForwardTable(buffer, ref bufferSize, false);
                }

                if (status != Error.None)
                {
                    if (buffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                    
                    if (status == Error.NoData)
                        throw new EmptyRouteTableException();
                    if (status == Error.NotSupported)
                        throw new NotSupportedException("There is no IP stack installed on the local computer.");

                    throw new Win32Exception(status);
                }

                var size = (uint) Marshal.ReadInt32(buffer);
                forwardTable = new IpForwardRow[size];

                IntPtr currentPointer = buffer + sizeof(uint);
                for (int i = 0; i < size; i++)
                {
                    forwardTable[i] = Marshal.PtrToStructure<IpForwardRow>(currentPointer);
                    currentPointer += Marshal.SizeOf<IpForwardRow>();
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Retrieves all the gateways on the IPv4 routing table
        /// </summary>
        /// <returns>An array of <see cref="Gateway"/> containing every found gateway</returns>
        /// <exception cref="OutOfMemoryException">Could not allocate a buffer that can store the routing table</exception>
        /// <exception cref="EmptyRouteTableException">There are no routes in the routing table</exception>
        /// <exception cref="NotSupportedException">There is no IP stack installed</exception>
        /// <exception cref="Win32Exception">Unexpected error</exception>
        public static Gateway[] GetGateways()
        {
            GetForwardTable(out IpForwardRow[] forwardTable);

            var gateways = new Gateway[0];
            foreach (var row in forwardTable)
            {
                if (row.Destination == 0)
                {
                    var array = gateways;
                    gateways = new Gateway[gateways.Length + 1];
                    Array.Copy(array, 0, gateways, 1, array.Length);
                    
                    gateways[0] = new Gateway(row.Gateway);
                }
            }
            return gateways;
        }

        /// <summary>
        /// Deletes every gateway entry from the IPv4 routing table and creates a new one with the specified <paramref name="gateway"/> 
        /// </summary>
        /// <param name="gateway">The gateway that will be set</param>
        /// <exception cref="OutOfMemoryException">Could not allocate a buffer that can store the routing table</exception>
        /// <exception cref="EmptyRouteTableException">There are no routes in the routing table</exception>
        /// <exception cref="NotSupportedException">There is no IP stack installed on the local computer</exception>
        /// <exception cref="GatewayNotFoundException">There are no gateway entries in the routing table</exception>
        /// <exception cref="UnauthorizedAccessException">The application is not running in an enhanced shell</exception>
        /// <exception cref="NotSupportedException">The IPv4 transport is not configured on the local computer</exception>
        /// <exception cref="Win32Exception">Unexpected error</exception>
        public static void ChangeGateway(in Gateway gateway)
        {
            GetForwardTable(out IpForwardRow[] forwardTable);
            
            int status;
            IpForwardRow? currentGateway = null;
            for (int i = 0; i < forwardTable.Length; i++)
            {
                var row = forwardTable[i];
                if (row.Destination == 0)
                {
                    if (currentGateway == null)
                        currentGateway = row;
                        
                    status = NativeLibrary.IPHelper.DeleteIpForwardEntry(ref forwardTable[i]);

                    if (status == Error.AccessDenied)
                        throw new UnauthorizedAccessException();
                    if (status == Error.NotSupported)
                        throw new NotSupportedException("The IPv4 transport is not configured on the local computer.");
                    
                    if (status != Error.None)
                        throw new Win32Exception(status);
                }
            }

            if (!currentGateway.HasValue)
                throw new GatewayNotFoundException();

            var current = currentGateway.Value;
            current.Gateway = gateway.Address;
            
            status = NativeLibrary.IPHelper.CreateIpForwardEntry(ref current);

            if (status != Error.None)
            {
                if (status == Error.AccessDenied)
                    throw new UnauthorizedAccessException();
                if (status == Error.NotSupported)
                    throw new NotSupportedException("The IPv4 transport is not configured on the local computer.");
                
                throw new Win32Exception(status);
            }
        }
    }
}